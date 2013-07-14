/*
 * HttpServer\Response.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.1
 * Source: https://wp8webserver.codeplex.com
 *
 * Tempate to construct an HTTP response. Not used directly by the server.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
		public HttpResponse (Socket sock, HttpVersion vers, HttpStatusCode stat, String type, byte[] cont)
		{
			socket = sock;
			version = vers;
			status = stat;
			contenttype = type;
			headers = new Dictionary<String, String>();
			content = cont;
		}

		public HttpResponse (Socket sock, HttpVersion vers, HttpStatusCode stat, String type, String cont)
			: this(sock, vers, stat, type, Encoding.UTF8.GetBytes(cont))
		{
		}

		public HttpResponse (Socket sock, HttpVersion vers, Uri redir)
			: this(sock, vers, HttpStatusCode.Redirect, null, (byte[])null)
		{
			headers["Location"] = redir.OriginalString;
		}
		#endregion

		private byte[] buildResponse ()
		{
			// Build the headers
			StringBuilder build = new StringBuilder();
			build.Append(HttpRequest.VERSIONS[(int)version]).Append(' ');
			build.Append((int)status).Append(' ').AppendLine(status.ToString());
			if (null != contenttype)
				build.Append("Content-Type: ").AppendLine(contenttype);
			foreach (String header in headers.Keys)
			{
				build.Append(header).Append(": ").AppendLine(headers[header]);
			}
			build.Append("Date: ").AppendLine(DateTime.UtcNow.ToString("R"));
			if (null != content)
			{
				build.Append("Content-Length: ").AppendLine(content.Length.ToString());
			}
			build.AppendLine(); // Empty line to terminate the headers
			String head = build.ToString();
			int headlen = Encoding.UTF8.GetByteCount(head);
			// Build the array
			if (null != content)
			{
				byte[] ret = new byte[headlen + content.Length];
				Array.Copy(Encoding.UTF8.GetBytes(head), ret, headlen);
				Array.Copy(content, 0, ret, headlen, content.Length);
				return ret;
			}
			else
			{
				return Encoding.UTF8.GetBytes(build.ToString());
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
		/// Sends the HTTP response asynchronously, then closes the socket.
		/// </summary>
		public void Send ()
		{
			byte[] resp = buildResponse();
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.SetBuffer(resp, 0, resp.Length);
			args.Completed += (sender, comp) => { socket.Close(); };
			socket.SendBufferSize = resp.Length;
			if (!socket.SendAsync(args))
				socket.Close();
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
			new HttpResponse(sock, version, url).Send();
		}

		/// <summary>
		/// Generates an HTTP redirection response, sends it, and closes the socket.
		/// </summary>
		/// <param name="sock">The socket to send the response over. Will get closed by this call.</param>
		/// <param name="version">The HTTP version to use. Defaults to 1.1.</param>
		/// <param name="url">The address to which the browser should be redirected.</param>
		public static void Redirect (Socket sock, String url, HttpVersion version = HttpVersion.ONE_POINT_ONE)
		{
			new HttpResponse(sock, version, new Uri(url)).Send();
		}
		#endregion
	}
}
