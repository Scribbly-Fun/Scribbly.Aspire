using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Scribbly.Aspire;

public static class ResourceBuilderExtensions
{
    [Experimental("ASPIREINTERACTION001")]
    internal static IResourceBuilder<AspireBotResource> AddAspireBot(
        this IDistributedApplicationBuilder builder, string name, Action<AspireBotOptions>? options = null)
    {
        builder.Services.AddHttpClient<AspireBotClient>();
        builder.Services.AddHostedService<AspireBotBackgroundService>();
        
        var aspireBot = new AspireBotResource(name);
        
        builder.Services.AddSingleton(aspireBot);
        
        var resourceBuilder = builder
            .AddResource(aspireBot)
            .WithIconName("bot");
        
        // Register the health check
        var healthCheckKey = $"{name}_aspire-bot-check";
        
        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            name: healthCheckKey,
            factory: sp => new AspireBotHealthCheck(aspireBot),
            failureStatus: HealthStatus.Unhealthy,
            tags: ["aspire-bot", "healthcheck"]));

        resourceBuilder.WithInitialState(new CustomResourceSnapshot
        {
            ResourceType = "AspireBot",
            CreationTimeStamp = DateTime.UtcNow,
            State = KnownResourceStates.Waiting,
            Properties =
            [
                new(CustomResourceKnownProperties.Source, "AspireBot")
            ]
        });

        resourceBuilder.WithHealthCheck(healthCheckKey);
        
        resourceBuilder.OnInitializeResource(async (resource, @event, ct) =>
        {
            await @event.Notifications.PublishUpdateAsync(resource,  snapShot => snapShot with
            {
                Urls = [..snapShot.Urls, ..resource.Urls],
                State = KnownResourceStates.Running,
                Properties = 
                [
                    .. snapShot.Properties, 
                    new ResourcePropertySnapshot("Aspire Bot Running", resource.IsRunning) { IsSensitive = false }
                ]
            }).ConfigureAwait(false);
        });
        
        resourceBuilder.OnResourceReady(async (resource, @event, cts) =>
        {
            var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
#pragma warning disable ASPIREINTERACTION001
            var interactions = @event.Services.GetRequiredService<IInteractionService>();
            
            if (interactions.IsAvailable)
            {
                await interactions.PromptNotificationAsync(
                    "🤖 Aspire Bot", 
                    "The Aspire Bot has Started Running all Registered Commands",
                    new NotificationInteractionOptions
                    {
                        ShowDismiss = true,
                        Intent = MessageIntent.Warning,
                    }, cts);
            }
#pragma warning restore ASPIREINTERACTION001
            
            await notifications.PublishUpdateAsync(resource,  snapShot =>
            {
                var commandUrls = resource.Urls.ToList();
                
                return snapShot with
                {
                    Urls = [..snapShot.Urls, ..commandUrls],
                };
            }).ConfigureAwait(false);
        });
        
        return resourceBuilder;
    }

    internal static IResourceBuilder<T> WithAspireBotCommand<T>(
        this IResourceBuilder<T> builder, 
        string path, 
        BotCommand.RequestMethod method = BotCommand.RequestMethod.Get,
        string endpoint = "https", 
        TimeSpan? interval = null, 
        Func<HttpContent>? contentRequest = null,
        Func<HttpResponseMessage, ValueTask<object?>>? responseHandler = null) where T : IResourceWithEndpoints
    {
        // TODO: Overload accepting entire HTTP client so we can deal with headers and more complex stuff.
        
        var resource = builder.ApplicationBuilder.Resources.FirstOrDefault(r => r.GetType() == typeof(AspireBotResource));
        if (resource is not AspireBotResource aspireBotResource)
        {
            throw new InvalidOperationException("Please Register the Aspire Bot Resource");
        }
        
        if (method is BotCommand.RequestMethod.Get or BotCommand.RequestMethod.Delete && contentRequest is not null)
        {
            throw new InvalidOperationException("GET and DELETE tdo not support Content");
        }
        
        var endpointReference = builder.GetEndpoint(endpoint);
        var command = new BotCommand(path, method, interval, contentRequest, responseHandler);
        
        builder.WithCommand($"aspire-bot-{path}", $"Run {path}", async context =>
            {
#pragma warning disable ASPIREINTERACTION001
                var client = context.ServiceProvider.GetRequiredService<AspireBotClient>();
                var botResource = context.ServiceProvider.GetRequiredService<AspireBotResource>();
                
                var reference = botResource.EndpointCommands
                    .FirstOrDefault(c => context.ResourceName.StartsWith(c.Key.Resource.Name, StringComparison.InvariantCultureIgnoreCase)).Key;

                if (reference is null)
                {
                    return new ExecuteCommandResult
                    {
                        Success = false
                    };
                }

                await client.RunJob(reference, command, context.CancellationToken);
                
                return new ExecuteCommandResult
                {
                    Success = false
                };
                
#pragma warning restore ASPIREINTERACTION001
            },
            new CommandOptions
            {
                Description = "Run",
                IconName = "Bot",
                IconVariant = IconVariant.Filled,
                IsHighlighted = true
            });
        
        if (!command.IsPollingCommand) 
        {
            // TODO: Add manual command to trigger the aspire bot to run the http method.
        }
        
        aspireBotResource.AddCommand(endpointReference, command);
        
        return builder;
    }
}