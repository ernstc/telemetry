using Common;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace EasySampleBlazorv2.Client.Pages
{
    public partial class FetchData : ComponentBase
    {
        [Inject]
        protected ILogger<Counter> _logger { get; set; }

        private WeatherForecast[] forecasts;

        protected override async Task OnInitializedAsync()
        {
            using (var scope = _logger.BeginMethodScope())
            {
                scope.LogDebug($"Http.BaseAddress: {Http.BaseAddress}");
                //forecasts = await Http.GetFromJsonAsync<WeatherForecast[]>("sample-data/weather.json");
                forecasts = await Http.GetFromJsonAsync<WeatherForecast[]>("WeatherForecast");
                scope.LogDebug(new { forecasts });
            }
        }

        public class WeatherForecast
        {
            public DateTime Date { get; set; }

            public int TemperatureC { get; set; }

            public string Summary { get; set; }

            public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
        }
    }
}
