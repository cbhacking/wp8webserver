/*
 * HttpServer\Response.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.5.0
 * Source: https://wp8webserver.codeplex.com
 *
 * Template to construct an HTTP response.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpServer
{
	public class HttpResponse
	{
		Socket socket;
		HttpVersion version;
		HttpStatusCode status;
		String contenttype;
		Dictionary<String, String> headers;
		byte[] content;

		#region Constructors
		public HttpResponse (Socket sock, HttpStatusCode stat, String type, byte[] cont, HttpVersion vers = HttpVersion.ONE_POINT_ONE)
		{
			socket = sock;
			version = vers;
			status = stat;
			contenttype = type;
			headers = new Dictionary<String, String>();
			content = cont;
		}

		public HttpResponse (Socket sock, HttpStatusCode stat, String type, String cont, HttpVersion vers = HttpVersion.ONE_POINT_ONE)
			: this(sock, stat, type, Encoding.UTF8.GetBytes(cont), vers)
		{
		}

		/// <summary>
		/// Create a redirection (HTTP 302) response. Does not send the response.
		/// </summary>
		/// <param name="sock">The open, connected TCP socket through which the response will eventually be sent.</param>
		/// <param name="redir">The URI to which the browser should be redirected. Can be absolute or relative.</param>
		/// <param name="vers">The HTTP version to use for the response. Optional (defaults to 1.1)</param>
		public HttpResponse (Socket sock, String redir, HttpVersion vers = HttpVersion.ONE_POINT_ONE)
			: this(sock, HttpStatusCode.Redirect, null, (byte[])null, vers)
		{
			headers["Location"] = redir;
		}

		/// <summary>
		/// Create a redirection (HTTP 302) response. Does not send the response.
		/// </summary>
		/// <param name="sock">The open, connected TCP socket through which the response will eventually be sent.</param>
		/// <param name="redir">The URI to which the browser should be redirected. Can be absolute or relative.</param>
		/// <param name="vers">The HTTP version to use for the response. Optional (defaults to 1.1)</param>
		public HttpResponse (Socket sock, Uri redir, HttpVersion vers = HttpVersion.ONE_POINT_ONE)
			: this(sock, redir.OriginalString, vers)
		{
		}
		#endregion

		private byte[] buildResponse ()
		{
			StringBuilder build = new StringBuilder();
			// Build the first line
			build.Append(Utility.VERSIONS[(int)version]).Append(' ');
			build.Append((int)status).Append(' ').AppendLine(status.ToString());
			// Add/update the standard headers
			if (null != contenttype)
			{
				headers["Content-Type"] = contenttype;
			}
			if (null != content)
			{
				headers["Content-Length"] = content.Length.ToString();
			}
			headers["Date"] = DateTime.UtcNow.ToString("R");
			// Put all headers in the response string, including custom ones
			foreach (String header in headers.Keys)
			{
				build.Append(header).Append(": ").AppendLine(headers[header]);
			}
			build.AppendLine(); // Empty line to terminate the headers
			String head = build.ToString();
			// Build the array
			if (null != content)
			{
				byte[] headbytes = Encoding.UTF8.GetBytes(head);
				int headlen = headbytes.Length;
				byte[] ret = new byte[headlen + content.Length];
				Array.Copy(Encoding.UTF8.GetBytes(head), ret, headlen);
				Array.Copy(content, 0, ret, headlen, content.Length);
				return ret;
			}
			else
			{
				return Encoding.UTF8.GetBytes(head);
			}
		}

		public Dictionary<String, String> Headers
		{
			get { return headers; }
			set { headers = value; }
		}

		public void SetHeader (String name, String value)
		{
			headers[name] = value;
		}

		/// <summary>
//		/// Sends the HTTP response asynchronously, then closes the socket.
		/// Sends the HTTP response asynchronously, then leaves the socket open.
		/// </summary>
		public void Send ()
		{
			// TODO: Fix the Close option!
			this.Send(ConnectionPersistence.KEEP_ALIVE);
		}

		/// <summary>
		/// Sends to the HTTP response asynchronously, then closes the connection if not persistent
		/// </summary>
		/// <param name="persist">Sets or overrides exiting Connection header unless Unspecified,
		/// in which case observes existing header or default behavior for version</param>
		public void Send (ConnectionPersistence persist)
		{
			// By default, don't do anything with the connection after sending
			EventHandler<SocketAsyncEventArgs> finished = (sender, comp) => { };
			if (persist != ConnectionPersistence.UNSPECIFIED)
			{	// There's some value for "persist", use it
				headers["Connection"] = Utility.PERSISTENCE[(int)persist];
				if (ConnectionPersistence.CLOSE == persist)
				{	// Close the connection after send is complete
					finished = (sender, comp) => { socket.Close(); };
				}
			}
			else if (headers.ContainsKey("Connection"))
			{	// Figure out what to do with the connection
				String conn = headers["Connection"];
				if (conn.Equals(Utility.PERSISTENCE[(int)ConnectionPersistence.KEEP_ALIVE], StringComparison.OrdinalIgnoreCase))
				{
					persist = ConnectionPersistence.KEEP_ALIVE;
				}
				else if (conn.Equals(Utility.PERSISTENCE[(int)ConnectionPersistence.CLOSE], StringComparison.OrdinalIgnoreCase))
				{
					finished = (sender, comp) => { socket.Close(); };
				}
				else if (this.version != HttpVersion.ONE_POINT_ONE)
				{	// Default to close for pre-1.1
					finished = (sender, comp) => { socket.Close(); };
				}
			}
			// If that falls through, there's no Connection header
			else if (this.version != HttpVersion.ONE_POINT_ONE)
			{	// Default to close for pre-1.1
				finished = (sender, comp) => { socket.Close(); };
			}
			byte[] resp = buildResponse();
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.SetBuffer(resp, 0, resp.Length);
			args.Completed += finished;
			socket.SendBufferSize = resp.Length;
			if (!socket.SendAsync(args))
				finished(null, null);
		}

		/// <summary>
		/// Builds and sends the HTTP response header block but does not send any content or close the socket.
		/// Useful when an amount of data too large for a single send (such as a large file) needs to be sent.
		/// </summary>
		/// <param name="contentlength">Desired value of the Content-Length header. Header won't be added if 0</param>
		public void SendHeaders (ulong contentlength)
		{
			AutoResetEvent reset = new AutoResetEvent(false);
			if (contentlength > 0UL)
			{
				headers["Content-Length"] = contentlength.ToString();
			}
			byte[] resp = buildResponse();
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.SetBuffer(resp, 0, resp.Length);
			args.Completed += (sender, comp) => { reset.Set(); };
			socket.SendBufferSize = resp.Length;
			if (socket.SendAsync(args))
				reset.WaitOne();
		}

		#region Static methods
		/// <summary>
		/// Generates an HTTP redirection response, sends it asynchronously, then closes the socket.
		/// </summary>
		/// <param name="sock">The socket to send the response over. Will get closed by this call.</param>
		/// <param name="version">The HTTP version to use. Defaults to 1.1.</param>
		/// <param name="url">The address to which the browser should be redirected.</param>
		public static void Redirect (Socket sock, Uri url, HttpVersion version = HttpVersion.ONE_POINT_ONE)
		{
			new HttpResponse(sock, url, version).Send();
		}

		/// <summary>
		/// Generates an HTTP redirection response, sends it, and closes the socket.
		/// </summary>
		/// <param name="sock">The socket to send the response over. Will get closed by this call.</param>
		/// <param name="version">The HTTP version to use. Defaults to 1.1.</param>
		/// <param name="url">The address to which the browser should be redirected.</param>
		public static void Redirect (Socket sock, String url, HttpVersion version = HttpVersion.ONE_POINT_ONE)
		{
			new HttpResponse(sock, url, version).Send();
		}
		#endregion
	}
}
