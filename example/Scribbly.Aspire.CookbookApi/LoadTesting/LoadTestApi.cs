using Scribbly.Stencil;

namespace Scribbly.Aspire.Web.LoadTesting;

[EndpointGroup("/load-test")]
[Configure]
public partial class LoadTestApi
{
    [GetEndpoint("/")]
    private static IResult GeRateLimitedEndpoint() => Results.Ok();

    /// <inheritdoc />
    public void Configure(RouteGroupBuilder loadTestApiBuilder)
    {
        loadTestApiBuilder.RequireRateLimiting("load-testing");
    }
}