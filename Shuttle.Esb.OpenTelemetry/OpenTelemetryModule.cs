using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Shuttle.Core.Contract;
using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public class OpenTelemetryModule : IDisposable, IPipelineObserver<OnStarted>
    {
        private readonly Type _dispatchTransportMessagePipelineType = typeof(DispatchTransportMessagePipeline);
        private readonly string _environmentName;
        private readonly Type _inboxMessagePipelineType = typeof(InboxMessagePipeline);
        private readonly IInboxMessagePipelineObserver _inboxMessagePipelineObserver;
        private readonly IDispatchTransportMessagePipelineObserver _dispatchTransportMessagePipelineObserver;
        private readonly OpenTelemetryOptions _openTelemetryOptions;
        private readonly ServiceBusOptions _serviceBusOptions;
        private readonly Type _startupPipelineType = typeof(StartupPipeline);

        private readonly Tracer _tracer;

        private volatile bool _active;
        private CancellationToken _cancellationToken;
        private DateTime _nextSendDate = DateTime.UtcNow;
        private Thread _thread;
        private string _ipv4Address;
        private bool _tracingStarted;

        public OpenTelemetryModule(IOptions<OpenTelemetryOptions> sentinelOptions, IOptions<ServiceBusOptions> serviceBusOptions, Tracer tracer, IPipelineFactory pipelineFactory, IInboxMessagePipelineObserver inboxMessagePipelineObserver, IDispatchTransportMessagePipelineObserver dispatchTransportMessagePipelineObserver, IHostEnvironment hostEnvironment, IHostApplicationLifetime hostApplicationLifetime)
        {
            Guard.AgainstNull(sentinelOptions, nameof(sentinelOptions));
            Guard.AgainstNull(sentinelOptions.Value, nameof(sentinelOptions.Value));
            Guard.AgainstNull(serviceBusOptions, nameof(serviceBusOptions));
            Guard.AgainstNull(serviceBusOptions.Value, nameof(serviceBusOptions.Value));
            Guard.AgainstNull(tracer, nameof(tracer));
            Guard.AgainstNull(pipelineFactory, nameof(pipelineFactory));
            Guard.AgainstNull(inboxMessagePipelineObserver, nameof(inboxMessagePipelineObserver));
            Guard.AgainstNull(dispatchTransportMessagePipelineObserver, nameof(dispatchTransportMessagePipelineObserver));
            Guard.AgainstNull(hostEnvironment, nameof(hostEnvironment));
            Guard.AgainstNullOrEmptyString(hostEnvironment.EnvironmentName, nameof(hostEnvironment.EnvironmentName));

            _serviceBusOptions = serviceBusOptions.Value;
            _openTelemetryOptions = sentinelOptions.Value;
            _tracer = tracer;
            _inboxMessagePipelineObserver = inboxMessagePipelineObserver;
            _dispatchTransportMessagePipelineObserver = dispatchTransportMessagePipelineObserver;
            _environmentName = hostEnvironment.EnvironmentName;

            if (!_openTelemetryOptions.Enabled)
            {
                return;
            }

            hostApplicationLifetime.ApplicationStopping.Register(() =>
            {
                using (var telemetrySpan = _tracer.StartRootSpan("EndpointStopped"))
                {
                    telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
                    telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);
                }
            });

            pipelineFactory.PipelineCreated += PipelineCreated;
        }

        public void Dispose()
        {
            _active = false;
        }

        public void Execute(OnStarted pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

            _cancellationToken = pipelineEvent.Pipeline.State.GetCancellationToken();

            _ipv4Address = "0.0.0.0";

            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                _ipv4Address = ip.ToString();
            }

            _thread = new Thread(Heartbeat);

            _active = true;
            _thread.Start();

            while (!_thread.IsAlive)
            {
            }
        }

        private void Heartbeat()
        {
            while (_active)
            {
                if (_nextSendDate <= DateTime.UtcNow)
                {
                    using (var telemetrySpan = _tracer.StartRootSpan(_tracingStarted ? "Heartbeat" : "EndpointStarted"))
                    {
                        telemetrySpan?.SetAttribute("MachineName", Environment.MachineName);
                        telemetrySpan?.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                        if (telemetrySpan != null && telemetrySpan.IsRecording  && !_tracingStarted)
                        {
                            telemetrySpan?.SetAttribute("IPv4Address", _ipv4Address);
                            telemetrySpan?.SetAttribute("EntryAssemblyQualifiedName", Assembly.GetEntryAssembly()?.FullName ?? "(unknown)");
                            telemetrySpan?.SetAttribute("InboxWorkQueueUri", _serviceBusOptions.Inbox?.WorkQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("InboxDeferredQueueUri", _serviceBusOptions.Inbox?.DeferredQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("InboxErrorQueueUri", _serviceBusOptions.Inbox?.ErrorQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("OutboxWorkQueueUri", _serviceBusOptions.Outbox?.WorkQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("OutboxErrorQueueUri", _serviceBusOptions.Outbox?.ErrorQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("ControlInboxWorkQueueUri", _serviceBusOptions.ControlInbox?.WorkQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("ControlInboxErrorQueueUri", _serviceBusOptions.ControlInbox?.ErrorQueueUri ?? string.Empty);
                            telemetrySpan?.SetAttribute("TransientInstance", _openTelemetryOptions.TransientInstance);
                            telemetrySpan?.SetAttribute("EnvironmentName", _environmentName);

                            _tracingStarted = true;
                        }
                    }

                    _nextSendDate = DateTime.UtcNow.Add(_tracingStarted ? _openTelemetryOptions.HeartbeatIntervalDuration : TimeSpan.FromSeconds(1));
                }

                Task.Delay(1000, _cancellationToken).Wait(_cancellationToken);
            }

            _thread.Join(TimeSpan.FromSeconds(5));
        }

        private void PipelineCreated(object sender, PipelineEventArgs e)
        {
            var pipelineType = e.Pipeline.GetType();

            if (pipelineType == _startupPipelineType)
            {
                e.Pipeline.RegisterObserver(this);

                return;
            }
            
            if (pipelineType == _inboxMessagePipelineType)
            {
                e.Pipeline.GetStage("Handle")
                    .BeforeEvent<OnHandleMessage>()
                    .Register<OnBeforeHandleMessage>();

                e.Pipeline.RegisterObserver(_inboxMessagePipelineObserver);
            }

            if (pipelineType == _dispatchTransportMessagePipelineType)
            {
                e.Pipeline.RegisterObserver(_dispatchTransportMessagePipelineObserver);
            }
        }
    }
}