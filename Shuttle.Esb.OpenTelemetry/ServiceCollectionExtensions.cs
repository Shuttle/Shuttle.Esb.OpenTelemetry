using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public static class ServiceCollectionExtensions
    {
        public static TracerProviderBuilder AddServiceBusSource(this TracerProviderBuilder builder)
        {
            Guard.AgainstNull(builder, nameof(builder));

            builder.AddSource("Shuttle.Esb");

            return builder;
        }

        public static IServiceCollection AddServiceBusInstrumentation(this IServiceCollection services,
            Action<OpenTelemetryBuilder> builder = null)
        {
            Guard.AgainstNull(services, nameof(services));

            var openTelemetryBuilder = new OpenTelemetryBuilder(services);

            builder?.Invoke(openTelemetryBuilder);

            services.AddOptions<ServiceBusOpenTelemetryOptions>().Configure(options =>
            {
                options.Enabled = openTelemetryBuilder.Options.Enabled;
                options.IncludeSerializedMessage = openTelemetryBuilder.Options.IncludeSerializedMessage;
                options.HeartbeatIntervalDuration = openTelemetryBuilder.Options.HeartbeatIntervalDuration;
                options.TransientInstance = openTelemetryBuilder.Options.TransientInstance;
            });

            services.AddPipelineModule<OpenTelemetryModule>();

            return services;
        }
    }
}