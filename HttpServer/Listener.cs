/*
 * HttpServer\Listener.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.4.1
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
	public class WebServer : IDisposable
	{
		Socket serversock;
		Thread listenthread;
		RequestServicer servicer;
		CancellationTokenSource cancelsource;

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
		/// <exception cref="System.Net.Sockets.SocketException">Opening the socket for listening failed</exception>
		public WebServer (ushort port, RequestServicer serv)
		{
			servicer = serv;
			serversock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			EndPoint local = new IPEndPoint(IPAddress.Any, port);
			serversock.Bind(local);
			serversock.Listen(5);
			cancelsource = new CancellationTokenSource();
			// Use a different thread to handle connection attempts, so this one doesn't block
			listenthread = new Thread(listener);
			listenthread.Start();
		}

		~WebServer ()
		{
			Dispose(false);
		}

		protected virtual void Dispose (bool disposing)
		{
			Close();
			cancelsource.Dispose();
		}

		public void Dispose ()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public void Close ()
		{
			cancelsource.Cancel();
			if (listenthread.IsAlive)
			{
				listenthread.Abort();
			}
			if (serversock.Connected)
			{
				serversock.Shutdown(SocketShutdown.Both);
				serversock.Close();
			}
		}

		/// <summary>
		/// This function is called once, as the start function of the listen thread.
		/// It dispatches connection acceptances into their own threads.
		/// </summary>
		private void listener ()
		{
			AutoResetEvent acceptreset = new AutoResetEvent(false);
			while (!cancelsource.IsCancellationRequested)
			{
				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += (sender, completedargs) =>
				{
					accepter(completedargs);
					// Resume the listen loop
					acceptreset.Set();
				};
				if (serversock.AcceptAsync(args))
				{
					// Operation is pending, and will fire the Completed event
					acceptreset.WaitOne();
				}
				else
				{
					// Accepted synchronously, so it didn't raise the event
					accepter(args);
				}
			}
		}

		/// <summary>
		/// This function is called every time the server accepts a client connection.
		/// It runs in the thread created by AcceptAsync
		/// </summary>
		/// <param name="args"></param>
		private void accepter (SocketAsyncEventArgs args)
		{
            if (args.SocketError == SocketError.Success)
            {
				// This is already in its own thread; just call the handler directly
				//new Thread(handler).Start(args.AcceptSocket);
				handler(args.AcceptSocket);
			}
		}

		/// <summary>
		/// Receives the incoming request and processes it
		/// </summary>
		/// <param name="o">The network socket</param>
		private void handler (Object o)
		{
			Socket sock = (Socket)o;
			handler(sock);
		}

		/// <summary>
		/// Called after the client connection is established. Receives client request.
		/// </summary>
		/// <param name="sock">Connected TCP socket communicating with client</param>
		private void handler (Socket sock)
		{
			sock.ReceiveBufferSize = (1 << 20); // Use a 1MB buffer
			String data = "";
			do
			{
				// Get one request at a time
				HttpRequest request = new HttpRequest();
				try
				{
					do
					{
						// We need (more) data
						String newdata = getString(sock);
						if (null == newdata)
						{
							// The connection was closed gracefully
							return;
						}
						data += newdata;
						// Update/populate the request using the Continue function
						data = request.Continue(data);
						// Sanity-check what we've parsed so far
						if (HttpVersion.INVALID_VERSION == request.Version)
						{
							HttpResponse resp = new HttpResponse(
								sock,
								HttpStatusCode.HttpVersionNotSupported,
								Utility.CONTENT_TYPES[(int)ResponseType.TEXT_PLAIN],
								"The protocol version specified in the request is not recognized!\n");
							// Send the error report, then close the connection; we don't care what else the client wanted
							resp.Send();
							sock.Close(100);
							return;
						}
						if (HttpMethod.INVALID_METHOD == request.Method)
						{
							HttpResponse resp = new HttpResponse(
								sock,
								HttpStatusCode.NotImplemented,
								Utility.CONTENT_TYPES[(int)ResponseType.TEXT_PLAIN],
								"The request HTTP verb (method) is not recognized!\n",
								request.Version);
							// Send the error report, then close the connection; we don't care what else the client wanted
							resp.Send();
							sock.Close(100);
							return;
						}
					} while (!request.Complete);
					// OK *that* request is done now.
					servicer(request, sock);
				}
				catch (ProtocolViolationException ex)
				{
					if (sock.Connected)
					{
						HttpResponse resp = new HttpResponse(
							sock,
							HttpStatusCode.BadRequest,
							Utility.CONTENT_TYPES[(int)ResponseType.TEXT_PLAIN],
							"Bad request!\n" + ex.ToString());
						resp.Send();
						sock.Close(100);
					}
					return;
				}
				catch (Exception ex)
				{
					if (sock.Connected)
					{
						HttpResponse resp = new HttpResponse(
							sock,
							HttpStatusCode.InternalServerError,
							Utility.CONTENT_TYPES[(int)ResponseType.TEXT_PLAIN],
							"Internal Server Error!\n" + ex.ToString() + '\n' + ex.StackTrace);
						resp.Send();
						sock.Close(100);
					}
					return;
				}
				// But, there might have been more than one request in the last packet
			} while (sock.Connected);
		}

		/// <summary>
		/// Retrieves waiting data on the socket. Will block if no data is available
		/// </summary>
		/// <param name="sock">The socket to read from</param>
		/// <returns>The UTF-8 encoded string read from the socket, or NULL if connection closed</returns>
		private String getString (Socket sock)
		{
			AutoResetEvent wait = new AutoResetEvent(false);
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			byte[] buffer = new byte[1 << 20];
			args.SetBuffer(buffer, 0, (1 << 20));
			args.Completed += (Object sender, SocketAsyncEventArgs args2) => { args = args2; wait.Set(); };
			if (sock.ReceiveAsync(args))
			{
				// Receive function is executing in the background; sychronize it
				wait.WaitOne();
			}
			// At this point, we should have data
			if (SocketError.Success == args.SocketError)
			{
				// Apparently this can happen when the connection closed gracefully
				if (args.BytesTransferred > 0)
				{
					return Encoding.UTF8.GetString(args.Buffer, 0, args.BytesTransferred);
				}
				else
				{
					// This connection is closed; no more data will flow
					return null;
				}
			}
			else
			{
				throw new SocketException();
			}
		}

		private byte[] getBytes (Socket sock, int maxLen = (1 << 20))
		{
			AutoResetEvent wait = new AutoResetEvent(false);
			byte[] buffer = new byte[maxLen];
			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.SetBuffer(buffer, 0, maxLen);
			args.Completed += (Object s, SocketAsyncEventArgs a2) => { args = a2; wait.Set(); };
			if (sock.ReceiveAsync(args))
			{
				// It's running in the background; block until done
				wait.WaitOne();
			}
			// We get signal?
			if (SocketError.Success == args.SocketError)
			{
				int reclen = args.BytesTransferred;
				if (reclen > 0)
				{
					// We got *some* data
					if (reclen < maxLen)
					{
						// Shrink the returned buffer
						byte[] newbuf = new byte[reclen];
						Array.Copy(buffer, newbuf, reclen);
						return newbuf;
					}
					// We filled the buffer
					return buffer;
				}
				else
				{
					// Connection closed gracefully
					return null;
				}
			}
			else
			{
				throw new SocketException();
			}
		}
	}
}
