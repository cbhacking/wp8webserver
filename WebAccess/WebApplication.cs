/*
 * WebAccess\WebApplication.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.5.2
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
using Windows.ApplicationModel;
using Windows.Storage;

using HttpServer;
using FileSystem;
using Registry;
using FileInfo = FileSystem.FileInfo;
using nfs = FileSystem.NativeFileSystem;

namespace WebAccess
{
	public static class WebApplication
	{
		public static void ServiceRequest (HttpRequest req, Socket sock)
		{
			String content = null;
			String title = null;
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
				body.Replace("{TITLE}", (null == title) ? String.Empty : title);
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
			if (req.UrlParameters.ContainsKey("path"))
			{
				String path = req.UrlParameters["path"];
				if (String.IsNullOrWhiteSpace(path))
				{
					// Redirect to the root of the FileSystem node
					HttpResponse.Redirect(sock, new Uri("/Filesystem", UriKind.Relative), req.Version);
					return null;
				}
				if ('\\' != path[path.Length - 1])
					path += '\\';
				if (path.StartsWith(".\\"))
				{
					// Redirect to the actual path
					HttpResponse.Redirect(sock, new Uri(req.Path + "?path=" + 
						Package.Current.InstalledLocation.Path + (req.UrlParameters.ContainsKey("pattern") ?
						"&pattern=" + req.UrlParameters["pattern"] : String.Empty), UriKind.Relative),
						req.Version);
					return null;
				}
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
					resp.SendHeaders((ulong)info[0].Size);
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
						args.Completed += (sender, args2) => { reset.Set(); };
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
				String[] array = dirs.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				StringBuilder build = new StringBuilder("<table>");
				foreach (String name in array)
				{
					// Suppress . and treat .. specially
					if (name.Equals("."))
						continue;
					if (name.Equals(".."))
					{
						build.AppendFormat(
							"<tr><td>DIR</td><td><a href=\"/Filesystem?path={0}&pattern={2}\">{1}</a></td></tr>",
							Path.GetDirectoryName(path.Substring(0, path.Length - 1)), name, pattern);
					}
					else
					{
						build.AppendFormat(
							"<tr><td>DIR</td><td><a href=\"/Filesystem?path={0}{1}&pattern={2}\">{1}</a></td></tr>",
							path, name, pattern);
					}
				}
				// Get files, and hyperlink them for download
				FileInfo[] files = nfs.GetFiles(search, false);
				// Check to make sure that there just aren't any files, which apparently also returns null...
				if (null == files || (0 == files.Length))
				{
					if (nfs.GetError() != 0)
					{
						String error = "An error occurred while querying file list.<br />The error number is " +
						"<a href=\"http://msdn.microsoft.com/en-us/library/windows/desktop/ms681381(v=vs.85).aspx\">" +
							nfs.GetError() + "</a>";
						return error;
					}
					else
					{
						build.AppendLine("<h4>There are no files in this directory.</h4>");
					}
				}
				else
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
			{
				// Build the landing page for Filesystem
				String[] drives = nfs.GetDriveLetters();
				StringBuilder build = new StringBuilder("<h4>The root of the C: drive is generally inaccessible.</h4><ul>");
				foreach (String drive in drives)
				{
					if (drive.Equals("C:\\"))
						build.AppendLine("<li><a href=\"/Filesystem?path=C:\\Windows\\&pattern=*\">C:\\Windows\\</a></li>");
					else
					{
						build.AppendFormat("<li><a href=\"/Filesystem?path={0}&pattern=*\">{0}</a></li>\n", drive);
					}
				}
				build.AppendFormat("<li><a href=\"/Filesystem?path={0}&pattern=*\">App data directory</a></li>\n", ApplicationData.Current.LocalFolder.Path)
					.AppendFormat("<li><a href=\"/Filesystem?path={0}&pattern=*\">App install directory</a></li>\n", Package.Current.InstalledLocation.Path);
				build.AppendLine("</ul>");
				return build.ToString();
			}
		}

		private static String serviceRegistry (HttpRequest req, Socket sock)
		{
			if (req.UrlParameters.ContainsKey("hive") &&
				req.UrlParameters.ContainsKey("path"))
			{
				// Load the requested registry key
				RegistryHive hk = (RegistryHive)int.Parse(req.UrlParameters["hive"], NumberStyles.AllowHexSpecifier);
				String path = req.UrlParameters["path"].TrimEnd('\\');
				String[] subkeys;
				ValueInfo[] values;
				if (!NativeRegistry.GetSubKeyNames(hk, path, out subkeys))
				{
					// An error occurred!
					return "An error occurred while getting the registry sub-keys! The Win32 error code was " +
						NativeRegistry.GetError();
				}
				if (!NativeRegistry.GetValues(hk, path, out values))
				{
					return "An error occurred while getting the registry values! The Win32 error code was " +
						NativeRegistry.GetError();
				}
				// Build the HTML body
				StringBuilder build = new StringBuilder();
				if (!String.IsNullOrWhiteSpace(path))
				{
					build.AppendLine("<h4><a href=\"/Registry?hive=" + req.UrlParameters["hive"] + "&path=" +
						(path.Contains('\\') ? path.Substring(0, path.LastIndexOf('\\')) : String.Empty) +
						"\">Go to parent key</a></h4>");
				}
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
				else
				{
					build.AppendLine("<h4>This key has no subkeys.</h4>");
				}
				if (values != null && values.Length > 0)
				{
					build.AppendLine("<table border=\"1\"><tr><th>Values</th><th>Type</th><th>Size</th><th>Data</th></tr>");
					foreach (ValueInfo info in values)
					{
						String name = String.IsNullOrEmpty(info.Name) ? "<i>default</i>" : info.Name;
						build.Append("<tr><td>").Append(name).Append("</td><td>").Append(info.Type.ToString())
							.Append("</td><td>").Append(info.Length).AppendLine("</td><td>");
						switch (info.Type)
						{
						case RegistryType.String:
						case RegistryType.VariableString:
							{	// Make sure it's really a string; display binary otherwise
								if (0 != (info.Length % 2))
								{
									byte[] binary;
									if (NativeRegistry.ReadBinary(hk, path, info.Name, out binary))
									{
										if (null != binary)
										{
											buildHexTable(build, binary);
										}
										else
										{	// No data!
											build.Append("<i>NULL</i>");
										}
									}
									else
									{	// Error reading data
										build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
									}
									break;
								}
								// Handle REG_SZ and REG_EXPAND_SZ
								String data;
								if (NativeRegistry.ReadString(hk, path, info.Name, out data) && !String.IsNullOrEmpty(data))
								{
									build.Append(WebUtility.HtmlEncode(data));
								}
								break;
							}
						case RegistryType.Integer:
							{	// Make sure it's really a DWORD; display binary otherwise
								if (info.Length != 4)
								{
									byte[] binary;
									if (NativeRegistry.ReadBinary(hk, path, info.Name, out binary))
									{
										if (null != binary)
										{
											buildHexTable(build, binary);
										}
										else
										{	// No data!
											build.Append("<i>NULL</i>");
										}
									}
									else
									{	// Error reading data
										build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
									}
									break;
								}
								// Handle REG_DWORD
								uint data;
								if (NativeRegistry.ReadDWORD(hk, path, info.Name, out data))
								{
									build.Append(data).AppendFormat(" (0x{0:X8})", data);
								}
								break;
							}
						case RegistryType.Long:
							{	// Make sure it's really a QWORD; display binary otherwise
								if (info.Length != 8)
								{
									byte[] binary;
									if (NativeRegistry.ReadBinary(hk, path, info.Name, out binary))
									{
										if (null != binary)
										{
											buildHexTable(build, binary);
										}
										else
										{	// No data!
											build.Append("<i>NULL</i>");
										}
									}
									else
									{	// Error reading data
										build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
									}
									break;
								}
								// Handle REG_QWORD
								ulong data;
								if (NativeRegistry.ReadQWORD(hk, path, info.Name, out data))
								{
									try
									{
										DateTime date = DateTime.FromFileTime((long)data);
										build.Append(data).AppendFormat(" (0x{0:X16}) (", data).Append(date.ToString()).Append(')');
									}
									catch (ArgumentOutOfRangeException)
									{
										// It's not a date...
										build.Append(data).AppendFormat(" (0x{0:X16})", data);
									}
								}
								break;
							}
						case RegistryType.MultiString:
							{	// Make sure it's really a string; display binary otherwise
								if (0 != (info.Length % 2))
								{
									byte[] binary;
									if (NativeRegistry.ReadBinary(hk, path, info.Name, out binary))
									{
										if (null != binary)
										{
											buildHexTable(build, binary);
										}
										else
										{	// No data!
											build.Append("<i>NULL</i>");
										}
									}
									else
									{	// Error reading data
										build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
									}
									break;
								}
								// Handle REG_MULTI_SZ
								String[] data;
								if (NativeRegistry.ReadMultiString(hk, path, info.Name, out data)
									&& (data != null) && (data.Length > 0))
								{
									build.Append(WebUtility.HtmlEncode(data[0]));
									for (int i = 1; i < data.Length; i++)
									{
										build.AppendLine("<br />").Append(WebUtility.HtmlEncode(data[i]));
									}
								}
								break;
							}
						case RegistryType.Binary:
							{	// Handle REG_BINARY
								switch (info.Length)
								{
								case 4:
									{	// Treat it as a DWORD
										uint data;
										if (NativeRegistry.ReadDWORD(hk, path, info.Name, out data))
										{
											build.Append(data).AppendFormat(" (0x{0:X8})", data);
										}
										break;
									}
								case 8:
									{	// Treat it as a QWORD
										ulong data;
										if (NativeRegistry.ReadQWORD(hk, path, info.Name, out data))
										{
											try
											{
												DateTime date = DateTime.FromFileTime((long)data);
												build.Append(data).AppendFormat(" (0x{0:X16}) (", data).Append(date.ToString()).Append(')');
											}
											catch (ArgumentOutOfRangeException)
											{
												// It's not a date...
												build.Append(data).AppendFormat(" (0x{0:X16})", data);
											}
										}
										break;
									}
								default:
									{	// Display as a binary hex sequence
										byte[] data;
										if (NativeRegistry.ReadBinary(hk, path, info.Name, out data))
										{
											if (null != data)
											{
												buildHexTable(build, data);
											}
											else
											{	// No data!
												build.Append("<i>NULL</i>");
											}
										}
										else
										{	// Error reading data
											build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
										}
										break;
									}
								} // End of info.length switch
								break;
							}
						default:
							{	// Handle arbitrary value types
								byte[] data;
								RegistryType type;
								if (NativeRegistry.QueryValue(hk, path, info.Name, out type, out data))
								{
									if (null != data)
									{
										buildHexTable(build, data);
									}
									else
									{	// No data!
										build.Append("<i>NULL</i>");
									}
								}
								else
								{	// Error reading data
									build.AppendFormat("<i>Error reading data: {0} ({0:X})</i>", NativeRegistry.GetError());
								}
								break;
							}
						}	// End of info.type switch
						build.AppendLine("</td></tr>");
					}
					build.AppendLine("</table>");
				}
				else
				{
					build.AppendLine("<h4>This key contains no values.</h4>");
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
		
		private static void buildHexTable (StringBuilder build, byte[] data)
		{	// Use a table layout
			int linestart = 0;
			build.Append("<pre>");
			for (int i = 0; i < data.Length; i++)
			{
				build.AppendFormat("{0:X2} ", data[i]);
				if (15 == (i & 15))
				{
					build.Append("| ");
					// After every 16th value, print the text line
					for (; linestart <= i; linestart++)
					{
						if ((data[linestart] >= 32) && (data[linestart] < 127))
						{
							build.Append((char)(data[linestart]));
						}
						else
						{
							build.Append("&#xB7;");
						}
					}
					build.AppendLine();
				}
			}
			// The last line may not have been a full 16 bytes
			int emptybytes = 16 - (data.Length - linestart);
			if (emptybytes < 16)
			{
				build.Append(' ', 3 * emptybytes).Append("| ");
				for (; linestart < (data.Length); linestart++)
				{
					if ((data[linestart] >= 32) && (data[linestart] < 127))
					{
						build.Append((char)(data[linestart]));
					}
					else
					{
						build.Append("&#xB7;");
					}
				}
			}
			build.AppendLine("</pre>");
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
