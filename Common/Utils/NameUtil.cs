﻿using System;

namespace Common.Utils
{
	public static class NameUtil
	{
		public static string GetApiVersion(string group, string version)
		{
			if (string.IsNullOrWhiteSpace(group))
			{
				group = "-";
			}

			if (string.IsNullOrWhiteSpace(version))
			{
				throw new ArgumentNullException(nameof(version));
			}

			return $"{group.Trim()}/{version.Trim()}";
		}

		public static string GetLongKind(string group, string version, string kind)
		{
			if (string.IsNullOrWhiteSpace(group))
			{
				group = "-";
			}

			if (string.IsNullOrWhiteSpace(version))
			{
				throw new ArgumentNullException(nameof(version));
			}

			if (string.IsNullOrWhiteSpace(kind))
			{
				throw new ArgumentNullException(nameof(kind));
			}

			return $"{GetApiVersion(group, version)}/{kind.Trim()}";
		}

		public static string GetLongName(string group, string version, string kind, string @namespace, string name)
		{
			if (string.IsNullOrWhiteSpace(group))
			{
				group = "-";
			}

			if (string.IsNullOrWhiteSpace(version))
			{
				throw new ArgumentNullException(nameof(version));
			}

			if (string.IsNullOrWhiteSpace(kind))
			{
				throw new ArgumentNullException(nameof(kind));
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				throw new ArgumentNullException(nameof(name));
			}

			if (string.IsNullOrWhiteSpace(@namespace))
			{
				@namespace = "-";
			}

			return $"{GetLongKind(group, version, kind)}/{@namespace.Trim()}/{name.Trim()}";
		}
	}
}
