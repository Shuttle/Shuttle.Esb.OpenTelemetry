using Shuttle.Core.Pipelines;
using System.Threading;
using OpenTelemetry.Trace;

namespace Shuttle.Esb.OpenTelemetry
{
    public static class PipelineStateExtensions
    {
        public static TelemetrySpan GetTelemetrySpan(this IState state)
        {
            return state.Get<TelemetrySpan>(StateKeys.TelemetrySpan);
        }

        public static void SetTelemetrySpan(this IState state, TelemetrySpan telemetrySpan)
        {
            state.Replace(StateKeys.TelemetrySpan, telemetrySpan);
        }

        public static TelemetrySpan GetRootTelemetrySpan(this IState state)
        {
            return state.Get<TelemetrySpan>(StateKeys.PipelineTelemetrySpan);
        }

        public static void SetPipelineTelemetrySpan(this IState state, TelemetrySpan telemetrySpan)
        {
            state.Replace(StateKeys.PipelineTelemetrySpan, telemetrySpan);
        }
    }
}