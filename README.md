# Shuttle.Esb.OpenTelemetry

```
PM> Install-Package Shuttle.Esb.OpenTelemetry
```

OpenTelemetry instrumentation for Shuttle.Esb implementations.

## Configuration

```c#
services.AddServiceBusInstrumentation(builder =>
{
	// default values
    builder.Options.Enabled = true;
    builder.Options.IncludeSerializedMessage = true;
    builder.Options.TransientInstance = false;
    builder.Options.HeartbeatIntervalDuration = TimeSpan.FromSeconds(30);

	// or bind from configuration
	configuration
		.GetSection(ServiceBusOpenTelemetryOptions.SectionName)
		.Bind(builder.Options);
});


## Options

| Option | Default	| Description |
| --- | --- | --- | 
| `Enabled` | `true` | Indicates whether to perform instrumentation. |
| `IncludeSerializedMessage` | `true` | If 'true', includes the serialized message as attribute `SerializedMessage` in the trace. |
| `TransientInstance` | `false` | Indicates whether the endpoint is transient, such as when deployed as a container. |
| `HeartbeatIntervalDuration` | `00:00:30` | The duration between `Heartbeat` traces. |
