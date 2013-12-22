/*
 * HttpServer\Utility.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.3.5
 * Source: https://wp8webserver.codeplex.com
 *
 * HTTP-related enumerations and string collections.
 */

using System;
using System.Text;

namespace HttpServer
{
	public enum HttpMethod
	{
		GET,
		POST,
		PUT,
		DELETE,
		HEAD,
		OPTIONS,
		CONNECT,
		INVALID_METHOD
	}

	public enum ResponseType
	{
		TEXT_HTML,
		TEXT_PLAIN,
		FORM_URLENCODED
	}

	public enum HttpVersion
	{
		ZERO_POINT_NINE,
		ONE_POINT_ZERO,
		ONE_POINT_ONE,
		INVALID_VERSION
	}

	public static class Utility
	{
		public static readonly String[] METHODS = { "GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "CONNECT" };
		public static readonly String[] VERSIONS = { "", "HTTP/1.0", "HTTP/1.1" };
		public static readonly String[] CONTENT_TYPES = {
			"text/html; charset=utf-8", "text/plain; charset=utf-8", "application/x-www-form-urlencoded"};
	}
}
