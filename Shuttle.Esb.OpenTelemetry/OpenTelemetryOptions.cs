using System;

namespace Shuttle.Esb.OpenTelemetry
{
    public class OpenTelemetryOptions
    {
        public const string SectionName = "Shuttle:Modules:ServiceBusInstrumentation";

        public bool Enabled { get; set; } = true;
        public bool IncludeSerializedMessage { get; set; } = true;
        public bool TransientInstance { get; set; }
        public TimeSpan HeartbeatIntervalDuration { get; set; } = TimeSpan.FromSeconds(30);
    }
}