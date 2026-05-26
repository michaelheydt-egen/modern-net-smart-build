using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Model.Weather;
using System.Net.Http.Json;

namespace Web.Shared.Components.Weather.Pages;

public partial class Weather
{
    [PersistentState(AllowUpdates = true)]
    public WeatherForecast[]? Forecasts { get; set; }

    [Inject]
    public IConfiguration? Configuration { get; set; } = default;

    protected override async Task OnInitializedAsync()
    {
        if (Forecasts is null)
        {
            var useStatic = Configuration!.GetValue<bool>("Weather:UseStaticData");
            if (useStatic)
            {
                Forecasts =
                [
                    new (DateOnly.FromDateTime(DateTime.Now), 25, "Sunny"),
                    new (DateOnly.FromDateTime(DateTime.Now.AddDays(1)), 28, "Cloudy" ),
                    new (DateOnly.FromDateTime(DateTime.Now.AddDays(2)), 22, "Rainy" )
                ];
            }
            else
            {
                var client = HttpClientFactory.CreateClient("api");
                Forecasts = await client.GetFromJsonAsync<WeatherForecast[]>("/api/weather/forecast");
            }
        }
    }
}
