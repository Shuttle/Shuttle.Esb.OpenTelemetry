﻿using System;
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
using Shuttle.Core.Threading;

namespace Shuttle.Esb.OpenTelemetry
{
    public class OpenTelemetryHostedService : IHostedService, IPipelineObserver<OnStarted>
    {
        private readonly string _environmentName;
        private readonly Type _dispatchTransportMessagePipelineType = typeof(DispatchTransportMessagePipeline);
        private readonly Type _inboxMessagePipelineType = typeof(InboxMessagePipeline);
        private readonly Type _transportMessagePipelineType = typeof(TransportMessagePipeline);
        private readonly InboxMessagePipelineObserver _inboxMessagePipelineObserver;
        private readonly DispatchTransportMessagePipelineObserver _dispatchTransportMessagePipelineObserver;
        private readonly ServiceBusOpenTelemetryOptions _openTelemetryOptions;
        private readonly ServiceBusOptions _serviceBusOptions;
        private readonly Type _startupPipelineType = typeof(StartupPipeline);

        private readonly Tracer _tracer;

        private volatile bool _active;
        private readonly CancellationToken _cancellationToken;
        private DateTime _nextSendDate = DateTime.UtcNow;
        private Thread _thread;
        private string _ipv4Address;
        private bool _tracingStarted;
        private readonly TransportMessagePipelineObserver _transportMessagePipelineObserver;
        private readonly IPipelineFactory _pipelineFactory;

        public OpenTelemetryHostedService(IOptions<ServiceBusOpenTelemetryOptions> openTelemetryOptions, IOptions<ServiceBusOptions> serviceBusOptions, TracerProvider tracerProvider, IPipelineFactory pipelineFactory, ICancellationTokenSource cancellationTokenSource, IHostEnvironment hostEnvironment, IHostApplicationLifetime hostApplicationLifetime)
        {
            Guard.AgainstNull(openTelemetryOptions, nameof(openTelemetryOptions));
            Guard.AgainstNull(serviceBusOptions, nameof(serviceBusOptions));
            Guard.AgainstNull(tracerProvider, nameof(tracerProvider));
            Guard.AgainstNull(hostEnvironment, nameof(hostEnvironment));
            Guard.AgainstNullOrEmptyString(hostEnvironment.EnvironmentName, nameof(hostEnvironment.EnvironmentName));

            _serviceBusOptions = Guard.AgainstNull(serviceBusOptions.Value, nameof(serviceBusOptions.Value));
            _openTelemetryOptions = Guard.AgainstNull(openTelemetryOptions.Value, nameof(openTelemetryOptions.Value));
            _environmentName = hostEnvironment.EnvironmentName;

            if (!_openTelemetryOptions.Enabled)
            {
                return;
            }

            _pipelineFactory = Guard.AgainstNull(pipelineFactory, nameof(pipelineFactory));

            _cancellationToken = cancellationTokenSource.Get().Token;

            _tracer = tracerProvider.GetTracer("Shuttle.Esb");

            _inboxMessagePipelineObserver = new InboxMessagePipelineObserver(_tracer);
            _dispatchTransportMessagePipelineObserver = new DispatchTransportMessagePipelineObserver(_tracer);
            _transportMessagePipelineObserver = new TransportMessagePipelineObserver(_openTelemetryOptions, _tracer);

            hostApplicationLifetime.ApplicationStopping.Register(() =>
            {
                using (var telemetrySpan = _tracer.StartRootSpan("EndpointStopped"))
                {
                    telemetrySpan.SetAttribute("MachineName", Environment.MachineName);
                    telemetrySpan.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);
                }
            });

            pipelineFactory.PipelineCreated += PipelineCreated;
        }

        private void Heartbeat()
        {
            while (_active)
            {
                if (_nextSendDate <= DateTime.UtcNow)
                {
                    using (var telemetrySpan = _tracer.StartRootSpan(_tracingStarted ? "Heartbeat" : "EndpointStarted"))
                    {
                        telemetrySpan.SetAttribute("MachineName", Environment.MachineName);
                        telemetrySpan.SetAttribute("BaseDirectory", AppDomain.CurrentDomain.BaseDirectory);

                        if (telemetrySpan is { IsRecording: true } && !_tracingStarted)
                        {
                            telemetrySpan.SetAttribute("IPv4Address", _ipv4Address);
                            telemetrySpan.SetAttribute("EntryAssemblyQualifiedName", Assembly.GetEntryAssembly()?.FullName ?? "(unknown)");
                            telemetrySpan.SetAttribute("InboxWorkQueueUri", _serviceBusOptions.Inbox?.WorkQueueUri ?? string.Empty);
                            telemetrySpan.SetAttribute("InboxDeferredQueueUri", _serviceBusOptions.Inbox?.DeferredQueueUri ?? string.Empty);
                            telemetrySpan.SetAttribute("InboxErrorQueueUri", _serviceBusOptions.Inbox?.ErrorQueueUri ?? string.Empty);
                            telemetrySpan.SetAttribute("OutboxWorkQueueUri", _serviceBusOptions.Outbox?.WorkQueueUri ?? string.Empty);
                            telemetrySpan.SetAttribute("OutboxErrorQueueUri", _serviceBusOptions.Outbox?.ErrorQueueUri ?? string.Empty);
                            telemetrySpan.SetAttribute("TransientInstance", _openTelemetryOptions.TransientInstance);
                            telemetrySpan.SetAttribute("EnvironmentName", _environmentName);

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

            if (pipelineType == _transportMessagePipelineType)
            {
                e.Pipeline.RegisterObserver(_transportMessagePipelineObserver);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _active = false;

            if (_openTelemetryOptions.Enabled)
            {
                _pipelineFactory.PipelineCreated -= PipelineCreated;
            }

            await Task.CompletedTask;
        }

        public void Execute(OnStarted pipelineEvent)
        {
            Guard.AgainstNull(pipelineEvent, nameof(pipelineEvent));

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

        public async Task ExecuteAsync(OnStarted pipelineEvent)
        {
            Execute(pipelineEvent);

            await Task.CompletedTask;
        }
    }
}