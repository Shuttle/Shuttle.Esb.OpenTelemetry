using System;
using System.IO;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public class TransportMessagePipelineObserver :
        IPipelineObserver<OnPipelineStarting>,
        IPipelineObserver<OnAfterAssembleMessage>,
        IPipelineObserver<OnAfterSerializeMessage>,
        IPipelineObserver<OnAfterEncryptMessage>,
        IPipelineObserver<OnAfterCompressMessage>
    {
        private readonly ServiceBusOpenTelemetryOptions _openTelemetryOptions;
        private readonly Tracer _tracer;
        private readonly string _transportMessagePipeline = nameof(TransportMessagePipeline);

        public TransportMessagePipelineObserver(ServiceBusOpenTelemetryOptions openTelemetryOptions, Tracer tracer)
        {
            Guard.AgainstNull(openTelemetryOptions, nameof(openTelemetryOptions));
            Guard.AgainstNull(tracer, nameof(tracer));

            _tracer = tracer;
            _openTelemetryOptions = openTelemetryOptions;
        }

        public void Execute(OnAfterAssembleMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;

                state.GetTelemetrySpan()?.Dispose();
                state.SetTelemetrySpan(_tracer.StartActiveSpan("OnSerializeMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterCompressMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;

                state.GetTelemetrySpan()?.SetAttribute("CompressionAlgorithm)", state.GetTransportMessage().CompressionAlgorithm);

                state.GetTelemetrySpan()?.Dispose();
                state.GetRootTelemetrySpan()?.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterEncryptMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;

                state.GetTelemetrySpan()?.SetAttribute("EncryptionAlgorithm", state.GetTransportMessage().EncryptionAlgorithm);

                state.GetTelemetrySpan()?.Dispose();
                state.SetTelemetrySpan(_tracer.StartActiveSpan("OnCompressMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnAfterSerializeMessage pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            try
            {
                var state = pipelineEvent.Pipeline.State;

                if (_openTelemetryOptions.IncludeSerializedMessage)
                {
                    using (var reader = new StreamReader(new MemoryStream(state.GetMessageBytes())))
                    {
                        state.GetTelemetrySpan()?.SetAttribute("SerializedMessage", reader.ReadToEnd());
                    }
                }

                state.GetTelemetrySpan()?.Dispose();
                state.SetTelemetrySpan(_tracer.StartActiveSpan("OnEncryptMessage"));
            }
            catch
            {
                // ignored
            }
        }

        public void Execute(OnPipelineStarting pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            var state = pipelineEvent.Pipeline.State;
            try
            {
                var telemetrySpan = _tracer.StartActiveSpan(_transportMessagePipeline);

                telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
                telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                state.SetPipelineTelemetrySpan(telemetrySpan);

                telemetrySpan = _tracer.StartActiveSpan("OnAssembleMessage");

                telemetrySpan?.SetAttribute("MessageType", state.GetMessage().GetType().FullName);

                state.SetTelemetrySpan(telemetrySpan);
            }
            catch
            {
                // ignored
            }
        }
    }
}