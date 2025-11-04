namespace Scribbly.Aspire.Resources.LoadTesting;

[Collection(nameof(LoadTestingResourceCollection))]
public class ResourceTests(LoadTestingResourceFactory app)
{
    [Fact]
    public async Task LoadTest_InitialResourceState_ShouldBeWaiting()
    {
        // // Arrange
        var resourceNotificationService = app.Services!.GetRequiredService<ResourceNotificationService>();

        // Act
        
        await resourceNotificationService.WaitForResourceAsync("load-tester", KnownResourceStates.Waiting).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
    
    [Fact]
    public async Task LoadTest_Should_Create_K6_Resource()
    {
        // Arrange
        var resourceNotificationService = app.Services!.GetRequiredService<ResourceNotificationService>();

        // Act
        await resourceNotificationService.WaitForResourceAsync("load-tester-k6", KnownResourceStates.NotStarted).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
    
    [Fact]
    public async Task LoadTest_Should_Create_ExampleScriptResource()
    {
        // Arrange
        var resourceNotificationService = app.Services!.GetRequiredService<ResourceNotificationService>();

        // Act
        await resourceNotificationService.WaitForResourceAsync("example-test", KnownResourceStates.NotStarted).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
    
    [Fact]
    public async Task LoadTest_Should_Start_InfluxResource()
    {
        // Arrange
        var resourceNotificationService = app.Services!.GetRequiredService<ResourceNotificationService>();

        // Act
        await resourceNotificationService.WaitForResourceAsync("influx", KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
    
    [Fact]
    public async Task LoadTest_Should_Start_DashboardResource()
    {
        // Arrange
        var resourceNotificationService = app.Services!.GetRequiredService<ResourceNotificationService>();

        // Act
        await resourceNotificationService.WaitForResourceAsync("dashboard", KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(true);
    }
}
