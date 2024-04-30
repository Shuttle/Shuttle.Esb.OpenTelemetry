using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public static class ServiceCollectionExtensions
    {
        public static TracerProviderBuilder AddServiceBusInstrumentation(this TracerProviderBuilder tracerProviderBuilder, Action<OpenTelemetryBuilder> builder = null)
        {
            Guard.AgainstNull(tracerProviderBuilder, nameof(tracerProviderBuilder));

            tracerProviderBuilder.AddSource("Shuttle.Esb");

            tracerProviderBuilder.ConfigureServices(services =>
            {
                var openTelemetryBuilder = new OpenTelemetryBuilder(services);

                builder?.Invoke(openTelemetryBuilder);

                services.AddOptions<ServiceBusOpenTelemetryOptions>().Configure(options =>
                {
                    options.Enabled = openTelemetryBuilder.Options.Enabled;
                    options.IncludeSerializedMessage = openTelemetryBuilder.Options.IncludeSerializedMessage;
                    options.HeartbeatIntervalDuration = openTelemetryBuilder.Options.HeartbeatIntervalDuration;
                    options.TransientInstance = openTelemetryBuilder.Options.TransientInstance;
                });

                services.AddHostedService<OpenTelemetryHostedService>();
            });

            return tracerProviderBuilder;
        }
    }
}