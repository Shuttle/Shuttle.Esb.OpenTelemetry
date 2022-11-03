using System;
using System.Linq;
using System.Web;
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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;
                
                var transportMessage = state.GetTransportMessage();
                var telemetrySpan = _tracer.StartActiveSpan(_dispatchTransportMessagePipelineName);

                if (telemetrySpan != null)
                {
                    telemetrySpan.SetAttribute("MachineName", Environment.MachineName);
                    telemetrySpan.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                    if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
                    {
                        Baggage.SetBaggage("CorrelationId", transportMessage.CorrelationId);
                    }

                    Baggage.SetBaggage("MessageId", transportMessage.MessageId.ToString());

                    transportMessage.Headers.Add(new TransportHeader
                    {
                        Key = TransportHeaderKeys.ParentTraceId,
                        Value = telemetrySpan.Context.TraceId.ToString()
                    });

                    if (!transportMessage.Headers.Contains(TransportHeaderKeys.Baggage))
                    {
                        var baggage = string.Join(",", Baggage.GetBaggage().Select(item => $"{item.Key}={HttpUtility.UrlEncode(item.Value)}"));

                        if (!string.IsNullOrEmpty(baggage))
                        {
                            transportMessage.Headers.Add(new TransportHeader
                            {
                                Key = TransportHeaderKeys.Baggage,
                                Value = baggage
                            });
                        }
                    }

                    state.SetPipelineTelemetrySpan(telemetrySpan);
                }

                telemetrySpan = _tracer.StartActiveSpan("OnFindRouteForMessage");

                telemetrySpan?.SetAttribute("MessageType", transportMessage.MessageType);

                state.SetTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterFindRouteForMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;

                state.GetTelemetrySpan()?.SetAttribute("RecipientInboxWorkQueueUri", state.GetTransportMessage().RecipientInboxWorkQueueUri);
                state.GetTelemetrySpan()?.Dispose();
                state.SetTelemetrySpan(_tracer.StartActiveSpan("OnSerializeTransportMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterSerializeTransportMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                pipelineEvent.Pipeline.State.GetTelemetrySpan()?.Dispose();
                pipelineEvent.Pipeline.State.GetPipelineTelemetrySpan()?.Dispose();
            }
            catch
            {
                // ignored
            }
        }
    }
}