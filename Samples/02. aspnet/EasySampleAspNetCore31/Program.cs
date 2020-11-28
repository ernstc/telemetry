using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasySampleAspNetCore31
{
    public class Program
    {
        static Type T = typeof(Program);

        public static void Main(string[] args)
        {
            //TraceManager.Init(System.Diagnostics.SourceLevels.All, null);
            using (var sec = TraceManager.GetCodeSection(T, new { args }))
            {
                CreateHostBuilder(args).Build().Run();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            using (var sec = TraceManager.GetCodeSection(T, new { args }))
            {
                var builder = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
                return builder;
            }
        }
    }
}
