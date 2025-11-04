using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;

namespace Scribbly.Aspire;

[Experimental("ASPIREINTERACTION001")]
internal sealed class AspireBotBackgroundService : BackgroundService
{
    private readonly AspireBotClient _botClient;
    private readonly AspireBotResource _resource;
    
    public AspireBotBackgroundService(AspireBotClient botClient, AspireBotResource resource)
    {
        _botClient = botClient;
        _resource = resource;
    }
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _resource.IsRunning = true;
            
            while (!stoppingToken.IsCancellationRequested)
            {
            
                // TODO: Calculate each commands polling routine.
            
                foreach (var resource in _resource.EndpointCommands)
                {
                    foreach (var botCommand in resource.Value.Where(c => c.IsPollingCommand))
                    {
                        await _botClient.RunJob(resource.Key, botCommand, stoppingToken);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {
            _resource.IsRunning = false;
        }
    }
}