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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;
using Newtonsoft.Json;
#endregion

namespace Common
{
    public static class TraceManager
    {
        #region const
        public const string CONFIGSETTING_MAXMESSAGELEVEL = "MaxMessageLevel"; public const int CONFIGDEFAULT_MAXMESSAGELEVEL = 3;
        public const string CONFIGSETTING_MAXMESSAGELEN = "MaxMessageLen"; public const int CONFIGDEFAULT_MAXMESSAGELEN = 1024;
        public const string CONFIGSETTING_MAXMESSAGELENINFO = "MaxMessageLenInfo"; public const int CONFIGDEFAULT_MAXMESSAGELENINFO = 1024;
        public const string CONFIGSETTING_MAXMESSAGELENWARNING = "MaxMessageLenWarning"; public const int CONFIGDEFAULT_MAXMESSAGELENWARNING = 1024;
        public const string CONFIGSETTING_MAXMESSAGELENERROR = "MaxMessageLenError"; public const int CONFIGDEFAULT_MAXMESSAGELENERROR = -1;
        public const string CONFIGSETTING_DEFAULTLISTENERTYPENAME = "Common.TraceListenerDefault,Common.Diagnostics";
        #endregion

        #region internal state
        private static Type T = typeof(TraceManager);
        private static readonly string _traceSourceName = "TraceSource";
        public static Func<string, string> CRLF2Space = (string s) => { return s?.Replace("\r", " ")?.Replace("\n", " "); };
        public static Func<string, string> CRLF2Encode = (string s) => { return s?.Replace("\r", "\\r")?.Replace("\n", "\\n"); };
        public static IConfiguration Configuration { get; private set; }
        public static ConcurrentDictionary<string, object> Properties { get; set; } = new ConcurrentDictionary<string, object>();
        public static ConcurrentDictionary<Assembly, IModuleContext> Modules { get; set; } = new ConcurrentDictionary<Assembly, IModuleContext>();
        public static TraceSource TraceSource { get; set; }
        public static SystemDiagnosticsConfig Config { get; set; }
        public static Stopwatch Stopwatch = new Stopwatch();
        internal static Process CurrentProcess { get; set; }
        internal static Assembly EntryAssembly { get; set; }
        public static string ProcessName = null;
        public static int ProcessId = -1;
        public static Reference<bool> _lockListenersNotifications = new Reference<bool>();
        public static ConcurrentQueue<TraceEntry> _pendingEntries = new ConcurrentQueue<TraceEntry>();

        // Asynchronous flow ambient data.
        public static AsyncLocal<CodeSection> CurrentCodeSection { get; set; } = new AsyncLocal<CodeSection>();
        public static AsyncLocal<IOperationContext> OperationContext { get; set; } = new AsyncLocal<IOperationContext>();
        #endregion

        #region .ctor
        static TraceManager()
        {
            Stopwatch.Start();
            CurrentProcess = Process.GetCurrentProcess();
            ProcessName = CurrentProcess.ProcessName;
            ProcessId = CurrentProcess.Id;

            EntryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        }
        #endregion

        #region Init

        private static void _lockListenersNotifications_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var pendingEntries = new ConcurrentQueue<TraceEntry>();
            var pendingEntriesTemp = _pendingEntries;
            _pendingEntries = pendingEntries;

            pendingEntriesTemp.ForEach(entry =>
            {
                var traceSource = entry.TraceSource;
                if (traceSource?.Listeners != null) foreach (TraceListener listener in traceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } }
                if (Trace.Listeners != null) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            });
        }
        public static void Init(SourceLevels filterLevel, IConfiguration configuration)
        {
            // start a telemetry lock 
            // during telemetry locks telemetry is stopped and accumulated for later streaming 
            _lockListenersNotifications.PropertyChanged += _lockListenersNotifications_PropertyChanged;
            using (new SwitchOnDispose(_lockListenersNotifications, true))
            {
                using (var sec = TraceManager.GetCodeSection(T))
                {
                    try
                    {
                        if (TraceManager.Configuration != null) { return; }

                        if (configuration == null)
                        {
                            //var env = hostingContext.HostingEnvironment;
                            var jsonFile = "";
                            var jsonFileName = "appsettings";
                            var currentDirectory = Directory.GetCurrentDirectory();
                            var appdomainFolder = System.AppDomain.CurrentDomain.BaseDirectory.Trim('\\');

                            jsonFile = currentDirectory == appdomainFolder ? $"{jsonFileName}.json" : Path.Combine(appdomainFolder, $"{jsonFileName}.json");
                            var builder = new ConfigurationBuilder()
                                          .AddJsonFile(jsonFile, true, true)
                                          //.AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true)
                                          .AddInMemoryCollection();
                            builder.AddEnvironmentVariables();
                            configuration = builder.Build();
                        }

                        TraceManager.Configuration = configuration;
                        ConfigurationHelper.Init(configuration);

                        var defaultConfig = new ListenerConfig()
                        {
                            name = "Default",
                            action = "add",
                            type = "Common.TraceListenerFormatItems, Common.Diagnostics",
                            innerListener = new ListenerConfig()
                            {
                                name = "Default",
                                action = "removeOrAdd",
                                type = "System.Diagnostics.DefaultTraceListener, System.Diagnostics.TraceSource"
                            }
                        };
                        ApplyListenerConfig(defaultConfig, Trace.Listeners);

                        var systemDiagnosticsConfig = new SystemDiagnosticsConfig();
                        configuration.GetSection("system.diagnostics").Bind(systemDiagnosticsConfig);
                        Config = systemDiagnosticsConfig;

                        var sourceConfig = systemDiagnosticsConfig?.sources?.FirstOrDefault(s => s.name == _traceSourceName);
                        var switchName = sourceConfig?.switchName;
                        var switchConfig = systemDiagnosticsConfig?.switches?.FirstOrDefault(sw => sw.name == switchName);

                        var sourceLevel = switchConfig != null ? switchConfig.value : SourceLevels.All;
                        TraceSource = new TraceSource(_traceSourceName, sourceLevel);
                        TraceSource.Listeners.Clear();

                        sourceConfig?.listeners?.ForEach(lc =>
                        {
                            ApplyListenerConfig(lc, TraceSource.Listeners);
                        });
                        Config?.sharedListeners?.ForEach(lc =>
                        {
                            ApplyListenerConfig(lc, Trace.Listeners);
                        });
                    }
                    catch (Exception ex)
                    {
                        sec.Exception(ex);
                    }
                }
            }
            // Release the telemetry lock 
            // now telemetry items are streamed to the listeners 
        }
        #endregion

        public static CodeSection GetCodeSection<T>(this T pthis, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(typeof(T), null, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }
        public static CodeSection GetCodeSection(Type t, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(t, null, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }
        public static CodeSection GetCodeSection<T>(object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(typeof(T), null, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }

        public static CodeSection GetNamedSection<T>(this T pthis, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(typeof(T), name, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }
        public static CodeSection GetNamedSection(Type t, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(t, name, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }
        public static CodeSection GetNamedSection<T>(string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            return new CodeSection(typeof(T), name, payload, TraceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber);
        }

        //public static void Debug(object obj, string category = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        //{
        //    var caller = CallContext.LogicalGetData("CurrentCodeSection") as CodeSection;
        //    if (caller == null) { var type = typeof(Application); caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true); }
        //    caller.Debug(obj, category, source);
        //}
        public static void Debug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Debug(message, category, properties, source);
        }
        public static void Debug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Debug(message, category, properties, source);
        }
        public static void Information(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Information, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Information(message, category, properties, source);
        }
        public static void Information(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Information, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Information(message, category, properties, source);
        }
        public static void Warning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Warning, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Warning(message, category, properties, source);
        }
        public static void Warning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Warning, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Warning(message, category, properties, source);
        }
        public static void Error(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Error(message, category, properties, source);
        }
        public static void Error(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Error(message, category, properties, source);
        }
        public static void Exception(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Exception(exception, category, properties, source);
        }

        // helpers
        #region GetModuleContext
        public static IModuleContext GetModuleContext(Assembly module)
        {
            if (!Modules.ContainsKey(module))
            {
                var moduleContext = new ModuleContext(module);
                Modules[module] = moduleContext;
            }
            return Modules[module];
        }
        #endregion
        #region GetMaxMessageLen
        public static int? GetMaxMessageLen(CodeSection section, TraceEventType traceEventType)
        {
            var maxMessageLenSpecific = default(int?);
            switch (traceEventType)
            {
                case TraceEventType.Error:
                case TraceEventType.Critical:
                    var maxMessageLenError = section?._maxMessageLenError ?? section?.ModuleContext?.MaxMessageLenError;
                    if (maxMessageLenError == null)
                    {
                        var val = ConfigurationHelper.GetSetting("MaxMessageLenError", TraceManager.CONFIGDEFAULT_MAXMESSAGELENERROR);
                        if (val != 0) { maxMessageLenError = val; if (section != null) { section._maxMessageLenError = maxMessageLenError; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenError = maxMessageLenError; } } }
                    }
                    if (maxMessageLenError != 0) { maxMessageLenSpecific = maxMessageLenError; }
                    break;
                case TraceEventType.Warning:
                    var maxMessageLenWarning = section?._maxMessageLenWarning ?? section?.ModuleContext?.MaxMessageLenWarning;
                    if (maxMessageLenWarning == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenWarning", TraceManager.CONFIGDEFAULT_MAXMESSAGELENWARNING);
                        if (val != 0) { maxMessageLenWarning = val; if (section != null) { section._maxMessageLenWarning = maxMessageLenWarning; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenWarning = maxMessageLenWarning; } } }
                    }
                    if (maxMessageLenWarning != 0) { maxMessageLenSpecific = maxMessageLenWarning; }
                    break;
                case TraceEventType.Information:
                    var maxMessageLenInfo = section?._maxMessageLenInfo ?? section?.ModuleContext?.MaxMessageLenInfo;
                    if (maxMessageLenInfo == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenInfo", TraceManager.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenInfo = val; if (section != null) { section._maxMessageLenInfo = maxMessageLenInfo; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenInfo = maxMessageLenInfo; } } }
                    }
                    if (maxMessageLenInfo != 0) { maxMessageLenSpecific = maxMessageLenInfo; }
                    break;
                case TraceEventType.Verbose:
                    var maxMessageLenVerbose = section?._maxMessageLenVerbose ?? section?.ModuleContext?.MaxMessageLenVerbose;
                    if (maxMessageLenVerbose == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenVerbose", TraceManager.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenVerbose = val; if (section != null) { section._maxMessageLenVerbose = maxMessageLenVerbose; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenVerbose = maxMessageLenVerbose; } } }
                    }
                    if (maxMessageLenVerbose != 0) { maxMessageLenSpecific = maxMessageLenVerbose; }
                    break;
            }
            var maxMessageLen = maxMessageLenSpecific ?? section?._maxMessageLen ?? section?.ModuleContext?.MaxMessageLen;
            if (maxMessageLen == null)
            {
                maxMessageLen = ConfigurationHelper.GetSetting<int>("MaxMessageLen", TraceManager.CONFIGDEFAULT_MAXMESSAGELEN);
                if (section != null) { section._maxMessageLen = maxMessageLen; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLen = maxMessageLen; } }
            }
            if (section != null) { section._maxMessageLen = maxMessageLen; }
            return maxMessageLen;
        }
        #endregion
        #region ApplyListenerConfig
        private static void ApplyListenerConfig(ListenerConfig listenerConfig, TraceListenerCollection listeners)
        {
            var action = listenerConfig.action ?? "add";
            var listener = default(TraceListener);
            listener = GetListenerFromConfig(listenerConfig, listeners);
            if (action.ToLower() != "remove" && listener != null)
            {
                listeners.Add(listener);
            }
        }
        #endregion
        #region GetListenerFromConfig
        private static TraceListener GetListenerFromConfig(ListenerConfig listenerConfig, TraceListenerCollection listeners, string defaultAction = "add")
        {
            var action = listenerConfig.action ?? defaultAction;
            switch (action.ToLower())
            {
                case "add":
                    {
                        var listener = default(TraceListener);
                        Type t = Type.GetType(listenerConfig.type);
                        try { listener = Activator.CreateInstance(t) as TraceListener; }
                        catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
                        if (listener != null)
                        {
                            listener.Name = listenerConfig.name;
                            if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
                            {
                                if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
                                {
                                    var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
                                    var filter = new EventTypeFilter(type);
                                    listener.Filter = filter;
                                }
                                if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
                                {
                                    var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
                                    if (filter != null) { listener.Filter = filter; }
                                }
                            }
                            TraceListener innerListener = null;
                            var innerListenerConfig = listenerConfig.innerListener;
                            if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
                            if (innerListener != null)
                            {
                                var outerListener = listener as ISupportInnerListener;
                                outerListener.InnerListener = innerListener;
                            }
                        }
                        return listener;
                    }
                case "attach":
                    {
                        var listener = default(TraceListener);
                        Type t = Type.GetType(listenerConfig.type);
                        try { listener = Activator.CreateInstance(t) as TraceListener; }
                        catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
                        if (listener != null)
                        {
                            listener.Name = listenerConfig.name;
                            if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
                            {
                                if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
                                {
                                    var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
                                    var filter = new EventTypeFilter(type);
                                    listener.Filter = filter;
                                }
                                if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
                                {
                                    var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
                                    if (filter != null) { listener.Filter = filter; }
                                }
                            }
                            TraceListener innerListener = null;
                            var innerListenerConfig = listenerConfig.innerListener;
                            if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
                            if (innerListener != null)
                            {
                                var outerListener = listener as ISupportInnerListener;
                                outerListener.InnerListener = innerListener;
                            }
                        }
                        return listener;
                    }
                case "remove":
                    {
                        var listener = default(TraceListener);
                        if (!string.IsNullOrEmpty(listenerConfig.name))
                        {
                            listener = listeners.OfType<TraceListener>().FirstOrDefault(l => l.Name == listenerConfig.name);
                            if (listener != null) { listeners.Remove(listener); }
                        }
                        else if (!string.IsNullOrEmpty(listenerConfig.type))
                        {
                            var t = Type.GetType(listenerConfig.type);
                            listener = listeners.OfType<TraceListener>().FirstOrDefault(l => t.IsAssignableFrom(l.GetType()));
                            if (listener != null) { listeners.Remove(listener); }
                        }
                        return listener;
                    }
                case "removeoradd":
                    {
                        var listener = default(TraceListener);
                        if (!string.IsNullOrEmpty(listenerConfig.name))
                        {
                            listener = listeners.OfType<TraceListener>().FirstOrDefault(l => l.Name == listenerConfig.name);
                            if (listener != null) { listeners.Remove(listener); }
                        }
                        else if (!string.IsNullOrEmpty(listenerConfig.type))
                        {
                            var t = Type.GetType(listenerConfig.type);
                            listener = listeners.OfType<TraceListener>().FirstOrDefault(l => t.IsAssignableFrom(l.GetType()));
                            if (listener != null) { listeners.Remove(listener); }
                        }
                        if (listener == null && !string.IsNullOrEmpty(listenerConfig.type))
                        {
                            Type t = Type.GetType(listenerConfig.type);
                            try { listener = Activator.CreateInstance(t) as TraceListener; }
                            catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
                            if (listener != null)
                            {
                                listener.Name = listenerConfig.name;
                                if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
                                {
                                    if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
                                    {
                                        var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
                                        var filter = new EventTypeFilter(type);
                                        listener.Filter = filter;
                                    }
                                    if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
                                    {
                                        var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
                                        if (filter != null) { listener.Filter = filter; }
                                    }
                                }
                                TraceListener innerListener = null;
                                var innerListenerConfig = listenerConfig.innerListener;
                                if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
                                if (innerListener != null)
                                {
                                    var outerListener = listener as ISupportInnerListener;
                                    outerListener.InnerListener = innerListener;
                                }
                            }
                        }
                        return listener;
                    }
                case "clear":
                    {
                        listeners.Clear();
                    }
                    break;
            }

            return null;
        }
        #endregion
    }

    // TRACE ENTRY
    public struct TraceEntry
    {
        public TraceEventType TraceEventType { get; set; }
        [JsonIgnore]
        public TraceSource TraceSource { get; set; }
        public string Message { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public Exception Exception { get; set; }
        [JsonIgnore]
        public Thread Thread { get; set; }
        public int ThreadID { get; set; }
        public ApartmentState ApartmentState { get; set; }
        public bool DisableCRLFReplace { get; set; }
        [JsonIgnore]
        public CodeSection CodeSection { get; set; }
        [JsonIgnore]
        public RequestContext RequestContext { get; set; }
        [JsonIgnore]
        public ProcessInfo ProcessInfo { get; set; }
        [JsonIgnore]
        public SystemInfo SystemInfo { get; set; }

        public override string ToString()
        {
            return base.ToString();
        }
    }
    public struct TraceEntrySurrogate
    {
        public TraceEventType TraceEventType { get; set; }
        public string TraceSourceName { get; set; }
        public string Message { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public Exception Exception { get; set; }
        public int ThreadID { get; set; }
        public ApartmentState ApartmentState { get; set; }
        public bool DisableCRLFReplace { get; set; }
        public CodeSectionSurrogate CodeSection { get; set; }
    }
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
                           string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
                           : this(pthis.GetType(), name, payload, traceSource, sourceLevel, category, properties, source, memberName, sourceFilePath, sourceLineNumber)
        { }

        public CodeSection(Type type, string name = null, object payload = null, TraceSource traceSource = null, SourceLevels sourceLevel = SourceLevels.Verbose,
            string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0, bool disableStartEndTraces = false)
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

            _caller = TraceManager.CurrentCodeSection.Value;
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

            var entry = new TraceEntry() { TraceEventType = TraceEventType.Start, TraceSource = this.TraceSource, Message = null, Properties = properties, Source = source, Category = category, SourceLevel = sourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) foreach (TraceListener listener in traceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } }
                if (Trace.Listeners != null && Trace.Listeners.Count>0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }
        }
        #endregion

        public void Debug(object obj, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            var message = obj.GetLogString();

            var entry = new TraceEntry() { Message = message, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }
        }
        public void Debug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }
        }
        public void Debug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Verbose)) { return; }

            try
            {
                var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Verbose, SourceLevel = SourceLevels.Verbose, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
                if (!TraceManager._lockListenersNotifications.Value)
                {
                    if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                    if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                }
                else { TraceManager._pendingEntries.Enqueue(entry); }
            }
            catch (Exception) { }
        }

        public void Information(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Information)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }
        }
        public void Information(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Information)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Information, SourceLevel = SourceLevels.Information, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }
        }

        public void Warning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Warning)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }

        }
        public void Warning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Warning)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Warning, SourceLevel = SourceLevels.Warning, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }

        }

        public void Error(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Error)) { return; }

            var entry = new TraceEntry() { Message = message.Value, TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }

        }
        public void Error(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false)
        {
            if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Error)) { return; }

            var entry = new TraceEntry() { Message = string.Format(message.Format, message.GetArguments()), TraceEventType = TraceEventType.Error, SourceLevel = SourceLevels.Error, TraceSource = this.TraceSource, Properties = properties, Source = source ?? this.Source, Category = category, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), DisableCRLFReplace = disableCRLFReplace, ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }

        }

        public void Exception(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = true)
        {
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
                ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds
            };
            if (!TraceManager._lockListenersNotifications.Value)
            {
                if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            }
            else { TraceManager._pendingEntries.Enqueue(entry); }

        }

        bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;

            try
            {
                if (!TraceManager._lockListenersNotifications.Value)
                {
                    if (TraceSource?.Switch != null && !TraceSource.Switch.ShouldTrace(TraceEventType.Stop) || this.DisableStartEndTraces == true) { return; }

                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
                    if (TraceSource?.Listeners != null && TraceSource.Listeners.Count > 0) { foreach (TraceListener listener in TraceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                    if (Trace.Listeners != null && Trace.Listeners.Count > 0) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
                }
                else
                {
                    var entry = new TraceEntry() { TraceEventType = TraceEventType.Stop, TraceSource = this.TraceSource, Message = null, Properties = this.Properties, Source = this.Source, Category = this.Category, SourceLevel = this.SourceLevel, CodeSection = this, Thread = Thread.CurrentThread, ThreadID = Thread.CurrentThread.ManagedThreadId, ApartmentState = Thread.CurrentThread.GetApartmentState(), ElapsedMilliseconds = TraceManager.Stopwatch.ElapsedMilliseconds };
                    TraceManager._pendingEntries.Enqueue(entry);
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

