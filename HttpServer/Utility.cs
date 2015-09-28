/*
 * HttpServer\Utility.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.5.0
 * Source: https://wp8webserver.codeplex.com
 *
 * HTTP-related enumerations and string collections.
 */

using System;

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
		TRACE,
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

	public enum ConnectionPersistence
	{
		UNSPECIFIED = -1,
		KEEP_ALIVE = 0,
		CLOSE = 1
	}

	public static class Utility
	{
		public const byte CR = (byte)'\r';
		public const byte LF = (byte)'\n';

		public static readonly String[] METHODS = { "GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT" };
		public static readonly String[] VERSIONS = { "", "HTTP/1.0", "HTTP/1.1" };
		public static readonly String[] PERSISTENCE = { "keep-alive", "close" };
		public static readonly String[] CONTENT_TYPES = {
			"text/html; charset=utf-8", "text/plain; charset=utf-8", "application/x-www-form-urlencoded", "multipart/form-data"};
	}

}
