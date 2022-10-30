using System;
using Microsoft.Extensions.DependencyInjection;
using Shuttle.Core.Contract;

namespace Shuttle.Esb.OpenTelemetry
{
    public class OpenTelemetryBuilder
    {
        private ServiceBusOpenTelemetryOptions _openTelemetryOptions = new ServiceBusOpenTelemetryOptions();

        public OpenTelemetryBuilder(IServiceCollection services)
        {
            Guard.AgainstNull(services, nameof(services));

            Services = services;
        }

        public ServiceBusOpenTelemetryOptions Options
        {
            get => _openTelemetryOptions;
            set => _openTelemetryOptions = value ?? throw new ArgumentNullException(nameof(value));
        }

        public IServiceCollection Services { get; }
    }
}