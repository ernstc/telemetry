using System;
using System.Collections.Generic;
using System.Web;
using System.Web.Routing;
using Common;
using Microsoft.AspNet.FriendlyUrls;

namespace EasySampleAspNet462
{
    public static class RouteConfig
    {
        private static Type T = typeof(RouteConfig);

        public static void RegisterRoutes(RouteCollection routes)
        {
            using (var sec = TraceManager.GetCodeSection(T))
            {
                var settings = new FriendlyUrlSettings();
                settings.AutoRedirectMode = RedirectMode.Permanent;
                routes.EnableFriendlyUrls(settings);
            }
        }
    }
}
