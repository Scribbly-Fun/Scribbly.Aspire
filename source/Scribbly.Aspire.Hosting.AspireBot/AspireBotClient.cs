using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire;

[Experimental("ASPIREINTERACTION001")]
internal sealed class AspireBotClient
{
    private readonly HttpClient _client;
    private readonly IInteractionService _interactionService;

    public AspireBotClient(HttpClient client, IInteractionService interactionService)
    {
        _client = client;
        _interactionService = interactionService;
    }

    // TODO: Track when jobs have run to determine if they should. Add VERB
    internal async Task RunJob(EndpointReference endpoint, BotCommand command, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage
        {
            Method = command.HttpMethod,
            RequestUri = new Uri(endpoint.Url + command.Path),
            Content = command.ContentRequestDelegate?.Invoke()
        };

        using var response = await _client.SendAsync(request, cancellation);

        if (command.ResponseHandler is not null)
        {
            var result = await command.ResponseHandler.Invoke(response);
            
            if (_interactionService.IsAvailable)
            {
                await _interactionService.PromptNotificationAsync(
                    "🤖 Aspire Bot", 
                    $"Executed {command.Method} {command.Path} {response.StatusCode} {result}",
                    new NotificationInteractionOptions
                    {
                        LinkUrl = request.RequestUri.ToString(),
                        ShowDismiss = true,
                        Intent = response.IsSuccessStatusCode ? MessageIntent.Success : MessageIntent.Warning,
                    }, cancellation);
            }
            
            return;
        }
        
        if (_interactionService.IsAvailable)
        {
            await _interactionService.PromptNotificationAsync(
                "🤖 Aspire Bot", 
                $"Executed {command.Method} {command.Path} {response.StatusCode}",
                new NotificationInteractionOptions
                {
                    LinkUrl = request.RequestUri.ToString(),
                    ShowDismiss = true,
                    Intent = response.IsSuccessStatusCode ? MessageIntent.Success : MessageIntent.Warning,
                }, cancellation);
        }
    }
}