using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Web;
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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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

                var parentTraceId = transportMessage.Headers.Contains(TransportHeaderKeys.ParentTraceId) ? transportMessage.Headers.GetHeaderValue(TransportHeaderKeys.ParentTraceId) : null;
                var baggage = transportMessage.Headers.Contains(TransportHeaderKeys.Baggage) ? transportMessage.Headers.GetHeaderValue(TransportHeaderKeys.Baggage) : null;

                var telemetrySpan = string.IsNullOrEmpty(parentTraceId)
                    ? _tracer.StartActiveSpan("OnHandleMessage")
                    : _tracer.StartActiveSpan("OnHandleMessage", SpanKind.Consumer, new SpanContext(ActivityTraceId.CreateFromString(parentTraceId.ToCharArray()), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded));

                if (string.IsNullOrEmpty(baggage))
                {
                    if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
                    {
                        Baggage.Current.SetBaggage("CorrelationId", transportMessage.CorrelationId);
                    }

                    Baggage.Current.SetBaggage("MessageId", transportMessage.MessageId.ToString());
                }
                else
                {
                    var baggagePairs = new List<KeyValuePair<string, string>>();
                    var baggageItems = baggage.Split(',');
                    
                    if (baggageItems.Length > 0)
                    {
                        foreach (var item in baggageItems)
                        {
                            if (NameValueHeaderValue.TryParse(item, out var baggageItem))
                            {
                                baggagePairs.Add(new KeyValuePair<string, string>(baggageItem.Name, HttpUtility.UrlDecode(baggageItem.Value)));
                            }
                        }
                    }

                    for (var i = baggagePairs.Count - 1; i >= 0; i--)
                    {
                        var baggagePair = baggagePairs[i];
                        
                        Baggage.Current.SetBaggage(baggagePair.Key, baggagePair.Value);
                    }
                }

                if (!string.IsNullOrEmpty(transportMessage.CorrelationId))
                {
                    telemetrySpan?.SetAttribute("CorrelationId", transportMessage.CorrelationId);
                }

                telemetrySpan?.SetAttribute("MessageId", transportMessage.MessageId.ToString());
                telemetrySpan?.SetAttribute("MessageType", transportMessage.MessageType);

                state.SetTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterHandleMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var telemetrySpan = _tracer.StartActiveSpan(_inboxMessagePipelineName);

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