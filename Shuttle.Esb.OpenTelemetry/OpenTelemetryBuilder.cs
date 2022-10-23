using System;
using Microsoft.Extensions.DependencyInjection;
using Shuttle.Core.Contract;

namespace Shuttle.Esb.OpenTelemetry
{
    public class OpenTelemetryBuilder
    {
        private OpenTelemetryOptions _sentinelOptions = new OpenTelemetryOptions();

        public OpenTelemetryBuilder(IServiceCollection services)
        {
            Guard.AgainstNull(services, nameof(services));

            Services = services;
        }

        public OpenTelemetryOptions Options
        {
            get => _sentinelOptions;
            set => _sentinelOptions = value ?? throw new ArgumentNullException(nameof(value));
        }

        public IServiceCollection Services { get; }
    }
}