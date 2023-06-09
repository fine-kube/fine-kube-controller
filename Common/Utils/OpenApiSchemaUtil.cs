﻿using k8s.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Common.Utils
{
	public static class OpenApiSchemaUtil
	{
		public static V1JSONSchemaProps Convert(OpenApiSchema api)
		{
			if (api is null)
			{
				return default;
			}

			var k = new V1JSONSchemaProps();
			
			k.Type = api.Type;
			k.Title = api.Title;
			k.Format = api.Format;
			k.Example = api.Example;
			k.Pattern = api.Pattern;
			k.Not = Convert(api.Not);
			k.MaxItems = api.MaxItems;
			k.MinItems = api.MinItems;
			k.Nullable = api.Nullable;
			k.MaxLength = api.MaxLength;
			k.MinLength = api.MinLength;
			k.Items = Convert(api.Items);
			k.Description = api.Description;
			k.UniqueItems = api.UniqueItems;
			k.Maximum = Convert(api.Maximum);
			k.Minimum = Convert(api.Minimum);
			k.Required = api.Required?.ToList();
			k.MaxProperties = api.MaxProperties;
			k.MinProperties = api.MinProperties;
			k.MultipleOf = Convert(api.MultipleOf);
			k.Properties = Convert(api.Properties);
			k.DefaultProperty = Convert(api.Default);
			k.ExclusiveMaximum = api.ExclusiveMaximum;
			k.ExclusiveMinimum = api.ExclusiveMinimum;
			k.ExternalDocs = Convert(api.ExternalDocs);
			k.AdditionalItems = Convert(api.AdditionalProperties);
			k.AllOf = api.AllOf?.Select(x => Convert(x)).ToList();
			k.AnyOf = api.AnyOf?.Select(x => Convert(x)).ToList();
			k.OneOf = api.OneOf?.Select(x => Convert(x)).ToList();
			k.EnumProperty = api.Enum?.Select(x => Convert(x))?.ToList();

			return k;
		}

		private static V1ExternalDocumentation Convert(OpenApiExternalDocs a)
		{
			if (a is null)
			{
				return default;
			}

			var k = new V1ExternalDocumentation();

			k.Url = a.Url?.ToString();
			k.Description = a.Description;
			
			return k;
		}

		private static IDictionary<string, V1JSONSchemaProps> Convert(IDictionary<string, OpenApiSchema> az)
		{
			if (az is null)
			{
				return new Dictionary<string, V1JSONSchemaProps>();
			}

			var k = new Dictionary<string, V1JSONSchemaProps>();

			foreach (var a in az)
			{
				if (a.Key.Equals("apiVersion") || a.Key.Equals("kind") || a.Key.Equals("metadata"))
				{
					continue;
				}

				k[a.Key] = Convert(a.Value);
			}

			return k;
		}

		private static double? Convert(decimal? value)
		{
			if (value is null)
			{
				return default;
			}

			return System.Convert.ToDouble(value);
		}

		public static object Convert(IOpenApiAny value)
		{
			if (value is null)
			{
				return default;
			}

			switch (value.AnyType)
			{
				case AnyType.Array: break;
				case AnyType.Object: break;
				case AnyType.Primitive: return value.GetType().GetProperty("Value")?.GetValue(value);
			}

			return default;
		}
	}
}
