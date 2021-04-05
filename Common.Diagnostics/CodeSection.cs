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
#endregion

namespace Common
{
    public class CodeSection : IDisposable
    {
        #region internal state
        static Stopwatch _stopwatch = TraceManager.Stopwatch;
        public bool _isLogEnabled = true;
        public bool? _showNestedFlow = null;
        public int? _maxMessageLevel = null;
        public int? _maxMessageLen = null;
        public int? _maxMessageLenError = null;
        public int? _maxMessageLenWarning = null;
        public int? _maxMessageLenInfo = null;
        public int? _maxMessageLenVerbose = null;
        public int? _maxMessageLenDebug = null;
        public CodeSection _caller = null;

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
        public Assembly Assembly { get; set; }
        public TraceSource TraceSource;
        public TraceEventType TraceEventType;
        public IModuleContext ModuleContext { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public long CallStartMilliseconds { get; set; }
        public long CallStartTicks { get; set; }
        public DateTime SystemStartTime { get; set; }
        public string OperationID { get; set; }
        public bool IsInnerScope { get; set; }
        public CodeSection InnerScopeSection { get; set; }
        #endregion

        #region .ctor
        static CodeSection() { }
        public CodeSection(CodeSection pCopy)
        {
            this.Name = pCopy.Name;
            this.Payload = pCopy.Payload;
            this.TraceSource = pCopy.TraceSource;
            this.SourceLevel = pCopy.SourceLevel;
            this.MemberName = pCopy.MemberName;
            this.SourceFilePath = pCopy.SourceFilePath;
            this.SourceLineNumber = pCopy.SourceLineNumber;
            this.DisableStartEndTraces = true;
            this.T = pCopy.T;
            this.Assembly = pCopy.Assembly;
            this.Category = pCopy.Category;
            this.Source = pCopy.Source;
            this.CallStartMilliseconds = pCopy.CallStartMilliseconds;

            _caller = TraceManager.CurrentCodeSection.Value;

            this.NestingLevel = pCopy.NestingLevel;
            this.OperationID = pCopy.OperationID;
            this.OperationDept = pCopy.OperationDept;

            this.ModuleContext = pCopy.ModuleContext;
        }
        public CodeSection(object pthis, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose,
                           string category = null, IDictionary<string, object> properties = null, string source = null, long startTicks = 0, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
                           : this(pthis.GetType(), name, payload, traceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber)
        { }

        public CodeSection(Type type, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose,
                           string category = null, IDictionary<string, object> properties = null, string source = null, long startTicks = 0, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0, bool disableStartEndTraces = false)
        {
            this.Name = name;
            this.Payload = payload;
            this.TraceSource = traceSource;
            this.SourceLevel = sourceLevel;
            this.MemberName = memberName;
            this.SourceFilePath = sourceFilePath;
            this.SourceLineNumber = sourceLineNumber;
            this.DisableStartEndTraces = disableStartEndTraces;
            this.T = type;
            this.Assembly = type?.Assembly;
            this.Category = category;
            if (string.IsNullOrEmpty(source)) { source = this.Assembly?.GetName()?.Name; }

            this.Properties = properties;
            this.Source = source;
            this.CallStartMilliseconds = _stopwatch.ElapsedMilliseconds;
            this.CallStartTicks = startTicks;

            var caller = TraceManager.CurrentCodeSection.Value;
            while (caller != null && caller._disposed) { caller = caller._caller; }
            _caller = caller;

            if (disableStartEndTraces == false) { TraceManager.CurrentCodeSection.Value = this; }

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
            this.ModuleContext = this.Assembly != null ? TraceManager.GetModuleContext(this.Assembly) : null;

            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Start) || this.DisableStartEndTraces == true) { return; }

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                // traceSource.TraceData()
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                // Trace.WriteLine()
                if (Trace.Listeners != null && Trace.Listeners.Count > 0)
                {
                    foreach (TraceListener listener in Trace.Listeners)
                    {
                        try
                        {
                            listener.WriteLine(entry);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }
        #endregion

        public void Debug(object obj, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            var message = obj.GetLogString();

            var entry = new TraceEntry() { Message = message, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }
        public void Debug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }
        public void Debug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            try
            {
                var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                if (!TraceManager._lockListenersNotifications.Value)
                {
                    if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                    if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                }
                else
                {
                    TraceManager._pendingEntries.Enqueue(entry);
                    if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
                }
            }
            catch (Exception) { }
        }

        public void Information(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Information)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }
        public void Information(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Information)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }

        public void Warning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Warning)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }

        }
        public void Warning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Warning)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }

        public void Error(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Error)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }
        public void Error(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Error)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }

        public void Exception(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = true)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (exception == null) return;
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Critical)) { return; }

            var entry = new TraceEntry()
            {
                TraceEventType = TraceEventType.Critical,
                SourceLevel = SourceLevels.Critical,
                TraceSource = this.TraceSource,
                Message = $"Exception: {exception.Message} (InnerException: {exception?.InnerException?.Message ?? "null"})\nStackTrace: {exception.StackTrace}",
                Properties = properties,
                Source = source ?? this.Source,
                Category = category,
                CodeSection = this,
                Thread = Thread.CurrentThread,
                ThreadID = Thread.CurrentThread.ManagedThreadId,
                ApartmentState = Thread.CurrentThread.GetApartmentState(),
                DisableCRLFReplace = disableCRLFReplace,
                ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds,
                TraceStartTicks = startTicks
            };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else
            {
                TraceManager._pendingEntries.Enqueue(entry);
                if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
            }
        }

        bool _disposed = false;
        public void Dispose()
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            if (_disposed) { return; }
            _disposed = true;

            try
            {
                if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Stop) || this.DisableStartEndTraces == true) { return; }
                if (!TraceManager._lockListenersNotifications.Value)
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                    if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                }
                else
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds, TraceStartTicks = startTicks };
                    TraceManager._pendingEntries.Enqueue(entry);
                    if (TraceManager._isInitializeComplete.Value == false && TraceManager._isInitializing.Value == false) { TraceManager.Init(SourceLevels.All, null); }
                }

            }
            finally { TraceManager.CurrentCodeSection.Value = _caller; }
        }

        public CodeSection GetInnerCodeSection()
        {
            if (InnerScopeSection == null) { InnerScopeSection = this.Clone(); InnerScopeSection.IsInnerScope = true; }
            return InnerScopeSection;
        }
        public CodeSection Clone() { return new CodeSection(this); }

        #region getOperationInfo
        public static (string, int) getOperationInfo()
        {
            string operationID = null;
            try
            {
                var operationContext = TraceManager.OperationContext.Value;
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
