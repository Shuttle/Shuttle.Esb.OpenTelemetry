using Shuttle.Core.Pipelines;

namespace Shuttle.Esb.OpenTelemetry
{
    public interface IInboxMessagePipelineObserver :
        IPipelineObserver<OnPipelineStarting>,
        IPipelineObserver<OnBeforeHandleMessage>,
        IPipelineObserver<OnAfterHandleMessage>,
        IPipelineObserver<OnPipelineException>,
        IPipelineObserver<OnAfterDispatchTransportMessage>
    {
    }
}