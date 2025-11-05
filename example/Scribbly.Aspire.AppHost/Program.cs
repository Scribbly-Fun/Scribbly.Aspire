
using Scribbly.Aspire;
using Scribbly.Aspire.K6;

var builder = DistributedApplication.CreateBuilder(args);

var weatherApi = builder.AddProject<Projects.Scribbly_Aspire_WeatherApi>("weather-api");
var cookbookApi = builder.AddProject<Projects.Scribbly_Aspire_CookbookApi>("cookbook-api");

builder.AddProject<Projects.Scribbly_Aspire_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(weatherApi)
    .WaitFor(weatherApi);

if (!builder.ExecutionContext.IsPublishMode)
{
    builder
        .AddLoadTesting("load-tester", "./scripts", options =>
        {
            options
                .WithBuiltInDashboard()
                .WithGrafanaDashboard()
                .WithOtlpEnvironment();
        })
        .WithDefaultApiResourceForScripts(cookbookApi)
        .WithApiResourceForScript("weather-test", weatherApi)
        .WithApiResourceForScript("rate-limited", weatherApi);
}

builder.Build().Run();
