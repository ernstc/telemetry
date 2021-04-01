using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;

namespace EasySampleBlazorAppv2.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = CreateHostBuilder(args)
                          .ConfigureLogging((context, loggingBuilder) =>
                          {
                              loggingBuilder.ClearProviders();

                              var options = new Log4NetProviderOptions();
                              options.Log4NetConfigFileName = "log4net.config";
                              var log4NetProvider = new Log4NetProvider(options);
                              loggingBuilder.AddDiginsightFormatted(log4NetProvider, context.Configuration);

                              TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration("6600ae1e-1466-4ad4-aea7-c017a8ab5dce");
                              ApplicationInsightsLoggerOptions appinsightOptions = new ApplicationInsightsLoggerOptions();
                              var tco = Options.Create<TelemetryConfiguration>(telemetryConfiguration);
                              var aio = Options.Create<ApplicationInsightsLoggerOptions>(appinsightOptions);
                              loggingBuilder.AddDiginsightJson(new ApplicationInsightsLoggerProvider(tco, aio), context.Configuration);

                              loggingBuilder.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Trace);

                          });


            var host = builder.Build();

            host.InitTraceLogger();

            var logger = host.GetLogger<Program>();
            using (var scope = logger.BeginMethodScope())
            {
                host.Run();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
