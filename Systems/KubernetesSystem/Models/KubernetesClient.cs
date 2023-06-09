﻿using k8s;
using System;

namespace Systems.KubernetesSystem.Models
{
	public sealed class KubernetesClient : IDisposable
	{
		public Kubernetes Client { get; set; }
		public void Dispose() => Client?.Dispose();
	}
}
