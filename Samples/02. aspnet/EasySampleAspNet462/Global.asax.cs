using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;
using System.Web.SessionState;

namespace EasySampleAspNet462
{
    public class Global : HttpApplication
    {
        static Type T = typeof(Global);

        void Application_Start(object sender, EventArgs e)
        {
            //TraceManager.Init(System.Diagnostics.SourceLevels.All, null);
            using (var sec = TraceManager.GetCodeSection(T))
            {
                //Code that runs on application startup
                RouteConfig.RegisterRoutes(RouteTable.Routes);
                BundleConfig.RegisterBundles(BundleTable.Bundles);
            }
        }
    }
}