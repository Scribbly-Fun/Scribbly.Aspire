using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;

namespace Scribbly.Aspire.Resources.LoadTesting;

public sealed class LoadTestingResourceFactory : IAsyncLifetime
{
    internal DistributedApplication? App;
    internal IServiceProvider? Services;
    
    /// <inheritdoc />
    [MemberNotNull(nameof(App))]
    [MemberNotNull(nameof(Services))]
    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Scribbly_Aspire_AppHost>();
    
        App = await appHost.BuildAsync();
        Services = App.Services;
        await App.StartAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await App!.DisposeAsync();
    }
}