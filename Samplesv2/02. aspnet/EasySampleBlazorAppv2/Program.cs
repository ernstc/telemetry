using Common;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Services;

namespace EasySampleBlazorAppv2
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            var serviceProvider = builder.Services.BuildServiceProvider();

            var consoleProvider = serviceProvider.GetRequiredService<ILoggerProvider>();
            Console.WriteLine($"loggerProvider: '{consoleProvider}'");

            var traceLoggerProvider = new TraceLoggerFormatProvider(builder.Configuration) { ConfigurationSuffix = "Console" };
            traceLoggerProvider.AddProvider(consoleProvider);

            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(traceLoggerProvider);
            // i.e. builder.Services.AddSingleton(traceLoggerProvider);

            serviceProvider = builder.Services.BuildServiceProvider();
            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            Console.WriteLine($"loggerFactory: '{loggerFactory}'");

            var logger = loggerFactory.CreateLogger<Program>();
            Console.WriteLine($"logger: '{logger}'");

            using (var scope = logger.BeginMethodScope())
            {
                builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

                await builder.Build().RunAsync();
            }

        }
    }
}
