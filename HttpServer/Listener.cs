/*
 * HttpServer\Listener.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.0
 * Source: https://wp8webserver.codeplex.com
 *
 * Implements the listener portion of an HTTP server.
 */

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;

namespace HttpServer
{
	public delegate void RequestServicer (HttpRequest request, Socket socket);

	/// <summary>
	/// Implements the listener half of a basic UTF-8 HTTP server.
	/// </summary>
	public class WebServer
	{
		Socket serversock;
		Thread listenthread;
		RequestServicer servicer;

		/// <summary>
		/// Starts a new WebServer that listens on all connections at the specified port.
		/// The WebServer will begin listening immediately after construction.
		/// </summary>
		/// <remarks>
		/// Normally, incoming connections are only possible on WiFi networks.
		/// Each connection gets its own thread.
		/// </remarks>
		/// <param name="port">The TCP port to listen on</param>
		/// <param name="serv">The function which handles received requests.</param>
		public WebServer (ushort port, RequestServicer serv)
		{
			servicer = serv;
			serversock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			EndPoint local = new IPEndPoint(IPAddress.Any, port);
			serversock.Bind(local);
			serversock.Listen(5);
			listenthread = new Thread(listener);
			listenthread.Start();
		}

		~WebServer ()
		{
			listenthread.Abort();
			serversock.Shutdown(SocketShutdown.Both);
			serversock.Close();
		}

		private void listener ()
		{
			while (true)
			{
				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += accepter;
				if (!serversock.AcceptAsync(args))
				{
					// The operation completed synchronously, so it didn't raise the event
					accepter(this, args);
				}
			}
		}

		private void accepter (Object sender, SocketAsyncEventArgs args)
		{
			new Thread(handler).Start(args.AcceptSocket);
		}

		/// <summary>
		/// Receives the incoming request and processes it
		/// </summary>
		/// <param name="o">The network socket</param>
		private void handler (Object o)
		{
			Socket sock = (Socket)o;
			sock.ReceiveBufferSize = (1 << 20); // Use a 1MB buffer
			String data = "";
			HttpRequest request;
			do
			{
				// Get initial data from the socket
				data += getData(sock);
				request = new HttpRequest(ref data);
				while (!request.Complete)
				{
					// We need more data
					data += getData(sock);
					data = request.Continue(data);
				}
				// OK *that* request is done now
				try
				{
					servicer(request, sock);
				}
				catch (Exception ex)
				{
					HttpResponse resp = new HttpResponse(
						sock,
						request.Version,
						HttpStatusCode.InternalServerError,
						Utility.CONTENT_TYPES[(int)ResponseType.TEXT_PLAIN],
						"Internal Server Error!\n" + ex.ToString());
					resp.Send();
				}
				// But, there might have been more than one request in the last packet
			} while (!String.IsNullOrEmpty(data));
		}

		/// <summary>
		/// Retrieves waiting data on the socket. Will block if no data is available
		/// </summary>
		/// <param name="sock">The socket to read from</param>
		/// <returns></returns>
		private String getData (Socket sock)
		{
			EventWaitHandle wait = new EventWaitHandle(false, EventResetMode.AutoReset);
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			byte[] buffer = new byte[1 << 20];
			args.SetBuffer(buffer, 0, (1 << 20));
			args.Completed += (Object sender, SocketAsyncEventArgs args2) => { args = args2; wait.Set(); };
			if (sock.ReceiveAsync(args))
			{
				wait.WaitOne();
			}
			// At this point, we should have data
			if (SocketError.Success == args.SocketError)
			{
				return Encoding.UTF8.GetString(args.Buffer, 0, args.BytesTransferred);
			}
			else
			{
				throw new SocketException();
			}
		}
	}
}
