using System;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public class DispatchTransportMessagePipelineObserver :
        IPipelineObserver<OnPipelineStarting>,
        IPipelineObserver<OnAfterFindRouteForMessage>,
        IPipelineObserver<OnAfterSerializeTransportMessage>,
        IPipelineObserver<OnAfterDispatchTransportMessage>
    {
        private readonly string _dispatchTransportMessagePipelineName = nameof(DispatchTransportMessagePipeline);
        private readonly Tracer _tracer;

        public DispatchTransportMessagePipelineObserver(Tracer tracer)
        {
            Guard.AgainstNull(tracer, nameof(tracer));

            _tracer = tracer;
        }

        public void Execute(OnPipelineStarting pipelineEvent)
        {
            try
            {
                var state = pipelineEvent.Pipeline.State;

                var transportMessage = state.GetTransportMessage();
                var telemetrySpan = _tracer.StartActiveSpan(_dispatchTransportMessagePipelineName);

                telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
                telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
                {
                    Baggage.SetBaggage("CorrelationId", transportMessage.CorrelationId);
                }

                Baggage.SetBaggage("MessageId", transportMessage.MessageId.ToString());

                pipelineEvent.Pipeline.State.SetPipelineTelemetrySpan(telemetrySpan);

                telemetrySpan = _tracer.StartActiveSpan("OnFindRouteForMessage");

                telemetrySpan?.SetAttribute("MessageType", transportMessage.MessageType);

                pipelineEvent.Pipeline.State.SetTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterFindRouteForMessage pipelineEvent)
        {
            try
            {
                pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
                pipelineEvent.Pipeline.State.SetTelemetrySpan(_tracer.StartActiveSpan("OnSerializeTransportMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterSerializeTransportMessage pipelineEvent)
        {
            try
            {
                pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
                pipelineEvent.Pipeline.State.SetTelemetrySpan(_tracer.StartActiveSpan("OnDispatchTransportMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterDispatchTransportMessage pipelineEvent)
        {
            try
            {
                pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
                pipelineEvent.Pipeline.State.GetRootTelemetrySpan()?.Dispose();
            }
            catch
            {
                // ignored
            }
        }
    }
}