/*
 * WebAccess\WebApplication.cs
 * Author: GoodDayToDie on XDA-Developers forum
 * License: Microsoft Public License (MS-PL)
 * Version: 0.5.3
 * Source: https://wp8webserver.codeplex.com
 *
 * Performs operations involving .REG files.
 * Uses the NativeAccess project to access the registry.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

using Registry;

namespace WebAccess
{
	static class RegTools
	{
		public static String BuildRegFile (RegistryHive hive, String path, String filename = null)
		{
			String name = filename;
			if (String.IsNullOrEmpty(filename))
			{
				if (!String.IsNullOrEmpty(path))
				{
					if (path.Contains('\\'))
					{
						name = path.Substring(path.LastIndexOf('\\') + 1);
					}
					else
					{
						name = path;
					}
				}
				else
				{
					name = hive.ToString();
				}
				name += ".REG";
			}
			try
			{
				name = Path.Combine(ApplicationData.Current.LocalFolder.Path, name);
				if (File.Exists(name))
					File.Delete(name);
				FileInfo f = new FileInfo(name);
				using (StreamWriter writer = f.CreateText())
				{
					// Write header
					writer.WriteLine("Windows Registry Editor Version 5.00");
					BuildRegRecurse(hive, path, writer);
				}
			}
			catch (Exception)
			{
				File.Delete(name);
				return null;
			}
			return name;
		}

		static void BuildRegRecurse (RegistryHive hive, String path, StreamWriter writer)
		{
			String[] subkeys;
			ValueInfo[] vals;
			// First, write this key
			writer.WriteLine("\r\n[" + new RegistryKey(hive, path).FullName + ']');
			if (NativeRegistry.GetValues(hive, path, out vals))
			{
				if (null != vals)
				{
					foreach (ValueInfo val in vals)
					{
						String name = String.IsNullOrEmpty(val.Name) ? "@" : '"' + val.Name + '"';
						switch (val.Type)
						{
						case RegistryType.String:
							{
								String str;
								if (NativeRegistry.ReadString(hive, path, val.Name, out str))
								{
									// Put a comment with what we think the string is
									writer.WriteLine(";" + name + "=\"" + str + '\"');
								}
								else
								{
									// Explain the error
									writer.WriteLine(";Getting the string failed with error " + NativeRegistry.GetError());
								}
								break;
							}
						case RegistryType.Integer:
							{
								uint i;
								if (NativeRegistry.ReadDWORD(hive, path, val.Name, out i))
								{
									// Put a comment with what we think the integer is
									writer.WriteLine(";" + name + "=DWORD:" + i.ToString("X8") + " ;(" + i.ToString() + ')');
								}
								else
								{
									// Explain the error
									writer.WriteLine(";Getting the DWORD failed with error " + NativeRegistry.GetError());
								}
								break;
							}
						case RegistryType.MultiString:
							{
								String[] ms;
								if (NativeRegistry.ReadMultiString(hive, path, val.Name, out ms))
								{
									// Put a comment with what we think the strings are
									writer.Write(";" + name + "=");
									foreach (String s in ms)
									{
										writer.Write("\\\r\n;  \"" + s + '"');
									}
									writer.WriteLine();
								}
								else
								{
									// Explain the error
									writer.WriteLine(";Getting the multi-string failed with error " + NativeRegistry.GetError());
								}
								break;
							}
						default:
							// Put a comment with what type it's supposed to be
							writer.WriteLine(";" + name + " has type " + val.Type.ToString("G"));
							break;
						}
						// Write the actual value in hex format
						byte[] buf;
						RegistryType t;
						if (!NativeRegistry.QueryValue(hive, path, val.Name, out t, out buf))
						{
							// Explain the error and move on
							writer.WriteLine(";Querying value failed with error " + NativeRegistry.GetError());
							continue;
						}
						writer.Write(name + "=hex(" + ((uint)t).ToString("x") + ((null != buf && buf.Length > 0) ? "):\\\r\n  " : "):\r\n"));
						if (null != buf && buf.Length > 0)
						{
							for (int i = 0; i < buf.Length; i++)
							{
								writer.Write(buf[i].ToString("X2"));
								if ((i + 1) < buf.Length)
								{
									writer.Write(',');
									if (0xF == (i & 0xF))
										writer.Write("\\\r\n  ");
								}
							}
							writer.WriteLine();
						}
					}
				}
				else
				{
					// No values; put a comment saying so
					writer.WriteLine("; This key contains no values");
				}
			}
			else
			{
				// Error while getting the values
				writer.WriteLine("; Failed to get the values of this key. Error " + NativeRegistry.GetError());
			}
			// OK, we wrote the values (whew). Time for subkeys.
			if (NativeRegistry.GetSubKeyNames(hive, path, out subkeys))
			{
				if (null != subkeys)
				{
					foreach (String sk in subkeys)
					{
						BuildRegRecurse(hive, (null != path ? path + '\\' + sk : sk), writer);
					}
				}
				// Else no subkeys, no need to say anything about it
			}
			else
			{
				// Error getting subkeys
				writer.WriteLine("; Failed to get subkeys of this key. Error " + NativeRegistry.GetError());
			}
		}
	}
}
