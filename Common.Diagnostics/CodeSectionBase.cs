#region using
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
//using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
#endregion

namespace Common
{
    public class CodeSectionBase : ICodeSection, IDisposable, ICloneable
    {
        #region internal state
        public static Stopwatch _stopwatch = TraceLogger.Stopwatch;
        #endregion

        public bool _isLogEnabled { get; set; } = true;
        public bool? _showNestedFlow { get; set; }
        public int? _maxMessageLevel { get; set; }
        public int? _maxMessageLen { get; set; }
        public int? _maxMessageLenError { get; set; }
        public int? _maxMessageLenWarning { get; set; }
        public int? _maxMessageLenInfo { get; set; }
        public int? _maxMessageLenVerbose { get; set; }
        public int? _maxMessageLenDebug { get; set; }

        public ICodeSection Caller { get; set; }
        public int NestingLevel { get; set; }
        public int OperationDept { get; set; }
        public object Payload { get; set; }
        // public object Exception { get; set; }
        public object Result { get; set; }
        public string Name { get; set; }
        public string MemberName { get; set; }
        public string SourceFilePath { get; set; }
        public int SourceLineNumber { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }

        public long CallStartMilliseconds { get; set; }
        public long CallStartTicks { get; set; }
        public DateTime SystemStartTime { get; set; }
        public string OperationID { get; set; }
        public bool IsDisposed { get; set; }
        public bool DisableStartEndTraces { get; set; }

        public Type T { get; set; }
        public string TypeName { get; set; }
        public string ClassName { get; set; }
        public Assembly Assembly { get; set; }

        public TraceSource TraceSource { get; set; }
        public TraceEventType TraceEventType { get; set; }
        public IModuleContext ModuleContext { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public LogLevel LogLevel { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public bool IsInnerScope { get; set; }
        public ICodeSection InnerScope { get; set; }

        public static AsyncLocal<ICodeSection> Current { get; set; } = new AsyncLocal<ICodeSection>();
        public static AsyncLocal<IOperationContext> OperationContext { get; set; } = new AsyncLocal<IOperationContext>();

        public ICodeSection GetInnerSection()
        {
            if (InnerScope == null) { InnerScope = this.Clone() as CodeSectionBase; InnerScope.IsInnerScope = true; }
            return InnerScope;
        }
        public virtual void Dispose()
        {
            if (IsDisposed) { return; }
            IsDisposed = true;
        }
        public virtual object Clone()
        {
            return null;
        }
    }
}
