#region using
using Common;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
//using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
#endregion

namespace EasySample
{
    /// <summary>Interaction logic for App.xaml</summary>
    public partial class App : Application
    {
        static Type T = typeof(App);
        const string CONFIGVALUE_APPINSIGHTSKEY = "AppInsightsKey", DEFAULTVALUE_APPINSIGHTSKEY = "";

        public static IHost Host;
        private ILogger<App> _logger;

        static App()
        {
            using (var scope = Host.BeginMethodScope(T))
            {
                try
                {
                    // sec.Debug("this is a debug trace");
                    // sec.Information("this is a Information trace");
                    // sec.Warning("this is a Warning trace");
                    // sec.Error("this is a error trace");

                    throw new InvalidOperationException("this is an exception");
                }
                catch (Exception ex)
                {
                    //sec.Exception(ex);
                }
            }
        }

        public App()
        {
            using (var scope = Host.BeginMethodScope(T))
            {
            }
        }
        protected override async void OnStartup(StartupEventArgs e)
        {
            var configuration = TraceLogger.GetConfiguration();
            ConfigurationHelper.Init(configuration);

            var appInsightKey = ConfigurationHelper.GetClassSetting<App, string>(CONFIGVALUE_APPINSIGHTSKEY, DEFAULTVALUE_APPINSIGHTSKEY); // , CultureInfo.InvariantCulture

            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration(builder =>
                    {
                        builder.Sources.Clear();
                        builder.AddConfiguration(configuration);
                    }).ConfigureServices((context, services) =>
                    {
                        ConfigureServices(context.Configuration, services);
                    })
                    .ConfigureLogging((context, loggingBuilder) =>
                    {
                        loggingBuilder.ClearProviders();

                        var options = new Log4NetProviderOptions();
                        options.Log4NetConfigFileName = "log4net.config";
                        var log4NetProvider = new Log4NetProvider(options);
                        loggingBuilder.AddDiginsightFormatted(log4NetProvider, configuration);

                        TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration(appInsightKey);
                        ApplicationInsightsLoggerOptions appinsightOptions = new ApplicationInsightsLoggerOptions();
                        var tco = Options.Create<TelemetryConfiguration>(telemetryConfiguration);
                        var aio = Options.Create<ApplicationInsightsLoggerOptions>(appinsightOptions);
                        loggingBuilder.AddDiginsightJson(new ApplicationInsightsLoggerProvider(tco, aio), configuration);

                        loggingBuilder.AddFilter<ApplicationInsightsLoggerProvider>("", LogLevel.Trace);
                    }).Build();

            Host.InitTraceLogger();

            var logger = Host.GetLogger<App>();
            using (var scope = logger.BeginMethodScope())
            {
                // LogStringExtensions.RegisterLogstringProvider(this);
            }
            await Host.StartAsync();

            var mainWindow = Host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }
        private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            services.AddSingleton<MainWindow>();

            // var descriptor = new ServiceDescriptor(typeof(ILogger), typeof(TraceLogger), ServiceLifetime.Singleton);
            // services.Replace(descriptor);

        }
        protected override async void OnExit(ExitEventArgs e)
        {
            using (Host)
            {
                await Host.StopAsync(TimeSpan.FromSeconds(5));
            }

            base.OnExit(e);
        }

        private string GetMethodName([CallerMemberName] string memberName = "") { return memberName; }
    }
}
