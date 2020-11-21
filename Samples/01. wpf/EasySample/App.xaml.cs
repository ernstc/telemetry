#region using
using Common;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
#endregion

namespace EasySample
{
    /// <summary>Interaction logic for App.xaml</summary>
    public partial class App : Application, IProvideLogString
    {
        static Type T = typeof(App);

        static App()
        {
            using (var sec = TraceManager.GetCodeSection(T))
            {
                // TraceManager.Init(SourceLevels.All, null);
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
                LogStringExtensions.RegisterLogstringProvider(this);
            }
        }

        public string ToLogString(object t, HandledEventArgs arg)
        {
            switch (t)
            {
                case Window w: arg.Handled = true; return ToLogStringInternal(w);
                case Button w: arg.Handled = true; return ToLogStringInternal(w);
                default:
                    break;
            }
            return null;
        }
        public static string ToLogStringInternal(Window pthis)
        {
            string logString = $"{{Window:{{Name:{pthis.Name},ActualHeight:{pthis.ActualHeight},ActualWidth:{pthis.ActualWidth},AllowsTransparency:{pthis.AllowsTransparency},Background:{pthis.Background},Width:{pthis.Width},Height:{pthis.Height}}}}}";
            return logString;
        }
        public static string ToLogStringInternal(Button pthis)
        {
            string logString = $"{{Button:{{Name:{pthis.Name},Content:{pthis.Content.GetLogString()}}}}}";
            return logString;
        }
    }
}
