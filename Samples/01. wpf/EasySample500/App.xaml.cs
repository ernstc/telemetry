#region using
using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows; 
#endregion

namespace EasySample500
{
    /// <summary>Interaction logic for App.xaml</summary>
    public partial class App : Application
    {
        static Type T = typeof(App);

        static App()
        {
            // builder....
            // void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
            //    var diginsightFactory = loggerFactory.AddDiginsight(app, env, Configuration);
            //                            diginsightFactory.AddApplicationInsights()
            //                            diginsightFactory.AddConsole()
            //                            diginsightFactory.AddEventLog()

            //TraceManager.Init(SourceLevels.All, null);
            using (var sec = TraceManager.GetCodeSection(T))
            {
                try
                {
                    sec.Debug("this is a debug trace");
                    sec.Information("this is a Information trace");
                    sec.Warning("this is a Warning trace");
                    sec.Error("this is a error trace");

                    throw new InvalidOperationException("this is an exception");
                }
                catch (Exception ex)
                {
                    sec.Exception(ex);
                }
            }
        }

        public App()
        {
            using (var sec = TraceManager.GetCodeSection(T))
            {
                //LogStringExtensions.RegisterLogstringProvider(this);
            }
        }
    }
}
