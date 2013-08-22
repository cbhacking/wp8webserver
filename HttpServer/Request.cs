﻿/*
 * HttpServer\Request.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.4
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
		HttpMethod method;
		String path;
		String querystring;
		String fragment;
		HttpVersion version;
		int contentlength;
		Dictionary<String, String> headers;
		String body;

		Dictionary<String, String> urlparams;
		int currentLine;

		/// <summary>
		/// Creates a new ServerRequest from the provided String.
		/// </summary>
		/// <remarks>
		/// If the String does not contain a complete request, the Complete property will be false.
		/// It is possible to add additional data to the request using the Continue method.
		/// There can be more than one request in a String; only the first will be constructed.
		/// Any data that is not part of a completely parsed request will be returned in <paramref name="request"/>.
		/// </remarks>
		/// <param name="request">String containing the request. Incomplete requests will be returned through this parameter.</param>
		public HttpRequest (ref String request)
		{
			request = parseRequest(request, false);
			urlparams = null;
		}

		private String parseRequest (String request, bool resume)
		{
			String[] lines = request.Split(new String[] { "\r\n", "\n" }, StringSplitOptions.None);
			#region FIRSTLINE
			if (!resume || 0 == currentLine)
			{
				// Parse the first line
				currentLine = 0;
				String[] firstline = lines[0].Split(' ');
				// Check for a well-formed line
				if (firstline.Length < 2)
				{
					if (lines.Length < 2)
					{
						// We just have a very incomplete request
						return request;
					}
					else
					{
						// The first line contains no spaces...
						throw new ProtocolViolationException(
							"Invalid value for HTTP request (" + lines[currentLine] + ")");
					}
				}
				// Identify the method
				method = HttpMethod.INVALID_METHOD;
				for (int i = 0; i < Utility.METHODS.Length; i++)
				{
					if (Utility.METHODS[i].Equals(firstline[0], StringComparison.InvariantCultureIgnoreCase))
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
						if (Utility.VERSIONS[i].Equals(firstline[2], StringComparison.InvariantCultureIgnoreCase))
						{
							version = (HttpVersion)i;
						}
					}
				}
			}   // if (!resume || 0 == currentLine)
			#endregion
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
						int bodyLen = Math.Min(contentlength, (request.Length - endIndex));
						body = request.Substring(endIndex, bodyLen);
						// Check whether we're done...
						if (bodyLen < contentlength)
						{
							// Note that we need more body
							currentLine = int.MinValue;
						}
						else
						{
							// Note that we're done with this request, return any remnant
							currentLine = -1;
							request = request.Substring(endIndex + contentlength);
						}
					}
					else
					{
						// There is no body to this request; we're done
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
				if (headerName.Equals("Content-Length", StringComparison.InvariantCultureIgnoreCase))
				{
					if (!int.TryParse(headerValue, out contentlength) && (currentLine + 1) < lines.Length)
					{
						// This was supposed to be a complete line. It is broken
						throw new ProtocolViolationException(
							"Invalid value for HTTP header (" + lines[currentLine] + ")");
					}
					// Move on to the next header
					continue;
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
		public bool Complete { get { return (-1 == currentLine); } }

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
							String name = HttpUtility.UrlDecode(item.Substring(0, item.IndexOf('=')));
							String value = HttpUtility.UrlDecode(item.Substring(item.IndexOf('=') + 1));
							urlparams[name] = value;
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
		/// Gets the body of the request as a String. May be null if the request had no body.
		/// The body is presented as-is and may contain encoded characters.
		/// </summary>
		public String Body { get { return body; } }

		/// <summary>
		/// Continues parsing the request from where the previous (incomplete) String ended.
		/// Returns any text which is not part of a fully parsed request.
		/// </summary>
		/// <remarks>
		/// This function is provided as a faster way to build a complete request. It is not robust.
		/// In particular, a partial request which ends in the middle of a header name will leave an invalid header.
		/// It is safer to create a new ServerRequest instead.
		/// </remarks>
		/// <param name="request">String containing the raw request. Must include the portion parsed thus far.</param>
		/// <returns>Any remaining text which is nor part of a completely parsed request.</returns>
		public String Continue (String request)
		{
			if (this.Complete)
				return request;
			return parseRequest(request, true);
		}
	}
}
