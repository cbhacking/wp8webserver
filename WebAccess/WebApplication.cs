/*
 * WebAccess\WebApplication.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.3
 * Source: https://wp8webserver.codeplex.com
 *
 * Handles GET requests from the web server.
 * Uses the NativeAccess project to access the file system.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Resources;
using FileSystem;
using HttpServer;
using FileInfo = FileSystem.FileInfo;

namespace WebAccess
{
	public static class WebApplication
	{
		public static void ServiceRequest (HttpRequest req, Socket sock)
		{
			String content = null;
			Stream stream;
			byte[] data;
			String body;
			HttpResponse resp;

			if (req.Path.Equals("/Filesystem", StringComparison.InvariantCultureIgnoreCase))
			{
				// The Retrieve the requested file system resource and display it in the template
				content = serviceFilesystem(req, sock);
				if (null == content)
					return;
			}
			if (req.Path.StartsWith("/Content/", StringComparison.InvariantCultureIgnoreCase))
			{
				// Retrieve the requested Content address and display it un-modified
				try
				{
					stream = Application.GetResourceStream(new Uri(req.Path.Substring(1), UriKind.Relative)).Stream;
					data = new byte[stream.Length];
					stream.Read(data, 0, data.Length);
					resp = new HttpResponse(sock, HttpStatusCode.OK,
						Utility.CONTENT_TYPES[(int)ResponseType.TEXT_HTML], data, req.Version);
					resp.Send();
				}
				catch (FileNotFoundException ex)
				{
					// The specified Content page doesn't exist; return 404
					stream = Application.GetResourceStream(new Uri("Templates/Error.htm", UriKind.Relative)).Stream;
					data = new byte[stream.Length];
					stream.Read(data, 0, data.Length);
					body = Encoding.UTF8.GetString(data, 0, data.Length);
					body = body.Replace("{ERROR}",
						(int)(HttpStatusCode.NotFound) + " " + HttpStatusCode.NotFound.ToString());
					body = body.Replace("{CONTENT}",
						"Unable to find the page \"" + req.Path + "\"</p><p>Exception info:<br />" + ex.ToString());
					resp = new HttpResponse(sock, HttpStatusCode.NotFound,
						Utility.CONTENT_TYPES[(int)ResponseType.TEXT_HTML], body, req.Version);
					resp.Send();
				}
				return;
			}
			if (req.Path.Equals("/"))
			{
				// Go to the home page
				HttpResponse.Redirect(sock, new Uri("/Content/Index.htm", UriKind.Relative), req.Version);
			}
			StreamResourceInfo sri = Application.GetResourceStream(new Uri("Templates/Filesystem.htm", UriKind.Relative));
			if (null != sri)
			{
				stream = sri.Stream;
				data = new byte[stream.Length];
				stream.Read(data, 0, data.Length);
				body = Encoding.UTF8.GetString(data, 0, data.Length);
				body = body.Replace("{CONTENT}",
					(null == content) ? "The requested URL path, \"" + req.Path + "\", was not found" : content);
				body = body.Replace("{PATH}",
					(req.UrlParameters.ContainsKey("path")) ? req.UrlParameters["path"] : "");
			}
			else
			{
				body = "ERROR! Unable to find .\\Templates\\Filesystem.htm";
			}
			resp = new HttpResponse(sock, HttpStatusCode.Gone, Utility.CONTENT_TYPES[(int)ResponseType.TEXT_HTML],
				body, req.Version);
			resp.Send();
			GC.Collect();
		}

		private static String serviceFilesystem (HttpRequest req, Socket sock)
		{
			NativeFileSystem nfs = new NativeFileSystem();
			if (req.UrlParameters.ContainsKey("path"))
			{
				String path = req.UrlParameters["path"];
				if ('\\' != path[path.Length - 1])
					path += '\\';
				// Check for file download before checking for search pattern
				if (req.UrlParameters.ContainsKey("download"))
				{
					String filename = req.UrlParameters["download"];
					String fullname = path + filename;
					// Get the length
					FileInfo[] info = nfs.GetFiles(fullname);
					if (null == info)
					{
						// An error occurred
						String error = "An error occurred while getting file information for download.<br />" +
							"The error number is <a href=\"" +
							"http://msdn.microsoft.com/en-us/library/windows/desktop/ms681381(v=vs.85).aspx\">" +
							nfs.GetError() + "</a>";
						return error;
					}
					// Create a file-download server response
					HttpResponse resp = new HttpResponse(sock, HttpStatusCode.OK, null, (byte[])null, req.Version);
					resp.Headers["Content-Disposition"] = "attachment; filename=\"" + filename + "\"";
					resp.SendHeaders(info[0].Size);
					// Read and send the file in chunks
					long offset = 0L;
					AutoResetEvent reset = new AutoResetEvent(true);
					while (offset < info[0].Size)
					{
						// Read the file in 4MB chunks, but wait until the last chunk is sent before sending again
						byte[] data = nfs.ReadFile(fullname, offset, 0x400000);
						if (null == data)
						{
							// An error occurred
							break;
						}
						reset.WaitOne();
						// Send while we read the next part of the file
						SocketAsyncEventArgs args = new SocketAsyncEventArgs();
						args.SetBuffer(data, 0, data.Length);
						args.Completed += (sender, args2) => {reset.Set();};
						if (!sock.SendAsync(args))
							reset.Set();
						offset += data.Length;
					}
					// Wait for the last data to be sent (in case of an error, wait two minutes) then close the socket
					reset.WaitOne(120000);
					sock.Close();
					return null;
				}
				// If we got here, not downloading. Assume listing directory contents
				String search = path;
				String pattern;
				if (req.UrlParameters.ContainsKey("pattern"))
					pattern = req.UrlParameters["pattern"];
				else
					pattern = "*";
				search += pattern;
				// Get folders, and hyperlink them for navigation
				String dirs = nfs.GetFileNames(search, false, true);
				if (null == dirs)
				{
					String error = "An error occurred while querying directory list.<br />The error number is " + 
					"<a href=\"http://msdn.microsoft.com/en-us/library/windows/desktop/ms681381(v=vs.85).aspx\">" +
						nfs.GetError() + "</a>";
					return error;
				}
				String[] array = dirs.Split(new char[] {'|'}, StringSplitOptions.RemoveEmptyEntries);
				StringBuilder build = new StringBuilder("<table>");
				foreach (String name in array)
				{
					build.AppendFormat(
						"<tr><td>DIR</td><td><a href=\"/Filesystem?path={0}{1}&pattern={2}\">{1}</a></td></tr>",
						path, name, pattern);
				}
				// Get files, and hyperlink them for download
				FileInfo[] files = nfs.GetFiles(search, false);
				// Check to make sure that there just aren't any files, which apparently also returns null...
				if ((null == files) && (nfs.GetError() != 0))
				{
					String error = "An error occurred while querying file list.<br />The error number is " +
					"<a href=\"http://msdn.microsoft.com/en-us/library/windows/desktop/ms681381(v=vs.85).aspx\">" +
						nfs.GetError() + "</a>";
					return error;
				}
				if (null != files)
				{
					foreach (FileInfo info in files)
					{
						build.AppendFormat(
							"<tr><td>{2}</td><td><a href=\"/Filesystem?path={0}&download={1}\">{1}</a></td></tr>",
							path, info.Name, info.Size);
					}
				}
				return build.Append("</table>").ToString();
			}
			else
				return null;	// No path, this should never happen...
		}
	}
}
