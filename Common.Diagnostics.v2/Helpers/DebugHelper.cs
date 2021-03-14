using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    internal class DebugHelper
    {
        #region internal state
        private static bool _isDebugBuild = false;
        //private static ConcurrentDictionary<string, object> _dicOverrides = new ConcurrentDictionary<string, object>();
        #endregion

        #region properties
        public static bool IsDebugBuild { get => _isDebugBuild; set => _isDebugBuild = value; }
        public static bool IsReleaseBuild { get => !_isDebugBuild; set => _isDebugBuild = !value; }
        #endregion

        #region .ctor
        static DebugHelper()
        {
#if DEBUG
            IsDebugBuild = true;
#endif
        }
        #endregion

        public static void IfDebug(Action action)
        {
            if (!_isDebugBuild) { return; }
            action();
        }
        public static void IfRelease(Action action)
        {
            if (_isDebugBuild) { return; }
            action();
        }

    }
}
