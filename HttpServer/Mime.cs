/*
 * HttpServer\Mime.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.5.1
 * Source: https://wp8webserver.codeplex.com
 *
 * Basic implementation of Multipart Internet Mail Extensions.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace HttpServer
{
	public class MimePart
	{
		Dictionary<String, String> headers;
		byte[] body;
		String bodyText;
		String name;
		String filename;
		String multipartboundry;
		MimePart[] bodyParts;
		int offset;
		int length;

		public Dictionary<String, String> Headers
		{
			get { return headers; }
		}

		public String Filename
		{
			get { return filename; }
		}

		public String Name
		{
			get { return name; }
		}

		public byte[] Body
		{
			get { return body; }
		}

		public String BodyText
		{
			get { return bodyText; }
		}

		public MimePart[] MimeParts
		{
			get { return bodyParts; }
		}

		private void parseHeaders (String[] lines)
		{
			if (null == headers)
			{
				headers = new Dictionary<String, String>(lines.Length);
			}
			foreach (String line in lines)
			{
				// First, check for a name/value delineator
				int valueIndex = line.IndexOf(':');
				String headerName, headerValue;
				if (valueIndex >= 0)
				{
					headerName = line.Substring(0, valueIndex).Trim();
					headerValue = line.Substring(valueIndex + 1).Trim();
				}
				else
				{
					headerName = line.Trim();
					headerValue = null;
				}
				// Check for particularly important headers
				if (headerName.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
				{
					// Split the parts of the value; this could be a file.
					if (null == headerValue)
					{
						// I don't think undefined content types are legal...
						throw new ProtocolViolationException(
							"Invalid value for HTTP header (" + line + ")");
					}
					String[] pieces = headerValue.Split(
						new char[] { ' ', ';' },
						StringSplitOptions.RemoveEmptyEntries);
					foreach (String piece in pieces)
					{
						String[] bits = piece.Split(
							new char[] { ' ', '=', '\"' },
							StringSplitOptions.RemoveEmptyEntries);
						// Check for common disposition pieces.
						if (bits[0].Equals("name", StringComparison.OrdinalIgnoreCase))
						{
							name = (bits.Length < 2) ? null : bits[1];
						}
						else if (bits[0].Equals("filename", StringComparison.OrdinalIgnoreCase))
						{
							filename = (bits.Length < 2) ? null : bits[1];
						}
					}
				}
				else if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
				{
					// For now, just look for multipart.
					if (null == headerValue)
					{
						// I don't think undefined content types are legal...
						throw new ProtocolViolationException(
							"Invalid value for HTTP header (" + line + ")");
					}
					if (headerValue.StartsWith("multipart"))
					{
						int offset = headerValue.IndexOf("boundary=");
						if (offset > 0)
							multipartboundry = headerValue.Substring(offset + 9).Trim();
						else
						{
							// There is supposed to be a boundary definition here...
							throw new ProtocolViolationException(
								"Invalid value for HTTP header (" + line + ")");
						}
					}
				}
				// Store this header in the dictionary
				headers[headerName] = headerValue;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="data">The byte array in which to search for boundaries</param>
		/// <param name="boundary">The boundary string, from the Content-Type header</param>
		/// <param name="startIndex">The 0-based index to begin the search at (default is 0)</param>
		/// <returns>Index of the start of the next boundary line, or -1 if no such line exists</returns>
		public static MimePart[] findParts (byte[] data, String boundary, int startIndex = 0)
		{
			List<MimePart> parts = new List<MimePart>();
			MimePart part = null;
			int lineStart = startIndex - 1;
			int start = -1;
			// First, we have to find the start boundary.
			// Scan the body for lines long enough to contain the boundary marker.
			while (lineStart < (data.Length - boundary.Length))
			{
				// Find the next line
				int lineEnd = Array.IndexOf<byte>(data, (byte)'\n', ++lineStart);
				if (-1 == lineEnd)
				{
					// We found the end (instead of a newline); treat the whole thing as a line.
					lineEnd = data.Length;
				}
				// Make sure the line is long enough to hold the boundary
				if ((lineEnd - lineStart) >= boundary.Length)
				{
					// Found a long-enough line; Stringify it and check.
					String line = Encoding.UTF8.GetString(
						data,
						lineStart,
						(lineEnd - lineStart - 1));
					if (line.Contains(boundary))
					{
						// Check if this is the first boundary or a subsequent one.
						if (-1 == start)
						{
							// OK, we found the first boundary! Remember it and find the next.
							start = lineEnd + 1;
						}
						else
						{
							// We found a full MIME part! Parse it.
							part = new MimePart();
							part.offset = start;
							part.length = lineStart - start;

							// Locate the headers (\r\n\r\n), searching from end of boundary line.
							for (start -= 2; start < (lineStart - 3); start++)
							{
								// Find the end of the headers
								if (Utility.CR == data[start] &&
									Utility.CR == data[start + 2] &&
									Utility.LF == data[start + 1] &&
									Utility.LF == data[start + 3])
								{
									// End of the headers found
									String head = Encoding.UTF8.GetString(
										data,
										part.offset,
										start - part.offset);
									String[] lines = head.Split(new String[] { "\r\n" },
										StringSplitOptions.RemoveEmptyEntries);
									// Parse the headers for this part.
									part.parseHeaders(lines);
									// End of headers reached, skip over the blank line
									start += 4;
									part.body = new byte[lineStart - start];
									Array.Copy(data, start, part.body, 0, lineStart - start);
									// Done with headers
									break;
								}
							}
							if (null == part.headers)
							{
								// We never found the end of the headers. It's all body?
								part.body = new byte[part.length];
								Array.Copy(data, part.offset, part.body, 0, part.length);
							}
							// All right, headers are parsed. Let's get the body now...
							else if (part.headers.ContainsKey("Content-Type"))
							{
								String ct = part.headers["Content-Type"];
								// Find the offset of the value part of the character set.
								int cso = ct.IndexOf("charset");
								if (-1 != cso)
								{
									// The charset field is present; get the body as a string.
									cso = ct.IndexOf('=', cso);
									String cs = ct.Substring(cso + 1).Trim();
									Encoding enc = Encoding.GetEncoding(cs);
									part.bodyText = enc.GetString(part.body, 0, part.body.Length);
								}
								else if (!String.IsNullOrEmpty(part.multipartboundry))
								{
									part.bodyParts = findParts(part.body, part.multipartboundry);
								}
							}
							else
							{
								// There are headers, but no Content-Type.
								// Probably an ordinary value; try to encode it as a string.
								try
								{
									part.bodyText = Encoding.UTF8.GetString(
										part.body,
										0,
										part.body.Length);
								}
								catch (Exception)
								{ }
							}
							// Part fully parsed
							parts.Add(part);
							// Keep going; there may be more parts.
							start = lineEnd + 1;
						}
					}
				}
				// Maybe the line wasn't long enough or it didn't have the boundary.
				// Or maybe it did, and it was the first boundary, so we have to find another.
				// Or maybe we even found a whole part, parsed it, and are now looking for more.
				lineStart = lineEnd;
			}
			// We didn't find an expected boundary so there's no (more) part(s).
			return parts.Count > 0 ? parts.ToArray() : null;
		}
	}

}