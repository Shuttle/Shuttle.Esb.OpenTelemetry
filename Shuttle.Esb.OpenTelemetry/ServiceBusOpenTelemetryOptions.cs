using System;

namespace Shuttle.Esb.OpenTelemetry
{
    public class ServiceBusOpenTelemetryOptions
    {
        public const string SectionName = "Shuttle:Instrumentation:ServiceBus";

        public bool Enabled { get; set; } = true;
        public bool IncludeSerializedMessage { get; set; } = true;
        public bool TransientInstance { get; set; }
        public TimeSpan HeartbeatIntervalDuration { get; set; } = TimeSpan.FromSeconds(30);
    }
}