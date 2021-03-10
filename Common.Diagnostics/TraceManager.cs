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
        public static string EnvironmentName = null;
        public static int ProcessId = -1;
        internal static Reference<bool> _lockListenersNotifications = new Reference<bool>(true);
        internal static Reference<bool> _isInitializing = new Reference<bool>(false);
        internal static Reference<bool> _isInitializeComplete = new Reference<bool>(false);
        internal static ConcurrentQueue<TraceEntry> _pendingEntries = new ConcurrentQueue<TraceEntry>();

        // Asynchronous flow ambient data.
        public static AsyncLocal<CodeSection> CurrentCodeSection { get; set; } = new AsyncLocal<CodeSection>();
        public static AsyncLocal<IOperationContext> OperationContext { get; set; } = new AsyncLocal<IOperationContext>();
        #endregion

        #region .ctor
        static TraceManager()
        {
            _lockListenersNotifications.PropertyChanged += _lockListenersNotifications_PropertyChanged;

            Stopwatch.Start();
            try
            {
                CurrentProcess = Process.GetCurrentProcess();
                ProcessName = CurrentProcess.ProcessName;
                ProcessId = CurrentProcess.Id;
            }
            catch (PlatformNotSupportedException)
            {
                ProcessName = "NO_PROCESS";
                ProcessId = -1;
            }


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
            using (new SwitchOnDispose(_lockListenersNotifications, true))
            using (new SwitchOnDispose(_isInitializing, true))
            using (new SwitchOnDispose(_isInitializeComplete, false))
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

                            var environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.ToLower();
                            if (string.IsNullOrEmpty(environment)) { environment = System.Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLower(); }
                            if (string.IsNullOrEmpty(environment)) { environment = "production"; }

                            jsonFile = currentDirectory == appdomainFolder ? $"{jsonFileName}.json" : Path.Combine(appdomainFolder, $"{jsonFileName}.json");

                            var builder = default(IConfigurationBuilder);
                            DebugHelper.IfDebug(() =>
                            {   // for debug build only check environment setting on appsettings.json
                                builder = new ConfigurationBuilder()
                                              .AddJsonFile(jsonFile, true, true)
                                              .AddInMemoryCollection();
                                builder.AddEnvironmentVariables();
                                configuration = builder.Build();
                                var jsonEnvironment = Configuration.GetValue($"AppSettings:Environment", "");
                                if (string.IsNullOrEmpty(jsonEnvironment)) { environment = jsonEnvironment; }
                            });

                            builder = new ConfigurationBuilder()
                                      .AddJsonFile(jsonFile, true, true)
                                      .AddJsonFile($"appsettings.{environment}.json", true, true)
                                      .AddInMemoryCollection();
                            builder.AddEnvironmentVariables();
                            configuration = builder.Build();

                            TraceManager.EnvironmentName = environment;
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
                        var message = $"Exception '{ex.GetType().Name}' occurred: {ex.Message}\r\nAdditional Information:\r\n{ex}";
                        sec.Exception(new InvalidDataException(message, ex));
                        Trace.WriteLine(message);
                    }
                }
            }
        }
        #endregion

        public static CodeSection GetCodeSection<T>(this T pthis, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), null, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            //var stopTicks = TraceManager.Stopwatch.ElapsedTicks;
            //var delta = stopTicks - startTicks;
            return sec;
        }
        public static CodeSection GetCodeSection(Type t, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(t, null, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetCodeSection<T>(object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), null, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }

        public static CodeSection GetNamedSection<T>(this T pthis, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), name, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetNamedSection(Type t, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(t, name, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetNamedSection<T>(string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), name, payload, TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }

        //public static void Debug(object obj, string category = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        //{
        //    var caller = CallContext.LogicalGetData("CurrentCodeSection") as CodeSection;
        //    if (caller == null) { var type = typeof(Application); caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, memberName, sourceFilePath, sourceLineNumber, true); }
        //    caller.Debug(obj, category, source);
        //}
        public static void Debug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Debug(message, category, properties, source);
        }
        public static void Debug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Verbose, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Debug(message, category, properties, source);
        }
        public static void Information(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Information(message, category, properties, source);
        }
        public static void Information(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Information(message, category, properties, source);
        }
        public static void Warning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Warning, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Warning(message, category, properties, source);
        }
        public static void Warning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Warning, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Warning(message, category, properties, source);
        }
        public static void Error(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Error(message, category, properties, source);
        }
        public static void Error(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerCodeSection.Error(message, category, properties, source);
        }
        public static void Exception(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceManager.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            CodeSection caller = CurrentCodeSection.Value as CodeSection;
            CodeSection innerCodeSection = caller != null ? caller = caller.GetInnerCodeSection() : caller = new CodeSection(type, null, null, null, SourceLevels.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
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
            var listenerType = listenerConfig.type;
            if (listenerConfig.innerListener != null)
            {
                for (var innerListener = listenerConfig.innerListener; innerListener != null; innerListener = innerListener.innerListener) { listenerType = $"{listenerType}/{innerListener.type}"; }
            }
            TraceManager.Information($"Listener action:'{action}', type:'{listenerType}'");

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
}

