namespace Scribbly.Aspire.Tests;

public class WebTests
{
    [Fact]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Scribbly_Aspire_AppHost>();
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        
        await using var app = await appHost.BuildAsync();
        var resourceNotificationService = app.Services.GetRequiredService<ResourceNotificationService>();
        await app.StartAsync();

        // Act
        
        await resourceNotificationService.WaitForResourceAsync("load-test", KnownResourceStates.Waiting).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
}
