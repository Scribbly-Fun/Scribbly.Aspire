using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Scribbly.Aspire;

internal sealed class AspireBotHealthCheck : IHealthCheck
{
    private readonly AspireBotResource _resource;

    public AspireBotHealthCheck(AspireBotResource resource)
    {
        _resource = resource;
    }
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        return _resource.IsRunning
            ? Task.FromResult(HealthCheckResult.Healthy())
            : Task.FromResult(HealthCheckResult.Degraded());
    }
}