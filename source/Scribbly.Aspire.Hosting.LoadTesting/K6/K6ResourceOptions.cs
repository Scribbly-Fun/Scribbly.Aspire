namespace Scribbly.Aspire.K6;

public sealed class K6ResourceOptions
{
    public bool UseGrafanaDashboard { get; set; } = true;
    public bool ExplicateStartDashboard { get; set; } = false;
    public string DashboardContainerName { get; set; } = "dashboard";
    public string DatabaseContainerName { get; set; } = "influx";
}