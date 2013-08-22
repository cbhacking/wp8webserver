/*
 * WebAccess\WebApplication.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.4.2
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
using System.Globalization;

using HttpServer;
using FileSystem;
using Registry;
using FileInfo = FileSystem.FileInfo;

namespace WebAccess
{
	public static class WebApplication
	{
		public static void ServiceRequest (HttpRequest req, Socket sock)
		{
			String content = null;
			String html;
			StringBuilder body;
			HttpStatusCode code = HttpStatusCode.OK;
			HttpResponse resp;

			if (req.Path.Equals("/Filesystem", StringComparison.InvariantCultureIgnoreCase))
			{
				// The Retrieve the requested file system resource and display it in the template
				try
				{
					content = serviceFilesystem(req, sock);

					if (null == content)
					{
						// This was handled entirely in the servicing function
						return;
					}
					body = readFile("Templates/Filesystem.htm");
					body.Replace("{CONTENT}", content);
					content = body.ToString();
				}
				catch (Exception ex)
				{
					code = HttpStatusCode.InternalServerError;
					body = readFile("Templates/Error.htm");
					body.Replace("{ERROR}", (int)code + " " + code.ToString())
						.Replace("{CONTENT}",
							"Unable to find the page \"" + req.Path +
							"\"<p>Exception info:<br />" + ex.ToString() + "</p>");
					content = body.ToString();
				}
			}
			else if (req.Path.StartsWith("/Registry", StringComparison.InvariantCultureIgnoreCase))
			{
				try
				{
					content = serviceRegistry(req, sock);
					if (null == content)
					{
						// This was handled entirely in the servicing function
						return;
					}
					body = readFile("Templates/Registry.htm");
					body.Replace("{CONTENT}", content);
					content = body.ToString();
				}
				catch (Exception ex)
				{
					code = HttpStatusCode.InternalServerError;
					body = readFile("Templates/Error.htm");
					body.Replace("{ERROR}", (int)code + " " + code.ToString())
						.Replace("{CONTENT}",
							"Unable to find the page \"" + req.Path +
							"\"<p>Exception info:<br />" + ex.ToString() + "</p>");
					content = body.ToString();
				}
			}
			else if (req.Path.Equals("/"))
			{
				// Go to the home page
				HttpResponse.Redirect(sock, new Uri("/Content/Index.htm", UriKind.Relative), req.Version);
				return;
			}
			else
			{
				// Retrieve the requested path (probably Content) and display it un-modified
				try
				{
					body = readFile(req.Path.Substring(1));
					resp = new HttpResponse(sock, HttpStatusCode.OK,
						Utility.CONTENT_TYPES[(int)ResponseType.TEXT_HTML], body.ToString(), req.Version);
					resp.Send();
				}
				catch (FileNotFoundException ex)
				{
					// The specified page doesn't exist; return 404
					code = HttpStatusCode.NotFound;
					body = readFile("Templates/Error.htm");
					body.Replace("{ERROR}", (int)code + " " + code.ToString())
						.Replace("{CONTENT}",
							"Unable to find the page \"" + req.Path + 
							"\"<p>Exception info:<br />" + ex.ToString() + "</p>");
					content = body.ToString();
				}
				return;
			}
			body = readFile("Templates/Master.htm");
			if (null != body)
			{
				if (null == content)
				{
					code = HttpStatusCode.BadRequest;
					StringBuilder error = readFile("Templates/Error.htm");
					error.Replace(" {ERROR}", "")
						.Replace("{CONTENT}", "Unknown error while servicing the request; no data returned");
					content = error.ToString();
				}
				body.Replace("{CONTENT}", content);
				html = body.ToString();
			}
			else
			{
				// Can't even open the master page!
				html = "ERROR! Unable to find .\\Templates\\Master.htm";
				code = HttpStatusCode.Gone;
			}
			resp = new HttpResponse(sock, code, Utility.CONTENT_TYPES[(int)ResponseType.TEXT_HTML],
				html, req.Version);
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

		private static String serviceRegistry (HttpRequest req, Socket sock)
		{
			if (req.UrlParameters.ContainsKey("hive") &&
				req.UrlParameters.ContainsKey("path"))
			{
				// Load the requested registry key
				RegistryHive hk = (RegistryHive)int.Parse(req.UrlParameters["hive"], NumberStyles.AllowHexSpecifier);
				String path = req.UrlParameters["path"];
				String[] subkeys;
				ValueInfo[] values;
				if (!NativeRegistry.GetSubKeyNames(hk, path, out subkeys))
				{
					// An error occurred!
					return null;
				}
				if (!NativeRegistry.GetValues(hk, path, out values))
				{
					return null;
				}
				// Build the HTML body
				StringBuilder build = new StringBuilder();
				if (subkeys != null && subkeys.Length > 0)
				{
					build.AppendLine("<table><tr><th>Keys</th></tr>");
					foreach (String key in subkeys)
					{
						build.Append("<tr><td><a href='/Registry?hive=").Append(((uint)hk).ToString("X"))
							.Append("&path=");
						if (!String.IsNullOrEmpty(path))
						{
							build.Append(HttpUtility.UrlEncode(path)).Append("\\");
						}
						build.Append(HttpUtility.UrlEncode(key)).Append("'>")
							.Append(key).AppendLine("</a></td></tr>");
					}
					build.AppendLine("</table>");
				}
				if (values != null && values.Length > 0)
				{
					build.AppendLine("<table><tr><th>Values</th><th>Type</th><th>Size</th><th>Data</th></tr>");
					foreach (ValueInfo info in values)
					{
						build.Append("<tr><td>").Append(info.Name).Append("</td><td>").Append(info.Type.ToString())
							.Append("</td><td>").Append(info.Length).AppendLine("</td><td>");
						if (RegistryType.String == info.Type || RegistryType.VariableString == info.Type)
						{
							String data;
							if (NativeRegistry.ReadString(hk, path, info.Name, out data))
							{
								build.Append(data);
							}
						}
						else if (RegistryType.Integer == info.Type)
						{
							uint data;
							if (NativeRegistry.ReadDWORD(hk, path, info.Name, out data))
							{
								build.Append(data);
							}
						}
						build.AppendLine("</td></tr>");
					}
					build.AppendLine("</table>");
				}
				return build.ToString();
			}
			else
			{
				// No request specified...
				return
					@"Specify a registry key above, or jump to the following examples:<br />
<a href='/Registry?hive=80000002&path=SOFTWARE\Microsoft\DeviceReg\Install'>Dev-unlock info</a><br />
<a href='/Registry?hive=80000001&path='>Current User registry hive</a><br />";
			}
		}

		private static StringBuilder readFile (String file)
		{
			String content = null;
			Stream stream;
			byte[] data;

			StreamResourceInfo sri = Application.GetResourceStream(new Uri(file, UriKind.Relative));
			if (null != sri)
			{
				stream = sri.Stream;
				data = new byte[stream.Length];
				stream.Read(data, 0, data.Length);
				content = Encoding.UTF8.GetString(data, 0, data.Length);
				return new StringBuilder(content);
			}
			return null;
		}
	}
}
