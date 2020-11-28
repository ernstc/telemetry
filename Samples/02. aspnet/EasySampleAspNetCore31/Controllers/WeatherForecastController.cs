using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EasySampleAspNetCore31.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            using (var sec = this.GetCodeSection(new { logger }))
            {
                _logger = logger;
            }
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            using (var sec = this.GetCodeSection())
            {
                //Trace.WriteLine("test");

                var rng = new Random();
                var ret = Enumerable.Range(1, 5).Select(index => new WeatherForecast
                {
                    Date = DateTime.Now.AddDays(index),
                    TemperatureC = rng.Next(-20, 55),
                    Summary = Summaries[rng.Next(Summaries.Length)]
                })
                .ToArray();

                sec.Result = ret;
                return ret;
            }
        }
    }
}
