using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System;

namespace Shuttle.Esb.OpenTelemetry
{
    public class InboxMessagePipelineObserver : IInboxMessagePipelineObserver
    {
        private readonly Tracer _tracer;
        private readonly string _inboxMessagePipelineName = nameof(InboxMessagePipeline);

        public InboxMessagePipelineObserver(Tracer tracer)
        {
            Guard.AgainstNull(tracer, nameof(tracer));

            _tracer = tracer;
        }

        public void Execute(OnBeforeHandleMessage pipelineEvent)
        {
            var state = pipelineEvent.Pipeline.State;
            var processingStatus = state.GetProcessingStatus();

            if (processingStatus == ProcessingStatus.Ignore || processingStatus == ProcessingStatus.MessageHandled)
            {
                return;
            }

            var transportMessage = state.GetTransportMessage();

            if (transportMessage.HasExpired())
            {
                return;
            }

            var telemetrySpan = _tracer.StartActiveSpan("OnHandleMessage");

            if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
            {
                Baggage.SetBaggage("CorrelationId", transportMessage.CorrelationId);
            }

            Baggage.SetBaggage("MessageId", transportMessage.MessageId.ToString());

            pipelineEvent.Pipeline.State.SetTelemetrySpan(telemetrySpan);
        }

        public void Execute(OnAfterHandleMessage pipelineEvent)
        {
            pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
        }

        public void Execute(OnPipelineException pipelineEvent)
        {
            var state = pipelineEvent.Pipeline.State;

            using (var telemetrySpan = state.GetTelemetrySpan())
            {
                telemetrySpan?.RecordException(pipelineEvent.Pipeline.Exception);
                telemetrySpan?.SetStatus(Status.Error);
            }
        }

        public void Execute(OnAfterDispatchTransportMessage pipelineEvent)
        {
            pipelineEvent.Pipeline.State.GetRootTelemetrySpan()?.Dispose();
        }

        public void Execute(OnPipelineStarting pipelineEvent)
        {
            var telemetrySpan = _tracer.StartRootSpan(_inboxMessagePipelineName);

            telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
            telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);
            
            pipelineEvent.Pipeline.State.SetRootTelemetrySpan(telemetrySpan);
        }
    }
}