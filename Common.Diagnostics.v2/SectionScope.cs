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
    public class SectionScope : IDisposable
    {
        #region internal state
        static Stopwatch _stopwatch = TraceLogger.Stopwatch;
        public bool _isLogEnabled = true;
        public bool? _showNestedFlow = null;
        public int? _maxMessageLevel = null;
        public int? _maxMessageLen = null;
        public int? _maxMessageLenError = null;
        public int? _maxMessageLenWarning = null;
        public int? _maxMessageLenInfo = null;
        public int? _maxMessageLenVerbose = null;
        public int? _maxMessageLenDebug = null;
        public SectionScope _caller = null;
        public ILogger _logger = null;

        public int NestingLevel { get; set; }
        public int OperationDept { get; set; }
        public object Payload { get; set; }
        //public object Exception { get; set; }
        public object Result { get; set; }
        public string Name { get; set; }
        public string MemberName { get; set; }
        public string SourceFilePath { get; set; }
        public int SourceLineNumber { get; set; }
        public bool DisableStartEndTraces { get; set; }
        public Type T { get; set; }
        public string TypeName { get; set; }
        public string ClassName { get; set; }
        public Assembly Assembly { get; set; }
        public TraceSource TraceSource;
        public TraceEventType TraceEventType;
        public IModuleContext ModuleContext { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public LogLevel LogLevel { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public long CallStartMilliseconds { get; set; }
        public long CallStartTicks { get; set; }
        public DateTime SystemStartTime { get; set; }
        public string OperationID { get; set; }
        public bool IsInnerScope { get; set; }
        public SectionScope InnerScope { get; set; }
        public static AsyncLocal<SectionScope> Current { get; set; } = new AsyncLocal<SectionScope>();
        public static AsyncLocal<IOperationContext> OperationContext { get; set; } = new AsyncLocal<IOperationContext>();
        #endregion

        #region .ctor
        static SectionScope() { }
        public SectionScope(SectionScope pCopy)
        {
            this.Name = pCopy.Name;
            this.Payload = pCopy.Payload;
            this.TraceSource = pCopy.TraceSource;
            this.SourceLevel = pCopy.SourceLevel;
            this.LogLevel = pCopy.LogLevel;
            this.MemberName = pCopy.MemberName;
            this.SourceFilePath = pCopy.SourceFilePath;
            this.SourceLineNumber = pCopy.SourceLineNumber;
            this.DisableStartEndTraces = true;
            this.T = pCopy.T;
            this.Assembly = pCopy.Assembly;
            this.Category = pCopy.Category;
            this.Source = pCopy.Source;
            this.CallStartMilliseconds = pCopy.CallStartMilliseconds;

            _caller = SectionScope.Current.Value;

            this.NestingLevel = pCopy.NestingLevel;
            this.OperationID = pCopy.OperationID;
            this.OperationDept = pCopy.OperationDept;

            this.ModuleContext = pCopy.ModuleContext;
        }

        public SectionScope(ILogger logger, Type type, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel logLevel = LogLevel.Trace,
                           string category = null, IDictionary<string, object> properties = null, string source = null, long startTicks = 0, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0, bool disableStartEndTraces = false)
        {
            this.Name = name;
            this.Payload = payload;
            this.TraceSource = traceSource;
            this.SourceLevel = sourceLevel;
            this.LogLevel = logLevel;
            this.MemberName = memberName;
            this.SourceFilePath = sourceFilePath;
            this.SourceLineNumber = sourceLineNumber;
            this.DisableStartEndTraces = disableStartEndTraces;
            _logger = logger;

            if (type == null && logger != null) { type = logger.GetType().GenericTypeArguments.FirstOrDefaultChecked(); }

            this.T = type;
            this.Assembly = type?.Assembly;
            this.Category = category;
            if (string.IsNullOrEmpty(source)) { source = this.Assembly?.GetName()?.Name; }

            this.Properties = properties;
            this.Source = source;
            this.CallStartMilliseconds = _stopwatch.ElapsedMilliseconds;
            this.CallStartTicks = startTicks;

            var caller = SectionScope.Current.Value;
            while (caller != null && caller._disposed) { caller = caller._caller; }
            _caller = caller;

            if (disableStartEndTraces == false) { SectionScope.Current.Value = this; }

            if (_caller != null)
            {
                if (disableStartEndTraces == false) { NestingLevel = _caller.NestingLevel + 1; }
                OperationID = _caller.OperationID;
                OperationDept = _caller.OperationDept;
                if (string.IsNullOrEmpty(OperationID)) { (this.OperationID, this.OperationDept) = getOperationInfo(); }
            }
            else
            {
                (string operationID, int operationDept) = getOperationInfo();
                NestingLevel = 0;
                OperationID = operationID;
                OperationDept = operationDept;
            }

            if (this.DisableStartEndTraces == true) { return; }

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(logLevel, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        internal SectionScope(ILogger logger, string typeName, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel logLevel = LogLevel.Trace,
                              string category = null, IDictionary<string, object> properties = null, string source = null, long startTicks = 0, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0, bool disableStartEndTraces = false)
        {
            this.Name = name;
            this.Payload = payload;
            this.TraceSource = traceSource;
            this.SourceLevel = sourceLevel;
            this.LogLevel = logLevel;
            this.MemberName = memberName;
            this.SourceFilePath = sourceFilePath;
            this.SourceLineNumber = sourceLineNumber;
            this.DisableStartEndTraces = disableStartEndTraces;
            _logger = logger;

            var type = logger?.GetType()?.GenericTypeArguments?.FirstOrDefault();
            this.T = type;
            this.TypeName = typeName;

            if (!string.IsNullOrEmpty(typeName))
            {
                var classNameIndex = typeName.LastIndexOf('.') + 1;
                var className = classNameIndex >= 0 ? typeName.Substring(classNameIndex) : typeName;
                this.ClassName = className;
            }
            else { this.ClassName = "Unknown"; }

            this.Assembly = type?.Assembly;
            this.Category = category;
            if (string.IsNullOrEmpty(source)) { source = this.Assembly?.GetName()?.Name; }

            this.Properties = properties;
            this.Source = source;
            this.CallStartMilliseconds = _stopwatch.ElapsedMilliseconds;
            this.CallStartTicks = startTicks;

            var caller = SectionScope.Current.Value;
            while (caller != null && caller._disposed) { caller = caller._caller; }
            _caller = caller;

            if (disableStartEndTraces == false) { SectionScope.Current.Value = this; }

            if (_caller != null)
            {
                if (disableStartEndTraces == false) { NestingLevel = _caller.NestingLevel + 1; }
                OperationID = _caller.OperationID;
                OperationDept = _caller.OperationDept;
                if (string.IsNullOrEmpty(OperationID)) { (this.OperationID, this.OperationDept) = getOperationInfo(); }
            }
            else
            {
                (string operationID, int operationDept) = getOperationInfo();
                NestingLevel = 0;
                OperationID = operationID;
                OperationDept = operationDept;
            }
            //this.ModuleContext = this.Assembly != null ? LoggerFormatter.GetModuleContext(this.Assembly) : null;

            if (this.DisableStartEndTraces == true) { return; }

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(logLevel, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        #endregion

        public void LogDebug(object obj, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var message = obj.GetLogString();

            var entry = new TraceEntry() { Message = message, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Debug, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }
        public void LogDebug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Debug, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }
        public void LogDebug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            try
            {
                var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
                {
                    _logger.Log<TraceEntry>(LogLevel.Debug, default(EventId), entry, null, (e, ex) => e.ToString());
                }
                else
                {
                    TraceLogger._pendingEntries.Enqueue(entry);
                    if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
                }
            }
            catch (Exception) { }
        }

        public void LogInformation(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Information, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }
        public void LogInformation(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Information, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        public void LogWarning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Warning, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }

        }
        public void LogWarning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Warning, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        public void LogError(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Error, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }
        public void LogError(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Error, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        public void LogException(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = true)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            if (exception == null) return;

            var entry = new TraceEntry()
            {
                TraceEventType = TraceEventType.Critical,
                SourceLevel = SourceLevels.Critical,
                TraceSource = this.TraceSource,
                Message = $"Exception: {exception.Message} (InnerException: {exception?.InnerException?.Message ?? "null"})\nStackTrace: {exception.StackTrace}",
                Properties = properties,
                Source = source ?? this.Source,
                Category = category,
                SectionScope = this,
                Thread = Thread.CurrentThread,
                ThreadID = Thread.CurrentThread.ManagedThreadId,
                ApartmentState = Thread.CurrentThread.GetApartmentState(),
                DisableCRLFReplace = disableCRLFReplace,
                ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds,
                TraceStartTicks = startTicks
            };
            if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
            {
                _logger.Log<TraceEntry>(LogLevel.Error, default(EventId), entry, null, (e, ex) => e.ToString());
            }
            else
            {
                TraceLogger._pendingEntries.Enqueue(entry);
                if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
            }
        }

        bool _disposed = false;
        public void Dispose()
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            if (_disposed) { return; }
            _disposed = true;

            try
            {
                if (this.DisableStartEndTraces == true) { return; }
                if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    _logger.Log<TraceEntry>(this.LogLevel, default(EventId), entry, null, (e, ex) => e.ToString());
                }
                else
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, SectionScope = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    TraceLogger._pendingEntries.Enqueue(entry);
                    if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
                }

            }
            finally { SectionScope.Current.Value = _caller; }
        }

        public SectionScope GetInnerScope()
        {
            if (InnerScope == null) { InnerScope = this.Clone(); InnerScope.IsInnerScope = true; }
            return InnerScope;
        }
        public SectionScope Clone() { return new SectionScope(this); }

        public string getClassName() { return this.T != null ? this.T?.Name : this.ClassName; }
        #region getOperationInfo
        public static (string, int) getOperationInfo()
        {
            string operationID = null;
            try
            {
                var operationContext = SectionScope.OperationContext.Value;
                //var operationContext = CallContext.LogicalGetData("OperationContext") as IOperationContext;
                if (operationContext != null && !string.IsNullOrEmpty(operationContext.RequestContext?.RequestId))
                {
                    return (operationContext?.RequestContext?.RequestId, operationContext?.RequestContext != null ? operationContext.RequestContext.RequestDept : 0);
                }
            }
            catch (Exception ex) { operationID = ex.Message; }
            return (operationID, 0);
        }
        #endregion
        #region Min
        int Min(int a, int b) { return a < b ? a : b; }
        #endregion

        private Type GetType<T>(T t) { return typeof(T); }
    }
    public class CodeSectionSurrogate
    {
        public int NestingLevel { get; set; }
        public int OperationDept { get; set; }
        public object Payload { get; set; }
        //public object Exception { get; set; }
        public object Result { get; set; }
        public string Name { get; set; }
        public string MemberName { get; set; }
        public string SourceFilePath { get; set; }
        public int SourceLineNumber { get; set; }
        public bool DisableStartEndTraces { get; set; }
        //public Type T { get; set; }
        public string TypeName { get; set; }
        public string TypeFullName { get; set; }
        //public Assembly Assembly { get; set; }
        public string AssemblyName { get; set; }
        public string AssemblyFullName { get; set; }
        //public TraceSource TraceSource;
        public string TraceSourceName;
        public TraceEventType TraceEventType;
        // public IModuleContext ModuleContext { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public long CallStartMilliseconds { get; set; }
        public DateTime SystemStartTime { get; set; }
        public string OperationID { get; set; }
        public bool IsInnerScope { get; set; }
    }

    public class CodeSectionInfo
    {
        public object Payload { get; set; }
        public string Name { get; set; }
        public string MemberName { get; set; }
        public string SourceFilePath { get; set; }
        public int SourceLineNumber { get; set; }
        public long CallStartMilliseconds { get; set; }
        public DateTimeOffset? CallStart { get; set; }
        public DateTimeOffset? CallEnd { get; set; }
        public int NestingLevel { get; set; }
        public Type T { get; set; }
    }
    public class ProcessInfo
    {
        public string ProcessID { get; set; }
        public string ProcessName { get; set; }
        public Assembly Assembly { get; set; }
        public Process Process { get; set; }
        public Thread Thread { get; set; }
        public int ThreadID { get; set; }
    }
    public class SystemInfo
    {
        public string Server { get; set; }
    }
    public interface IRequestContext
    {
        string Method { get; set; }
        string Path { get; set; }
        string QueryString { get; set; }
        string ContentType { get; set; }
        long? ContentLength { get; set; }
        string Protocol { get; set; }
        string PathBase { get; set; }
        string Host { get; set; }
        bool IsHttps { get; set; }
        string Scheme { get; set; }
        bool HasFormContentType { get; set; }
        IList<KeyValuePair<string, string>> Headers { get; set; }
        string TypeName { get; set; }
        string AssemblyName { get; set; }
        string Layer { get; set; }
        string Area { get; set; }
        string Controller { get; set; }
        string Action { get; set; }
        string RequestId { get; set; }
        int RequestDept { get; set; }
        string ServiceName { get; set; }
        string OperationName { get; set; }
        string RequestDescription { get; set; }
        DateTimeOffset RequestStart { get; set; }
        DateTimeOffset? RequestEnd { get; set; }
        object Input { get; set; }
        object Output { get; set; }
        string ProfileServiceURL { get; set; }
    }
    public interface IBusinessContext
    {
        string Branch { get; set; }
    }
    public interface IUserContext
    {
        bool? IsAuthenticated { get; set; }
        string AuthenticationType { get; set; }
        string ImpersonationLevel { get; set; }
        bool? IsAnonymous { get; set; }
        bool? IsGuest { get; set; }
        bool? IsSystem { get; set; }
        IIdentity Identity { get; set; }
    }
    public interface ISessionContext
    {
        string SessionId { get; set; }
        bool? SessionIsAvailable { get; set; }
    }
    public interface ISystemContext
    {
        string ConnectionId { get; set; }
        string ConnectionLocalIpAddress { get; set; }
        int? ConnectionLocalPort { get; set; }
        string ConnectionRemoteIpAddress { get; set; }
        int? ConnectionRemotePort { get; set; }
        string Server { get; set; }
    }
    public interface IOperationContext
    {
        //// REQUEST
        IRequestContext RequestContext { get; set; }
        // USER
        IUserContext UserContext { get; set; }
        // SESSION
        ISessionContext SessionContext { get; set; }
        // SYSTEM
        ISystemContext SystemContext { get; set; }
        // BUSINESS
        IBusinessContext BusinessContext { get; set; }
    }
    public interface IModuleContext
    {
        //bool? ShowNestedFlow { get; set; }
        int? MaxMessageLevel { get; set; }
        int? MaxMessageLen { get; set; }
        int? MaxMessageLenError { get; set; }
        int? MaxMessageLenWarning { get; set; }
        int? MaxMessageLenInfo { get; set; }
        int? MaxMessageLenVerbose { get; set; }
        int? MaxMessageLenDebug { get; set; }
        //DateTimeOffset? LogggingSettingsCreationDate { get; set; }

        Assembly Assembly { get; set; }
        ConcurrentDictionary<string, object> Properties { get; set; }
    }
    public class RequestContext : IRequestContext
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string QueryString { get; set; }
        public string ContentType { get; set; }
        public long? ContentLength { get; set; }
        public string Protocol { get; set; }
        public string PathBase { get; set; }
        public string Host { get; set; }
        public bool IsHttps { get; set; }
        public string Scheme { get; set; }
        public bool HasFormContentType { get; set; }
        public IList<KeyValuePair<string, string>> Headers { get; set; }
        public string TypeName { get; set; }
        public string AssemblyName { get; set; }
        public string Layer { get; set; }
        public string Area { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public string RequestId { get; set; }
        public int RequestDept { get; set; }
        public string ServiceName { get; set; }
        public string OperationName { get; set; }
        public string RequestDescription { get; set; }
        public DateTimeOffset RequestStart { get; set; }
        public DateTimeOffset? RequestEnd { get; set; }
        public object Input { get; set; }
        public object Output { get; set; }
        public string ProfileServiceURL { get; set; }
    }
    public class BusinessContext : IBusinessContext
    {
        public string Branch { get; set; }
    }
    public class ModuleContext : IModuleContext
    {
        #region .ctor
        public ModuleContext(Assembly assembly)
        {
            this.Assembly = assembly;
        }
        #endregion

        public bool? ShowNestedFlow { get; set; }
        public int? MaxMessageLevel { get; set; }
        public int? MaxMessageLen { get; set; }
        public int? MaxMessageLenError { get; set; }
        public int? MaxMessageLenWarning { get; set; }
        public int? MaxMessageLenInfo { get; set; }
        public int? MaxMessageLenVerbose { get; set; }
        public int? MaxMessageLenDebug { get; set; }
        public DateTimeOffset? LogggingSettingsCreationDate { get; set; }
        public Assembly Assembly { get; set; }
        public ConcurrentDictionary<string, object> Properties { get; set; } = new ConcurrentDictionary<string, object>();
        public void SetProperty(string name, object val)
        {
            this.Properties[name] = val;
            // map explicit properties
            //var cd = val as ChiaveDescrizione;
            //switch (name)
            //{
            //    case "Configuration.LoggingSettings:MaxMessageLevel":
            //        this.MaxMessageLevel = !string.IsNullOrEmpty(cd?.Valore) ? (int?)ConfigurationManagerCommon.GetValue(cd.Valore, Trace.CONFIGDEFAULT_MAXMESSAGELEVEL, null) : null;
            //        break;
            //};
        }
    }
    public sealed class NonFormattableString
    {
        public NonFormattableString(string arg)
        {
            Value = arg;
        }

        public string Value { get; }

        public static implicit operator NonFormattableString(string arg) { return new NonFormattableString(arg); }

        public static implicit operator NonFormattableString(FormattableString arg) { throw new InvalidOperationException(); }
    }
}
