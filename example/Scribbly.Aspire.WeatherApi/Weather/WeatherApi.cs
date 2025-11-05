using Scribbly.Stencil;

namespace Scribbly.Aspire.Web.Weather;

[EndpointGroup("/weatherforecast", "Weather Data")]
public partial class WeatherApi
{
    record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
    {
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }
   
    private static readonly string[] Summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

    [GetEndpoint("/", "GetWeatherForecast", "Gets the weather")]
    private static IEnumerable<WeatherForecast> GetForecast() =>
        Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    Summaries[Random.Shared.Next(Summaries.Length)]
                ));
}