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

namespace EasySampleBlazorApp.Client
{
    public class Program
    {
        public static Type T = typeof(Program);

        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            // Initializes Diginsight
            TraceManager.Init(System.Diagnostics.SourceLevels.All, builder.Configuration);

            using (var sec = TraceManager.GetCodeSection(T))
            {
                var buildServices = builder.Build(); sec.Debug("buildServices = builder.Build(); completed");
                IServiceProvider serviceProvider = buildServices.Services;

                sec.Debug("... await buildServices.RunAsync();");
                await buildServices.RunAsync();
            }

        }
    }
}
