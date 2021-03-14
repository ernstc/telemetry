#region using
using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Metrics = System.Collections.Generic.Dictionary<string, object>; // $$$
#endregion

namespace EasySample
{
    //public class C : WeakEventManager { }
    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        private static ILogger<MainWindow> _logger;
        
        private string GetScope([CallerMemberName] string memberName = "") { return memberName; }

        public MainWindow()
        {
            //var host = (App.Current as App).Host;
            //var logger = host.Services.GetRequiredService<ILogger<MainWindow>>();
        }
        public MainWindow(ILogger<MainWindow> logger) // ILogger logger
        {
            _logger = logger;
            using (_logger.BeginScope(TraceLogger.GetMethodName()))
            {
                InitializeComponent();
            }
        }
        private void MainWindow_Initialized(object sender, EventArgs e)
        {
            using (_logger.BeginMethodScope())
            {
                _logger.LogDebug("this is a debug trace", "User"); // , properties: new Dictionary<string, object>() { { "", "" } }
                _logger.LogInformation("this is a debug trace", "User"); // , properties: new Dictionary<string, object>() { { "", "" } }
                _logger.LogInformation("this is a Information trace", "Raw");
                _logger.LogWarning("this is a Warning trace", "User.Report");
                _logger.LogError("this is a error trace", "Resource");
            }

            //using (_logger.BeginScope()) 
            //{
            //    _logger.Debug("this is a debug trace", "User", properties: new Dictionary<string, object>() { { "", "" } });
            //    _logger.Information("this is a Information trace", "Raw");
            //    _logger.Warning("this is a Warning trace", "User.Report");
            //    _logger.Error("this is a error trace", "Resource");
            //}
        }

        private void btnRun_Click(object sender, RoutedEventArgs e)
        {
            using (var sec = _logger.BeginMethodScope())
            {
                try
                {
                    // _logger.
                    _logger.LogDebug("this is a debug trace", "User", new Metrics() {
                        { "User", 123 },
                        { "Tags", new[] { "sample", "user", "advanced" } }
                    });

                    _logger.LogInformation("this is a Information trace", "event");
                    _logger.LogInformation("this is a Information trace", "Raw");
                    _logger.LogWarning("this is a Warning trace", "User.Report");
                    _logger.LogError("this is a error trace", "Resource");


                    throw new NullReferenceException();
                }
                catch (Exception ex)
                {
                    //sec.Exception(ex);
                }


                // report button 
                // var recorder = Trace.Listeners.OfType<TraceListener>().FirstOrDefault(l => l is RecorderTraceListener) as RecorderTraceListener;
                // var entries = recorder.GetItems();
            }
        }

        public void SampleMethod()
        {
            using (var sec = _logger.BeginMethodScope())
            {
                Thread.Sleep(100);
                SampleMethodNested();
                SampleMethodNested1();

            }
        }
        public void SampleMethodNested()
        {
            Thread.Sleep(100);
        }
        public void SampleMethodNested1()
        {
            Thread.Sleep(10);
        }
    }
}
