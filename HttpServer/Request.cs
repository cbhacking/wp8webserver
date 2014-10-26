/*
	* HttpServer\Request.cs
	* Author: GoodDayToDie on XDA-Developers forum
	* License: Microsoft Public License (MS-PL)
	* Version: 0.4.1
	* Source: https://wp8webserver.codeplex.com
	*
	* Parses an HTTP request from the listener. Does not perform any I/O.
	*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace HttpServer
{
	/// <summary>
	/// Parses an HTTP request, and provides access to its details.
	/// Can recognize and usually handle incomplete requests.
	/// </summary>
	public class HttpRequest
	{
		// Don't try to handle a request bigger than 100 MB
		const long MAX_REQUEST_LENGTH = 100L << 20;


		// First line fields
		HttpMethod method;
		String path;
		String querystring;
		String fragment;
		HttpVersion version;
		Dictionary<String, String> urlparams;

		// Header fields
		long contentlength;
		Dictionary<String, String> headers;
		String multipartboundry;

		// Body fields
		byte[] body;
		String bodytext;

		// Parser markers
		long current;
		int currentLine;
		int bodyIndex;

		/// <summary>
		/// Allows creating an empty HttpRequest object.
		/// Caller must then use Continue to populate the request data.
		/// </summary>
		public HttpRequest ()
		{
			method = HttpMethod.INVALID_METHOD;
			path = null;
			querystring = null;
			fragment = null;
			version = HttpVersion.INVALID_VERSION;
			urlparams = null;
			contentlength = -1L;
			headers = null;
			multipartboundry = null;
			body = null;
			bodytext = null;
			current = 0L;
			currentLine = 0;
			bodyIndex = -1;
		}

		/// <summary>
		/// Creates a new HttpRequest from the provided String.
		/// </summary>
		/// <remarks>
		/// If the String does not contain a complete request, the Complete property will be false.
		/// It is possible to add additional data to the request using the Continue method.
		/// There can be more than one request in a String; only the first will be constructed.
		/// Any data that is not part of a completely parsed request will be returned in <paramref name="request"/>.
		/// </remarks>
		/// <param name="request">String containing the request. Incomplete requests will be returned through this parameter.</param>
		public HttpRequest (ref String request) : this()
		{
			request = parseRequest(request, false);
			urlparams = null;
		}

		public HttpRequest (ref byte[] request) : this()
		{
			request = parseRequest(request, false);
			return;
		}

		private void parseFirstLine (String line)
		{
			// Parse the first line
			currentLine = 0;
			String[] firstline = line.Split(' ');
			// Check for a well-formed line
			if (firstline.Length < 2)
			{
				// The first line contains no spaces...
				throw new ProtocolViolationException(
					"Invalid first line for HTTP request (" + line + ")");
			}
			// Identify the method
			method = HttpMethod.INVALID_METHOD;
			for (int i = 0; i < Utility.METHODS.Length; i++)
			{
				if (Utility.METHODS[i].Equals(firstline[0], StringComparison.OrdinalIgnoreCase))
				{
					method = (HttpMethod)i;
					break;
				}
			}
			// Crack the path
			int fragIndex = firstline[1].IndexOf('#');
			int queryIndex = firstline[1].IndexOf('?');
			if (fragIndex > 0 && queryIndex > fragIndex)
			{
				// The first question mark is in the fragment; we don't care
				queryIndex = -1;
			}
			if (queryIndex > 0)
			{
				// There is a query string; extract up to the start of it
				path = firstline[1].Substring(0, queryIndex);
				if (fragIndex > 0)
				{
					// There is also a fragment, extract up to the start of it, and then get it
					querystring = firstline[1].Substring(queryIndex + 1, (fragIndex - (queryIndex + 1)));
					fragment = firstline[1].Substring(fragIndex + 1);
				}
				else
				{
					// There is no fragment, all the rest is querystring
					querystring = firstline[1].Substring(queryIndex + 1);
					fragment = null;
				}
			}
			else
			{
				// No querystring
				querystring = null;
				if (fragIndex > 0)
				{
					// There's no querystring, but there is a fragment
					path = firstline[1].Substring(0, fragIndex);
					fragment = firstline[1].Substring(fragIndex + 1);
				}
				else
				{
					// No querystring or fragment
					path = firstline[1];
					fragment = null;
				}
			}
			if (firstline.Length == 2 || String.IsNullOrEmpty(firstline[2]))
			{
				// No HTTP/VERSION field
				version = HttpVersion.ZERO_POINT_NINE;
			}
			else
			{
				// Check for a known version
				version = HttpVersion.INVALID_VERSION;
				for (int i = 0; i < Utility.VERSIONS.Length; i++)
				{
					if (Utility.VERSIONS[i].Equals(firstline[2], StringComparison.OrdinalIgnoreCase))
					{
						version = (HttpVersion)i;
						break;
					}
				}
			}
			// First line parsed successfully
			currentLine = 1;
		}

		private void parseHeaders (String[] lines, int skip = 0)
		{
			if (null == headers)
			{
				headers = new Dictionary<String, String>(lines.Length - skip);
			}
			foreach (String line in lines)
			{
				// Skip any lines we aren't supposed to process
				if (skip > 0)
				{
					skip--;
					continue;
				}
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
				if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
				{
					if ((null == headerValue) || !long.TryParse(headerValue, out contentlength))
					{
						// This was supposed to be a complete line. It is broken
						throw new ProtocolViolationException(
							"Invalid value for HTTP header (" + line + ")");
					}
					if (contentlength > MAX_REQUEST_LENGTH)
					{
						// We probably can't handle a request this big right now!
						throw new OutOfMemoryException(
							"The server unable to handle the request, as its Content-Length exceeds " +
							MAX_REQUEST_LENGTH);
					}
				}
				else if (headerName.Equals("Content-Type"))
				{
					// For now, just look for multipart
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
							multipartboundry = headerValue.Substring(offset + 9);
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

		private byte[] parseRequest (byte[] request, bool resume)
		{
			if (!resume)
			{
				// Start the search from the beginning
				current = 0L;
				body = null;
				bodytext = null;
				bodyIndex = -1;
			}
			if (bodyIndex <= 0)
			{
				// Haven't found the end of the headers yet.
				for (; current < (request.Length - 3); current++)
				{
					// Find the end of the headers
					if (Utility.CR == request[current] && Utility.CR == request[current + 2] &&
						Utility.LF == request[current + 1] && Utility.LF == request[current + 3])
					{
						// End of the headers found
						String head = Encoding.UTF8.GetString(request, 0, (int)current);
						String[] lines = head.Split(new String[] { "\r\n" }, StringSplitOptions.None);
						// Parse the first line
						parseFirstLine(lines[0]);
						// Parse the headers. This version doesn't mess up current
						parseHeaders(lines, 1);
						// End of headers reached, skip over the blank line
						current += 4;
						bodyIndex = (int)current;
						break;
					}
				}
			}
			// Check whether the request is done
			if ((contentlength > 0) && ((request.Length - bodyIndex) >= contentlength))
			{
				// We have the entire body already
				body = new byte[contentlength];
				Array.Copy(request, bodyIndex, body, 0, (int)contentlength);
				if (headers.ContainsKey("Content-Type"))
				{
					String ct = headers["Content-Type"];
					int csi = ct.IndexOf("charset");
					if (-1 != csi)
					{
						// There is a character set for the body
						String cs = ct.Substring(ct.IndexOf('=', csi) + 1).Trim();
						if (cs.Contains(';'))
						{
							cs = cs.Substring(0, cs.IndexOf(';')).Trim();
						}
						Encoding enc = Encoding.GetEncoding(cs);
						bodytext = enc.GetString(body, 0, (int)contentlength);
					}
					else if (ct.Contains("text"))
					{
						// Text without specified charset. Oookay then
						bodytext = new System.IO.StreamReader(new System.IO.MemoryStream(body)).ReadToEnd();
					}
					else if (multipartboundry != null)
					{
						// Figure out how many parts there are
						List<int> partIndices = new List<int>();
						int idx = MimePart.findBoundary(body, multipartboundry);
						while (idx > 0)
						{
							partIndices.Add(idx);
						}
					}
				}
				// Since we have the whole body...
				current = -1;
				long totallen = (contentlength > 0) ? bodyIndex + contentlength : bodyIndex;
				if (request.Length > totallen)
				{
					// There may be another request past this one
					byte[] remainder = new byte[request.Length - totallen];
					Array.Copy(request, (int)totallen, remainder, 0, remainder.Length);
					return remainder;
				}
				else
				{
					// No remainder in this particular packet
					return null;
				}
			}
			else
			{
				// We don't have the full body yet
				return request;
			}
		}

		private String parseRequest (String request, bool resume)
		{
			String[] lines = request.Split(new String[] { "\r\n", "\n" }, StringSplitOptions.None);
			#region FIRSTLINE
			if (!resume || 0 == currentLine)
			{
				parseFirstLine(lines[0]);
			}   // if (!resume || 0 == currentLine)
			#endregion // firstline
			// Parse the headers
			if (lines.Length > 2)
			{
				headers = new Dictionary<String, String>(lines.Length - 2); // Max header count
			}
			else
			{
				headers = new Dictionary<string, string>();
			}
			if (!resume)
				contentlength = -1;
			for (currentLine = (resume) ? currentLine : 1;
				currentLine < lines.Length;
				currentLine++)
			{
				#region ENDREQUEST
				if (String.IsNullOrEmpty(lines[currentLine]))
				{
					// Two carriage returns in a row signals the end of the headers
					int endIndex = request.IndexOf("\r\n\r\n") + 4;
					if (3 == endIndex)
						endIndex = request.IndexOf("\n\n") + 2;
					if (contentlength > 0)
					{
						// There is some body to this request
						int bodyLen = Math.Min((int)contentlength, (request.Length - endIndex));
						body = Encoding.UTF8.GetBytes(request.Substring(endIndex, bodyLen));
						bodytext = request.Substring(endIndex);
						// Check whether we're done...
						if (bodyLen < contentlength)
						{
							// We need more body
						}
						else
						{
							#region MULTIPART
							// End of the headers. If multipart, parse the parts
							if (multipartboundry != null)
							{
								int firstpart = bodytext.IndexOf("--" + multipartboundry);
								// Find the end of the line
								firstpart = bodytext.IndexOf('\n', firstpart) + 1;
								if (-1 == firstpart)
								{
									throw new ProtocolViolationException("Incomplete multipart boundary line: " + lines[currentLine + 1]);
								}
								String line = bodytext.Substring(firstpart, (bodytext.IndexOf('\n', firstpart) - firstpart)).Trim();
								while (!String.IsNullOrWhiteSpace(line))
								{
									int partIndex = line.IndexOf(':');
									String partHeaderName, partHeaderValue;
									if (partIndex > 0)
									{
										partHeaderName = line.Substring(0, partIndex).Trim();
										partHeaderValue = line.Substring(partIndex + 1).Trim();
									}
									else
									{
										partHeaderName = line;
										partHeaderValue = null;
									}
								}
							}
							#endregion
							// Note that we're done with this request, return any remnant
							current = -1L;
							currentLine = -1;
							request = request.Substring(endIndex + (int)contentlength);
						}
					}
					else
					{
						// There is no body to this request; we're done
						current = -1L;
						currentLine = -1;
						request = request.Substring(endIndex);
					}
					// Either way, we're done parsing
					return request;
				}
				#endregion
				#region HEADERS
				// OK, this should be a header
				int valueIndex = lines[currentLine].IndexOf(':');
				String headerName, headerValue;
				if (valueIndex > 0)
				{
					headerName = lines[currentLine].Substring(0, valueIndex).Trim();
					headerValue = lines[currentLine].Substring(valueIndex + 1).Trim();
				}
				else
				{
					headerName = lines[currentLine];
					headerValue = null;
				}
				if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
				{
					if (!long.TryParse(headerValue, out contentlength) && (currentLine + 1) < lines.Length)
					{
						// This was supposed to be a complete line. It is broken
						throw new ProtocolViolationException(
							"Invalid value for HTTP header (" + lines[currentLine] + ")");
					}
					// Move on to the next header
					continue;
				}
				else if (headerName.Equals("Content-Type"))
				{
					// For now, just look for multipart
					if (headerValue.StartsWith("multipart"))
					{
						int offset = headerValue.IndexOf("boundary=");
						if (offset > 0)
							multipartboundry = headerValue.Substring(offset + 9);
						else
						{
							// There is supposed to be a boundary definition here...
							throw new ProtocolViolationException(
								"Invalid value for HTTP header (" + lines[currentLine] + ")");
						}
					}
				}
				// Some other header that we don't yet recognize
				headers[headerName] = headerValue;
				#endregion
			}
			// If we get here, we didn't find the end of the request...
			return request;
		}

		/// <summary>
		/// Gets whether the request data was sufficient for the full request
		/// </summary>
		public bool Complete { get { return ((-1L == current) || (-1 == currentLine)); } }

		/// <summary>
		/// Gets the HTTP request method (such as GET or HEAD).
		/// May be INVALID_METHOD if no recognizable method was found.
		/// </summary>
		public HttpMethod Method { get { return method; } }

		/// <summary>
		/// Gets the path component of the URL, without scheme, hostname, port, query string, or fragment.
		/// The path is presented as-is and may contain URL-encoded characters
		/// </summary>
		public String Path { get { return path; } }

		/// <summary>
		/// Gets the query string component of the URL without the leading '?'.
		/// The querystring is presented as-is and may contain URL-encoded characters.
		/// To get name/value pairs, use UrlParameters.
		/// </summary>
		/// <seealso cref="UrlParameters"/>
		public String QueryString { get { return querystring; } }

		public Dictionary<String, String> Headers
		{
			get
			{
				return headers;
			}
		}

		/// <summary>
		/// Gets the query string parameters as a dictionary of name/value pairs.
		/// Names and values are URL-decoded.
		/// </summary>
		public Dictionary<String, String> UrlParameters
		{
			get
			{
				if (null == urlparams)
				{
					if (null == querystring)
					{
						urlparams = new Dictionary<String, String>();
					}
					else
					{
						String[] items = querystring.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
						urlparams = new Dictionary<String, String>(items.Length);
						foreach (String item in items)
						{
							if (item.Contains('='))
							{
								String name = HttpUtility.UrlDecode(item.Substring(0, item.IndexOf('=')));
								String value = HttpUtility.UrlDecode(item.Substring(item.IndexOf('=') + 1));
								urlparams[name] = value;
							}
							else
							{
								urlparams[item] = null;
							}
						}
					}
				}
				return urlparams;
			}
		}

		/// <summary>
		/// Gets the fragment portion of the URL without the leading '#'
		/// The fragment is presented as-is and may contain URL-encoded characters
		/// </summary>
		public String Fragment { get { return fragment; } }

		/// <summary>
		/// Gets the HTTP version used for this request.
		/// If no version was stated, 0.9 is assumed.
		/// </summary>
		public HttpVersion Version { get { return version; } }

		/// <summary>
		/// Gets the body of the request as a byte array. May be null if the request had no body.
		/// The body is presented as-is and may contain encoded characters.
		/// </summary>
		public byte[] Body { get { return body; } }

		/// <summary>
		/// Continues parsing the request from where the previous (incomplete) String ended.
		/// Returns any text which is not part of a fully parsed request.
		/// </summary>
		/// <remarks>
		/// This function is provided as a faster way to build a complete request. It is not robust.
		/// In particular, a partial request which ends in the middle of a header name will leave an invalid header.
		/// It is safer to create a new HttpRequest instead.
		/// </remarks>
		/// <param name="request">String containing the raw request. Must include the portion parsed thus far.</param>
		/// <returns>Any remaining text which is nor part of a completely parsed request.</returns>
		public String Continue (String request)
		{
			if (this.Complete)
				return request;
			return parseRequest(request, true);
		}

		public byte[] Continue (byte[] request)
		{
			if (this.Complete)
				return request;
			return parseRequest(request, true);
		}
	}
}
