using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public static class ServiceCollectionExtensions
    {
        public static TracerProviderBuilder AddServiceBusInstrumentation(this TracerProviderBuilder builder)
        {
            Guard.AgainstNull(builder, nameof(builder));

            builder.AddSource("Shuttle.Esb");

            return builder;
        }

        public static IServiceCollection AddServiceBusInstrumentation(this IServiceCollection services,
            Action<OpenTelemetryBuilder> builder = null)
        {
            Guard.AgainstNull(services, nameof(services));

            var sentinelModuleBuilder = new OpenTelemetryBuilder(services);

            builder?.Invoke(sentinelModuleBuilder);

            services.AddOptions<OpenTelemetryOptions>().Configure(options =>
            {
                options.Enabled = sentinelModuleBuilder.Options.Enabled;
            });

            services.AddPipelineModule<OpenTelemetryModule>();

            return services;
        }
    }
}