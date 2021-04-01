using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace EasySampleBlazorAppv2.Pages
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
                forecasts = await Http.GetFromJsonAsync<WeatherForecast[]>("sample-data/weather.json");
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
