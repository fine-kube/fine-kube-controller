﻿using Common.Models;
using k8s;
using Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Systems.KubernetesSystem;

namespace Application.EventHandlers
{
	internal class ResourceObjectEventHandler : IResourceObjectEventHandler
	{
		private readonly IResourceObjectEventService _resourceObjectEventService;
		private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
		
		public ResourceObjectEventHandler
		(
			IResourceObjectEventService resourceObjectEventService
		)
		{
			_resourceObjectEventService = resourceObjectEventService ?? throw new ArgumentNullException(nameof(resourceObjectEventService));
		}

		public async Task HandleAsync(ResourceObject resourceObject, CancellationToken cancellationToken)
		{
			// filter

			if (resourceObject is null)
			{
				throw new ArgumentNullException(nameof(resourceObject));
			}

			// semaphore to limit concurrency so versions are not messed up
			// named for each specific resource object so that concurrency is allowed for different resource objects

			var namedSemaphore = _semaphores.GetOrAdd(resourceObject.LongName, x => new(1));

			// process

			try
			{
				if (resourceObject.EventType == WatchEventType.Deleted)
				{
					await _resourceObjectEventService.DeleteAsync(resourceObject, cancellationToken);
				}
				else
				{
					await _resourceObjectEventService.AddOrUpdateAsync(resourceObject, cancellationToken);
				}
			}
			finally
			{
				// release semaphore

				namedSemaphore.Release();

				// clean up

				if (resourceObject.EventType == WatchEventType.Deleted)
				{
					_semaphores.Remove(resourceObject.LongName, out var _);
				}
			}
		}
	}
}