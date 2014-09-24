/*
 * HttpServer\Mime.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.4.0
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
		String multipartboundry;
		MimePart[] bodyParts;

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
				if (headerName.Equals("Content-Type"))
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

		/// <summary>
		/// 
		/// </summary>
		/// <param name="data">The byte array in which to search for boundaries</param>
		/// <param name="boundary">The boundary string, from the Content-Type header</param>
		/// <param name="startIndex">The 0-based index to begin the search at (default is 0)</param>
		/// <returns>Index of the start of the next boundary line, or -1 if no such line exists</returns>
		public static int findBoundary (byte[] data, String boundary, int startIndex = 0)
		{
			int ret = startIndex - 1;
			while (ret < (data.Length - boundary.Length))
			{
				// Find the next line
				int next = Array.IndexOf<byte>(data, (byte)'\n', ++ret);
				// If we found the end (instead of a newline) that's OK too
				if (-1 == next) next = data.Length;
				// Make sure the line is long enough to hold the boundary
				if ((next - ret) >= boundary.Length)
				{
					// Found a long enough line that it could be a boundary
					for (int j = ret; j < (ret + 2); j++)
					{
						int k;
						for (k = 0; (k < boundary.Length) && ((k + j) < next); k++)
						{
							if (data[k + j] != (byte)boundary[k])
							{
								break;
							}
						}
						if ((boundary.Length) == k)
						{
							// We found the end of the boundary and exited the loop cleanly
							return ret;
						}
						// If we get here, we didn't find the boundary
						// Check for a prepended - character
						if ((byte)'-' != data[j])
						{
							// The whole line didn't match, and it doesn't start with a -
							break;
						}
					}
				}
				// Either the line wasn't long enough or it didn't have the boundary
				ret = next;
			}
			// We didn't find the boundary line
			return -1;
		}
	}

}