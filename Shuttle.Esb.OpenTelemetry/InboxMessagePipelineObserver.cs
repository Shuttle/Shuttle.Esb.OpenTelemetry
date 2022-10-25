using System;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public class InboxMessagePipelineObserver :
        IPipelineObserver<OnPipelineStarting>,
        IPipelineObserver<OnBeforeHandleMessage>,
        IPipelineObserver<OnAfterHandleMessage>,
        IPipelineObserver<OnPipelineException>,
        IPipelineObserver<OnAfterDispatchTransportMessage>
    {
        private readonly string _inboxMessagePipelineName = nameof(InboxMessagePipeline);
        private readonly Tracer _tracer;

        public InboxMessagePipelineObserver(Tracer tracer)
        {
            Guard.AgainstNull(tracer, nameof(tracer));

            _tracer = tracer;
        }

        public void Execute(OnBeforeHandleMessage pipelineEvent)
        {
            try
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
                    Baggage.Current.SetBaggage("CorrelationId", transportMessage.CorrelationId);
                }

                Baggage.Current.SetBaggage("MessageId", transportMessage.MessageId.ToString());

                pipelineEvent.Pipeline.State.SetTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterHandleMessage pipelineEvent)
        {
            try
            {
                pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnPipelineException pipelineEvent)
        {
            try
            {
                var state = pipelineEvent.Pipeline.State;

                using (var telemetrySpan = state.GetTelemetrySpan())
                {
                    telemetrySpan?.RecordException(pipelineEvent.Pipeline.Exception);
                    telemetrySpan?.SetStatus(Status.Error);
                }
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
                pipelineEvent.Pipeline.State.GetRootTelemetrySpan()?.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnPipelineStarting pipelineEvent)
        {
            try
            {
                var telemetrySpan = _tracer.StartRootSpan(_inboxMessagePipelineName);

                telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
                telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                pipelineEvent.Pipeline.State.SetPipelineTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }
    }
}