﻿using Common.Exceptions;
using Common.Models;
using Common.Utils;
using Humanizer;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Systems.KubernetesSystem;

namespace Systems.ApiSystem.Impl
{
	internal class ApiSystemImpl : IApiSystem
	{
		private static readonly ConcurrentDictionary<string, string> RESOURCE_OBJECT_NAMES = new();
		
		private readonly ILogger _logger;
		private readonly string _baseUrl;
		private readonly AppData _appData;
		private readonly RestClient _restClient;
		private readonly AppSettings _appSettings;
		private readonly IKubernetesSystem _kubernetesSystem;
		private readonly IHostApplicationLifetime _hostApplicationLifetime;

		public ApiSystemImpl
		(
			AppData appData,
			AppSettings appSettings,
			ILogger<ApiSystemImpl> logger,
			IKubernetesSystem kubernetesSystem,
			IHostApplicationLifetime hostApplicationLifetime
		)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_appData = appData ?? throw new ArgumentNullException(nameof(appData));
			_appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
			_kubernetesSystem = kubernetesSystem ?? throw new ArgumentNullException(nameof(kubernetesSystem));
			_hostApplicationLifetime = hostApplicationLifetime ?? throw new ArgumentNullException(nameof(hostApplicationLifetime));

			var scheme = _appSettings.API_HTTPS.Value ? "https" : "http";
			_baseUrl = $"{scheme}://{_appSettings.API_HOST}:{_appSettings.API_PORT}";

			if (!_appSettings.IsProduction)
			{
				_baseUrl += "/example"; 
			}

			_restClient = new RestClient(_baseUrl);
		}

		public string GetBaseUrl()
		{
			return _baseUrl;
		}

		public async Task<bool> IsRunningAsync(CancellationToken cancellationToken)
		{
			var request = new RestRequest("health", Method.GET);
			var response = await _restClient.ExecuteAsync(request, cancellationToken);
			
			if (!response.IsSuccessful)
			{
				_logger.LogWarning("{Path} {Status} {Message}", "health", response.StatusCode, response.Content);
			}

			return response.IsSuccessful;
		}

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			// LOAD SPEC

			var openApiDocument = default(OpenApiDocument);

			try
			{
				var specRequest = new RestRequest(_appSettings.API_SPEC_PATH, Method.GET);
				var specResponse = await _restClient.ExecuteAsync(specRequest, cancellationToken);

				if (!specResponse.IsSuccessful)
				{
					throw new AppException(specResponse.Content);
				}

				var openApiDiagnostic = default(OpenApiDiagnostic);
				var openApiStringReader = new OpenApiStringReader();

				switch (_appSettings.API_SPEC_FORMAT)
				{
					case SpecFormat.Json:
						openApiDocument = openApiStringReader.Read(specResponse.Content, out openApiDiagnostic);
						break;

					case SpecFormat.Yaml:
						openApiDocument = openApiStringReader.Read(specResponse.Content, out openApiDiagnostic);
						break;
				}

				if (openApiDocument is null)
				{
					throw new AppException("Failed to read spec : openApiDocument is null");
				}

				if (openApiDiagnostic is null)
				{
					throw new AppException("Failed to read spec : openApiDiagnostic is null");
				}

				if (openApiDiagnostic.SpecificationVersion != Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0)
				{
					throw new AppException("Specification is not version 3");
				}

				foreach (var openApiWarning in openApiDiagnostic.Warnings)
				{
					_logger.LogWarning("{Message} : {Pointer}", openApiWarning.Message, openApiWarning.Pointer);
				}

				foreach (var openApiError in openApiDiagnostic.Errors)
				{
					_logger.LogError("{Message} : {Pointer}", openApiError.Message, openApiError.Pointer);
				}

				if (openApiDiagnostic.Errors.Any())
				{
					throw new AppException("Specification has errors");
				}
			}
			catch (Exception exception)
			{
				throw new AppException("Failed to get spec from web api", exception);
			}

			// definitions

			var definitions = new List<CustomResourceDefinitionResourceObject>();

			var knownKindApiEndpoints = new List<ApiEndpoint>();

			var apiSchemas = openApiDocument.Components.Schemas.ToList();

			var apiEndpoints = openApiDocument.Paths
				.Where(x => !x.Key.Trim().Trim('/').ToLower().Equals("health", StringComparison.OrdinalIgnoreCase)) // skip health check endpoint
				.SelectMany(x => x.Value.Operations.Select(o =>
				{
					try
					{
						return new ApiEndpoint(x.Key, x.Value, o.Key, o.Value, _appSettings.API_GROUP);
					}
					catch (Exception exception)
					{
						_logger.LogWarning("Path '{Path}' skipped because '{Message}'", x.Key, exception.Message);
						return null;
					}
				}))
				.Where(x => x is not null)
				.Where(x => x.OperationType == OperationType.Put || x.OperationType == OperationType.Delete)
				.ToList();

			foreach (var apiSchemaKeyAndValue in apiSchemas)
			{
				var putEndpoint = apiEndpoints.SingleOrDefault(x => x.OperationType == OperationType.Put && x.KindLowerCase.Equals(apiSchemaKeyAndValue.Key, StringComparison.OrdinalIgnoreCase));
				var deleteEndpoint = apiEndpoints.SingleOrDefault(x => x.OperationType == OperationType.Delete && x.KindLowerCase.Equals(apiSchemaKeyAndValue.Key, StringComparison.OrdinalIgnoreCase));

				if (putEndpoint is null || deleteEndpoint is null)
				{
					continue; // skip supporting schema/type
				}

				if (!string.IsNullOrWhiteSpace(putEndpoint.NamespaceLowerCase) && !putEndpoint.NamespaceLowerCase.Equals(deleteEndpoint.NamespaceLowerCase, StringComparison.OrdinalIgnoreCase))
				{
					_logger.LogWarning("PUT and DELETE endpoints for schema '{Schema}' must have the same value for the namespace (segment index 3)", apiSchemaKeyAndValue.Key);
					continue;
				}

				if (putEndpoint.GroupLowerCase?.Equals(_appSettings.API_GROUP, StringComparison.OrdinalIgnoreCase) != true)
				{
					putEndpoint.KnownKindLowerCase = await _kubernetesSystem.GetKnownKindForApiEndpointAsync(putEndpoint, cancellationToken);
					
					if (!string.IsNullOrWhiteSpace(putEndpoint.KnownKindLowerCase))
					{
						knownKindApiEndpoints.Add(putEndpoint);
					}
					else
					{
						_logger.LogWarning("Unknown resource kind '{Kind}'", $"{putEndpoint.GroupLowerCase ?? "-"}/{putEndpoint.VersionLowerCase}/{putEndpoint.KindLowerCase}");
					}

					continue;
				}

				var kind = apiSchemaKeyAndValue.Key;
				var definitionVersion = putEndpoint.VersionLowerCase;
				var definition = new CustomResourceDefinitionResourceObject();

				definition.Kind = Constants.CustomResourceDefinitionPascalCase;
				definition.ApiVersion = Constants.ApiExtensionsK8sIoV1LowerCase;

				definition.EnsureMetadata();
				definition.Metadata.EnsureLabels();
				definition.Metadata.Name = $"{kind.ToLower()}.{_appSettings.API_GROUP}";
				
				definition.Spec ??= new();

				definition.Spec.Group = _appSettings.API_GROUP;
				definition.Spec.Scope = string.IsNullOrWhiteSpace(putEndpoint.NamespaceLowerCase) ? Constants.ClusterPascalCase : Constants.NamespacedPascalCase;

				definition.Spec.Names ??= new();
				definition.Spec.Names.Kind = kind;
				definition.Spec.Names.Plural = kind.ToLower();
				definition.Spec.Names.Singular = kind.ToLower();

				definition.Spec.Versions ??= new List<V1CustomResourceDefinitionVersion>();
				definition.Spec.Versions.Add(new() { Name = definitionVersion, Schema = new() { OpenAPIV3Schema = new() }, Served = true, Storage = true, });
				definition.Spec.Versions[0].Schema.OpenAPIV3Schema = OpenApiSchemaUtil.Convert(apiSchemaKeyAndValue.Value);

				definitions.Add(definition);
			}

			_appData.CustomResourceDefinitions = definitions;
			_appData.KnownKindApiEndpoints = knownKindApiEndpoints;
		}

		private string GetApiPath(ResourceObject resourceObject)
		{
			var kind = resourceObject.Kind;
			var name = resourceObject.Name();
			var group = resourceObject.ApiGroup();
			var @namespace = resourceObject.Namespace();
			var version = resourceObject.ApiGroupVersion();

			if (string.IsNullOrWhiteSpace(group))
			{
				group = Constants.NotApplicableSymbol;
			}

			if (string.IsNullOrWhiteSpace(@namespace))
			{
				@namespace = Constants.NotApplicableSymbol;
			}

			if (group.Equals(_appSettings.API_GROUP, StringComparison.OrdinalIgnoreCase))
			{
				group = Constants.CurrentApiGroupSymbol;
			}
			else
			{
				var endpoint = _appData.KnownKindApiEndpoints
					.Where(x => !string.IsNullOrWhiteSpace(x.KnownKindLowerCase))
					.SingleOrDefault(x => x.KnownKindLowerCase.Equals(kind, StringComparison.OrdinalIgnoreCase) || x.KnownKindLowerCase.Singularize().Equals(kind, StringComparison.OrdinalIgnoreCase) || x.KnownKindLowerCase.Pluralize().Equals(kind, StringComparison.OrdinalIgnoreCase));

				if (endpoint is not null)
				{
					kind = endpoint.KindLowerCase;
				}
				
			}

			return $"{group}/{version}/{kind}/{@namespace}/{name}".ToLower();
		}

		public async Task AddOrUpdateAsync(ResourceObject resourceObject, CancellationToken cancellationToken)
		{
			var logTag = $"Event={resourceObject.EventType}  Name={resourceObject.LongName}  ResourceVersion={resourceObject.ResourceVersion()}";

			try
			{
				var path = RESOURCE_OBJECT_NAMES.GetOrAdd(resourceObject.LongName, x => GetApiPath(resourceObject));
				logTag = $"Event={resourceObject.EventType}  Path={path}  ResourceVersion={resourceObject.ResourceVersion()}";
				var request = new RestRequest(path, Method.PUT, DataFormat.Json).AddBody(resourceObject.Data.ToString());
				var response = await _restClient.ExecuteAsync(request, cancellationToken);

				if (response.ErrorException is not null)
				{
					_logger.LogError(response.ErrorException, "Network error. Restarting app");
					_hostApplicationLifetime.StopApplication();
				}

				if (!response.IsSuccessful)
				{
					throw new AppException(response.Content);
				}

				_logger.LogInformation("{LogTag} : SUCCESS", logTag);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "{LogTag} : ERROR", logTag);
			}
		}

		public async Task DeleteAsync(ResourceObject resourceObject, CancellationToken cancellationToken)
		{
			var logTag = $"Event={resourceObject.EventType}  Name={resourceObject.LongName}  ResourceVersion={resourceObject.ResourceVersion()}";

			try
			{
				var path = RESOURCE_OBJECT_NAMES.GetOrAdd(resourceObject.LongName, x => GetApiPath(resourceObject));
				logTag = $"Event={resourceObject.EventType}  Path={path}  ResourceVersion={resourceObject.ResourceVersion()}";
				var request = new RestRequest(path, Method.DELETE, DataFormat.Json).AddBody(resourceObject.Data.ToString());
				var response = await _restClient.ExecuteAsync(request, cancellationToken);

				if (response.ErrorException is not null)
				{
					_logger.LogError(response.ErrorException, "Network error. Restarting app");
					_hostApplicationLifetime.StopApplication();
				}

				if (!response.IsSuccessful)
				{
					throw new AppException(response.Content);
				}

				RESOURCE_OBJECT_NAMES.Remove(resourceObject.LongName, out var _); // cleanup
				_logger.LogInformation("{LogTag} : SUCCESS", logTag);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "{LogTag} : ERROR", logTag);
			}
		}
	}
}
