using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public interface IDispatchTransportMessagePipelineObserver :
        IPipelineObserver<OnPipelineStarting>,
        IPipelineObserver<OnAfterFindRouteForMessage>,
        IPipelineObserver<OnAfterSerializeTransportMessage>,
        IPipelineObserver<OnAfterDispatchTransportMessage>
    {
    }
}