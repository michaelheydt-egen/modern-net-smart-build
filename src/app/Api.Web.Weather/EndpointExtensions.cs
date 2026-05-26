using Model.Weather;
using System.Diagnostics.Metrics;

public static class EndpointExtensions
{
    public static void MapWeatherEndpoints(this WebApplication app, WebApplicationBuilder builder)
    {
        // Meter name matches the ApplicationName registered in ServiceDefaults so the
        // OpenTelemetry pipeline picks these instruments up automatically.
        var meter = new Meter(builder.Environment.ApplicationName);
        var forecastsRequested = meter.CreateCounter<long>(
            "practical.weather.forecasts_requested",
            unit: "{forecast}",
            description: "Number of weather forecasts returned to callers.");

        app.MapGet("/api/weather/forecast", () =>
        {
            var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    Summaries[Random.Shared.Next(Summaries.Length)]
                ))
                .ToArray();
            forecastsRequested.Add(forecast.Length);
            return forecast;
        })
        .WithName("GetWeatherForecast");
    }

    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
}