namespace Scribbly.Aspire.K6;

public sealed class K6ResourceOptions
{
    public bool UseGrafanaDashboard { get; set; } = true;

    public string DashboardContainerName { get; set; } = "dashboard";
    public string DatabaseContainerName { get; set; } = "influx";
    
    internal static string? ScriptToRun { get; set; }
}