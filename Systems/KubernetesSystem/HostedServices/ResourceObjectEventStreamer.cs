﻿using Common.Interfaces;
using Common.Models;
using Common.Utils;
using k8s;
using k8s.Autorest;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Systems.KubernetesSystem.Models;

namespace Systems.KubernetesSystem.HostedServices
{
	internal class ResourceObjectEventStreamer : IHostedService
    {
		private Listener _listener;
		private readonly ILogger _logger;
		private readonly AppSettings _appSettings;
		private readonly KubernetesClient _kubernetesClient;
		private readonly IEnumerable<IResourceObjectEventHandler> _resourceObjectEventHandlers;

		public string Group { get; internal set; }
		public string Version { get; internal set; }
		public string NamePlural { get; internal set; }

		public ResourceObjectEventStreamer
		(
			AppSettings appSettings,
			KubernetesClient kubernetesClient,
			ILogger<ResourceObjectEventStreamer> logger,
            IEnumerable<IResourceObjectEventHandler> resourceObjectEventHandlers
        )
        {
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
			_kubernetesClient = kubernetesClient ?? throw new ArgumentNullException(nameof(kubernetesClient));
			_resourceObjectEventHandlers = resourceObjectEventHandlers ?? throw new ArgumentNullException(nameof(resourceObjectEventHandlers));
			_ = kubernetesClient.Client ?? throw new ArgumentException($"{nameof(kubernetesClient.Client)} is required", nameof(kubernetesClient));
		}

        public async Task StartAsync(CancellationToken cancellationToken)
        {
			if (string.IsNullOrWhiteSpace(Version))
			{
				throw new ApplicationException($"{nameof(Version)} is required");
			}

			if(string.IsNullOrWhiteSpace(NamePlural))
			{
				throw new ApplicationException($"{nameof(NamePlural)} is required");
			}

			Version = Version.Trim();
			NamePlural = NamePlural.Trim();
			Group = Group?.Trim() ?? string.Empty;

			var isReconnecting = false;

			var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _appSettings.IsProduction ? default : 1,
			};

			var logTag = $"{NameUtil.GetResourceObjectKindLongName(Group, Version, NamePlural)} Events";

			void connect()
			{
				Task.Run(async () =>
				{
					try
					{
						if (cancellationToken.IsCancellationRequested)
						{
							_logger.LogInformation("{LogTag} : Exiting", logTag);
							return;
						}

						_logger.LogInformation("{LogTag} : {Message}", logTag, isReconnecting ? "Reconnecting" : "Connecting");

						if (isReconnecting)
						{
							await Task.Delay(2000, cancellationToken);
						}

						isReconnecting = true; // from this moment onwards it's reconnections

						_listener?.Dispose();
						_listener = new(logTag, _logger);
						_listener.HttpOperationResponse = await _kubernetesClient.Client.CustomObjects.ListClusterCustomObjectWithHttpMessagesAsync(Group, Version, NamePlural, allowWatchBookmarks: false, watch: true, cancellationToken: cancellationToken);

						_logger.LogInformation("{LogTag} : Streaming", logTag);

						_listener.Watcher = _listener.HttpOperationResponse.Watch((WatchEventType eventType, object eventData) =>
						{
							if (eventType == WatchEventType.Error)
							{
								// TODO : How do i know it's GONE
								_logger.LogError("{LogTag} : Error (GONE suspected)", logTag);
								connect();
								return;
							}

							var eventDataJsonElement = (JsonElement)eventData;
							var resourceObjectEvent = eventDataJsonElement.Deserialize<JsonObject>();

							resourceObjectEvent.SetResourceObjectSpecificEventName(eventType.ToString());

							Parallel.ForEach(_resourceObjectEventHandlers, parallelOptions, resourceObjectEventHandler =>
							{
								Task.Run(async () =>
								{
									try
									{
										await resourceObjectEventHandler.HandleAsync(resourceObjectEvent, cancellationToken);
									}
									catch (Exception exception)
									{
										var handlerName = resourceObjectEventHandler.GetType().Name;
										var resourceObjectLongName = resourceObjectEvent.GetResourceObjectLongName();
										var specificEventName = resourceObjectEvent.GetResourceObjectSpecificEventName();

										_logger.LogError(exception, "Failed to process event | Handler : {HandlerName} | ResourceObject : {ResourceObjectLongName} | SpecificEvent : {SpecificEvent}", handlerName, resourceObjectLongName, specificEventName);
									}
								});
							});
						},
						onError: exception =>
						{
							_logger.LogError(exception, "{LogTag} : Error", logTag);
						},
						onClosed: () =>
						{
							_logger.LogWarning("{LogTag} : Closed", logTag);
							connect();
						});
					}
					catch (Exception exception)
					{
						_logger.LogError(exception, "{LogTag} : Error", logTag);
						connect();
					}
				}, cancellationToken);
			}

			connect();

			// force async

			await Task.CompletedTask;
		}

        public async Task StopAsync(CancellationToken cancellationToken)
        {
			try
			{
				_listener?.Dispose();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Failed to dispose handle");
			}

			await Task.CompletedTask;
        }

		private class Listener : IDisposable
		{
			private readonly string _logTag;
			private readonly ILogger _logger;

			public Watcher<object> Watcher { get; internal set; }
			public HttpOperationResponse<object> HttpOperationResponse { get; internal set; }

			public Listener
			(
				string logTag,
				ILogger logger
			)
			{
				_logTag = logTag ?? throw new ArgumentNullException(nameof(logTag));
				_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			}

			public void Dispose()
			{
				try
				{
					Watcher?.Dispose();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "{LogTag} : Failed to dispose watcher", _logTag);
				}

				try
				{
					HttpOperationResponse?.Dispose();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "{LogTag} : Failed to dispose http operation response", _logTag);
				}
			}
		}
	}
}
