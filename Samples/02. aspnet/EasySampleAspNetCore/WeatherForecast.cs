using Common;
using System;

namespace EasySampleAspNetCore
{
    public class WeatherForecast: ISupportLogString
    {
        public DateTime Date { get; set; }

        public int TemperatureC { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public string Summary { get; set; }

        public string ToLogString()
        {
            return $"{{WeatherForecast:{{Date:{this.Date},TemperatureC:{this.TemperatureC},Summary:{this.Summary}}}}}";
        }
    }
}
