﻿#region using
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
    public class CodeSectionScope : CodeSectionBase, ICodeSection, IDisposable, ICloneable, ICodeSectionLogger
    {
        public ILogger _logger = null;

        #region .ctor
        static CodeSectionScope() { }
        public CodeSectionScope(CodeSectionScope pCopy)
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

            Caller = CodeSectionBase.Current.Value;

            this.NestingLevel = pCopy.NestingLevel;
            this.OperationID = pCopy.OperationID;
            this.OperationDept = pCopy.OperationDept;

            this.IsDisposed = pCopy.IsDisposed;
            this.Caller = pCopy.Caller;
            this.ModuleContext = pCopy.ModuleContext;
        }

        public CodeSectionScope(ILogger logger, Type type, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel logLevel = LogLevel.Trace,
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

            var caller = CodeSectionBase.Current.Value;
            while (caller != null && caller.IsDisposed) { caller = caller.Caller; } // _disposed
            Caller = caller;

            if (disableStartEndTraces == false) { CodeSectionBase.Current.Value = this; } // disableStartEndTraces

            if (Caller != null)
            {
                if (disableStartEndTraces == false) { NestingLevel = Caller.NestingLevel + 1; } // NestingLevel
                OperationID = Caller.OperationID; // OperationID
                OperationDept = Caller.OperationDept; // OperationDept
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

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

        internal CodeSectionScope(ILogger logger, string typeName, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel logLevel = LogLevel.Trace,
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

            var caller = CodeSectionBase.Current.Value;
            while (caller != null && caller.IsDisposed) { caller = caller.Caller; }
            Caller = caller;

            if (disableStartEndTraces == false) { CodeSectionBase.Current.Value = this; }

            if (Caller != null)
            {
                if (disableStartEndTraces == false) { NestingLevel = Caller.NestingLevel + 1; }
                OperationID = Caller.OperationID;
                OperationDept = Caller.OperationDept;
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

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

        // () => { } - $"ffdf {variable}"  $"ffdf {0}", variable1
        public void LogDebug(object obj, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var message = obj.GetLogString();

            var entry = new TraceEntry() { Message = message, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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
                var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
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
                CodeSectionBase = this,
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

        public override void Dispose()
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            if (IsDisposed) { return; }

            base.Dispose();

            try
            {
                if (this.DisableStartEndTraces == true) { return; }
                if (!TraceLogger._lockListenersNotifications.Value && _logger != null)
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    _logger.Log<TraceEntry>(this.LogLevel, default(EventId), entry, null, (e, ex) => e.ToString());
                }
                else
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSectionBase = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    TraceLogger._pendingEntries.Enqueue(entry);
                    if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
                }

            }
            finally { CodeSectionBase.Current.Value = Caller; }
        }

        public override object Clone() { return new CodeSectionScope(this); }

        #region getOperationInfo
        public static (string, int) getOperationInfo()
        {
            string operationID = null;
            try
            {
                var operationContext = CodeSectionBase.OperationContext.Value;
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

        void ICodeSectionLogger.Debug(object obj, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogDebug(obj, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Debug(NonFormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogDebug(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Debug(FormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogDebug(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Information(NonFormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogInformation(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Information(FormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogInformation(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Warning(NonFormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogWarning(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Warning(FormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogWarning(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Error(NonFormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogError(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Error(FormattableString message, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace) { this.LogError(message, category, properties, source, disableCRLFReplace); }
        void ICodeSectionLogger.Exception(Exception exception, string category, IDictionary<string, object> properties, string source, bool disableCRLFReplace)  { this.LogException(exception, category, properties, source, disableCRLFReplace); }
    }
    public class SectionScopeSurrogate
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
    public class SectionScopeInfo
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
}
