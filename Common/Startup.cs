﻿using Common.Interfaces;
using Common.Models;
using Common.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Common
{
	public static class Startup
	{
		public static IServiceCollection AddCommon(this IServiceCollection services, AppSettings appSettings)
		{
			if (services is null)
			{
				throw new ArgumentNullException(nameof(services));
			}

			if (appSettings is null)
			{
				throw new ArgumentNullException(nameof(appSettings));
			}

			services.AddSingleton<AppData>();
			services.AddSingleton(appSettings);
			services.AddSingleton(typeof(IServiceProvider<>), typeof(ServiceProvider<>));
			services.AddSingleton<ICustomResourceDefinitionComparer, CustomResourceDefinitionComparerImpl>();

			return services;
		}
	}
}
