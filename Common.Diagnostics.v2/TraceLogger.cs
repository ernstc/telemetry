using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

// opzioni per nomenclatura definitiva:
// . Diginsight.Dotnet.Diagnostics // .Trace
// . Diginsight.Dotnet.Logger
// . Diginsight.Dotnet.Xxxx
// Microsoft.Extensions.Logging.ExecutionFlow
// EFDF => TraceEntry 
// Execution entry { MethodName, ClassName, TID   ,,,,,,,,, }

namespace Common
{
    // ConfigureServices() 
    //      loggerFactory.AddDiginsight();
    //      

    //public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
    //{
    //    ...
    // 
    //    var diginsightFactory = loggerFactory.AddDiginsight(app, env, Configuration);
    //                            diginsightFactory.AddApplicationInsights()
    //                            diginsightFactory.AddConsole()
    //                            diginsightFactory.AddEventLog()
    ////   loggerFactory.AddApplicationInsights()
    ////   loggerFactory.AddConsole()
    ////   loggerFactory.AddEventLog()

    //    MessagePackSerializer.SetDefaultResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
    //}

    public class TraceLogger : ILogger
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
        private static Type T = typeof(TraceLogger);
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
        public static IList<ILogger> Listeners { get; }
        #endregion

        #region .ctor
        static TraceLogger()
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
        public static void Init(SourceLevels filterLevel, IConfiguration configuration)
        {
            using (new SwitchOnDispose(_lockListenersNotifications, true))
            using (new SwitchOnDispose(_isInitializing, true))
            using (new SwitchOnDispose(_isInitializeComplete, false))
            {
                //using (var sec = TraceLogger.GetCodeSection(T))
                //{
                try
                {
                    if (TraceLogger.Configuration != null) { return; }

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

                    TraceLogger.Configuration = configuration;
                    ConfigurationHelper.Init(configuration);

                }
                catch (Exception ex)
                {
                    var message = $"Exception '{ex.GetType().Name}' occurred: {ex.Message}\r\nAdditional Information:\r\n{ex}";
                    //sec.Exception(new InvalidDataException(message, ex));
                    Trace.WriteLine(message);
                }
                //}
            }
        }
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
        #endregion



        // ILogger
        public IDisposable BeginScope<TState>(TState state)
        {

            return null;
        }
        public bool IsEnabled(LogLevel logLevel)
        {

            return true;
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // ... 

            if (TraceLogger.Listeners != null && TraceLogger.Listeners.Count > 0)
            {
                foreach (var listener in TraceLogger.Listeners)
                {
                    try
                    {
                        listener.Log(logLevel, eventId, state, exception, formatter); // state => TraceEntry
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return;
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
                        var val = ConfigurationHelper.GetSetting("MaxMessageLenError", TraceLogger.CONFIGDEFAULT_MAXMESSAGELENERROR);
                        if (val != 0) { maxMessageLenError = val; if (section != null) { section._maxMessageLenError = maxMessageLenError; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenError = maxMessageLenError; } } }
                    }
                    if (maxMessageLenError != 0) { maxMessageLenSpecific = maxMessageLenError; }
                    break;
                case TraceEventType.Warning:
                    var maxMessageLenWarning = section?._maxMessageLenWarning ?? section?.ModuleContext?.MaxMessageLenWarning;
                    if (maxMessageLenWarning == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenWarning", TraceLogger.CONFIGDEFAULT_MAXMESSAGELENWARNING);
                        if (val != 0) { maxMessageLenWarning = val; if (section != null) { section._maxMessageLenWarning = maxMessageLenWarning; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenWarning = maxMessageLenWarning; } } }
                    }
                    if (maxMessageLenWarning != 0) { maxMessageLenSpecific = maxMessageLenWarning; }
                    break;
                case TraceEventType.Information:
                    var maxMessageLenInfo = section?._maxMessageLenInfo ?? section?.ModuleContext?.MaxMessageLenInfo;
                    if (maxMessageLenInfo == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenInfo", TraceLogger.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenInfo = val; if (section != null) { section._maxMessageLenInfo = maxMessageLenInfo; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenInfo = maxMessageLenInfo; } } }
                    }
                    if (maxMessageLenInfo != 0) { maxMessageLenSpecific = maxMessageLenInfo; }
                    break;
                case TraceEventType.Verbose:
                    var maxMessageLenVerbose = section?._maxMessageLenVerbose ?? section?.ModuleContext?.MaxMessageLenVerbose;
                    if (maxMessageLenVerbose == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenVerbose", TraceLogger.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenVerbose = val; if (section != null) { section._maxMessageLenVerbose = maxMessageLenVerbose; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenVerbose = maxMessageLenVerbose; } } }
                    }
                    if (maxMessageLenVerbose != 0) { maxMessageLenSpecific = maxMessageLenVerbose; }
                    break;
            }
            var maxMessageLen = maxMessageLenSpecific ?? section?._maxMessageLen ?? section?.ModuleContext?.MaxMessageLen;
            if (maxMessageLen == null)
            {
                maxMessageLen = ConfigurationHelper.GetSetting<int>("MaxMessageLen", TraceLogger.CONFIGDEFAULT_MAXMESSAGELEN);
                if (section != null) { section._maxMessageLen = maxMessageLen; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLen = maxMessageLen; } }
            }
            if (section != null) { section._maxMessageLen = maxMessageLen; }
            return maxMessageLen;
        }
        #endregion
        //#region ApplyListenerConfig
        //private static void ApplyListenerConfig(ListenerConfig listenerConfig, TraceListenerCollection listeners)
        //{
        //    var action = listenerConfig.action ?? "add";
        //    var listenerType = listenerConfig.type;
        //    if (listenerConfig.innerListener != null)
        //    {
        //        for (var innerListener = listenerConfig.innerListener; innerListener != null; innerListener = innerListener.innerListener) { listenerType = $"{listenerType}/{innerListener.type}"; }
        //    }
        //    //TraceLogger.Information($"Listener action:'{action}', type:'{listenerType}'");

        //    var listener = default(TraceListener);
        //    listener = GetListenerFromConfig(listenerConfig, listeners);
        //    if (action.ToLower() != "remove" && listener != null)
        //    {
        //        listeners.Add(listener);
        //    }
        //}
        //#endregion
        //#region GetListenerFromConfig
        //private static TraceListener GetListenerFromConfig(ListenerConfig listenerConfig, TraceListenerCollection listeners, string defaultAction = "add")
        //{
        //    var action = listenerConfig.action ?? defaultAction;
        //    switch (action.ToLower())
        //    {
        //        case "add":
        //            {
        //                var listener = default(TraceListener);
        //                Type t = Type.GetType(listenerConfig.type);
        //                try { listener = Activator.CreateInstance(t) as TraceListener; }
        //                catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
        //                if (listener != null)
        //                {
        //                    listener.Name = listenerConfig.name;
        //                    if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
        //                    {
        //                        if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
        //                        {
        //                            var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
        //                            var filter = new EventTypeFilter(type);
        //                            listener.Filter = filter;
        //                        }
        //                        if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
        //                        {
        //                            var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
        //                            if (filter != null) { listener.Filter = filter; }
        //                        }
        //                    }
        //                    TraceListener innerListener = null;
        //                    var innerListenerConfig = listenerConfig.innerListener;
        //                    if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
        //                    if (innerListener != null)
        //                    {
        //                        var outerListener = listener as ISupportInnerListener;
        //                        outerListener.InnerListener = innerListener;
        //                    }
        //                }
        //                return listener;
        //            }
        //        case "attach":
        //            {
        //                var listener = default(TraceListener);
        //                Type t = Type.GetType(listenerConfig.type);
        //                try { listener = Activator.CreateInstance(t) as TraceListener; }
        //                catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
        //                if (listener != null)
        //                {
        //                    listener.Name = listenerConfig.name;
        //                    if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
        //                    {
        //                        if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
        //                        {
        //                            var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
        //                            var filter = new EventTypeFilter(type);
        //                            listener.Filter = filter;
        //                        }
        //                        if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
        //                        {
        //                            var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
        //                            if (filter != null) { listener.Filter = filter; }
        //                        }
        //                    }
        //                    TraceListener innerListener = null;
        //                    var innerListenerConfig = listenerConfig.innerListener;
        //                    if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
        //                    if (innerListener != null)
        //                    {
        //                        var outerListener = listener as ISupportInnerListener;
        //                        outerListener.InnerListener = innerListener;
        //                    }
        //                }
        //                return listener;
        //            }
        //        case "remove":
        //            {
        //                var listener = default(TraceListener);
        //                if (!string.IsNullOrEmpty(listenerConfig.name))
        //                {
        //                    listener = listeners.OfType<TraceListener>().FirstOrDefault(l => l.Name == listenerConfig.name);
        //                    if (listener != null) { listeners.Remove(listener); }
        //                }
        //                else if (!string.IsNullOrEmpty(listenerConfig.type))
        //                {
        //                    var t = Type.GetType(listenerConfig.type);
        //                    listener = listeners.OfType<TraceListener>().FirstOrDefault(l => t.IsAssignableFrom(l.GetType()));
        //                    if (listener != null) { listeners.Remove(listener); }
        //                }
        //                return listener;
        //            }
        //        case "removeoradd":
        //            {
        //                var listener = default(TraceListener);
        //                if (!string.IsNullOrEmpty(listenerConfig.name))
        //                {
        //                    listener = listeners.OfType<TraceListener>().FirstOrDefault(l => l.Name == listenerConfig.name);
        //                    if (listener != null) { listeners.Remove(listener); }
        //                }
        //                else if (!string.IsNullOrEmpty(listenerConfig.type))
        //                {
        //                    var t = Type.GetType(listenerConfig.type);
        //                    listener = listeners.OfType<TraceListener>().FirstOrDefault(l => t.IsAssignableFrom(l.GetType()));
        //                    if (listener != null) { listeners.Remove(listener); }
        //                }
        //                if (listener == null && !string.IsNullOrEmpty(listenerConfig.type))
        //                {
        //                    Type t = Type.GetType(listenerConfig.type);
        //                    try { listener = Activator.CreateInstance(t) as TraceListener; }
        //                    catch (Exception ex) { Trace.WriteLine($"Failed to create Trace Listener '{listenerConfig.type}':\r\nAdditional information: {ex.Message}\r\n{ex.ToString()}"); }
        //                    if (listener != null)
        //                    {
        //                        listener.Name = listenerConfig.name;
        //                        if (listenerConfig.filter != null && !string.IsNullOrEmpty(listenerConfig.filter.initializeData))
        //                        {
        //                            if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("EventTypeFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.EventTypeFilter")))
        //                            {
        //                                var type = (SourceLevels)Enum.Parse(typeof(SourceLevels), listenerConfig.filter.initializeData);
        //                                var filter = new EventTypeFilter(type);
        //                                listener.Filter = filter;
        //                            }
        //                            if (listenerConfig.filter.type != null && (listenerConfig.filter.type.StartsWith("SourceFilter") || listenerConfig.filter.type.StartsWith("System.Diagnostics.SourceFilter")))
        //                            {
        //                                var filter = !string.IsNullOrEmpty(listenerConfig.filter.initializeData) ? new SourceFilter(listenerConfig.filter.initializeData) : null;
        //                                if (filter != null) { listener.Filter = filter; }
        //                            }
        //                        }
        //                        TraceListener innerListener = null;
        //                        var innerListenerConfig = listenerConfig.innerListener;
        //                        if (innerListenerConfig != null) { innerListener = GetListenerFromConfig(innerListenerConfig, listeners); }
        //                        if (innerListener != null)
        //                        {
        //                            var outerListener = listener as ISupportInnerListener;
        //                            outerListener.InnerListener = innerListener;
        //                        }
        //                    }
        //                }
        //                return listener;
        //            }
        //        case "clear":
        //            {
        //                listeners.Clear();
        //            }
        //            break;
        //    }

        //    return null;
        //}
        //#endregion
    }
    public static class TraceLoggerExtensions {
        public static CodeSection GetCodeSection<T>(this T pthis, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), null, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            //var stopTicks = TraceLogger.Stopwatch.ElapsedTicks;
            //var delta = stopTicks - startTicks;
            return sec;
        }
        public static CodeSection GetCodeSection(Type t, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(t, null, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetCodeSection<T>(object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), null, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }

        public static CodeSection GetNamedSection<T>(this T pthis, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), name, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetNamedSection(Type t, string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(t, name, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }
        public static CodeSection GetNamedSection<T>(string name = null, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var sec = new CodeSection(typeof(T), name, payload, TraceLogger.TraceSource, sourceLevel, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            return sec;
        }

    }



    public class TraceLoggerFactory : ILoggerFactory
    {

        public void AddProvider(ILoggerProvider provider)
        {
            // var _loggerAI = provider.CreateLogger() // string categoryName
            // this.Listeners.Add(_loggerAI);
        }

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new TraceLogger();
            return logger;
        }

        public void Dispose()
        {
            ;
        }
    }
    public static class TraceLoggerFactoryExtensions
    {
        //public static ILoggerFactory AddApplicationInsights(this ILoggerFactory factory, IServiceProvider serviceProvider, LogLevel minLevel);
        public static ILoggerFactory AddDiginsight(this ILoggerFactory factory, IServiceProvider serviceProvider, LogLevel minLevel)
        {
            return null;
        }
    }
    //public class TraceLoggerProvider : ILoggerProvider
    //{
    //    public static IList<ILogger> Listeners { get; }

    //    // ILoggerFactory AddApplicationInsights()
    //    public void AddProvider(ILoggerProvider provider)
    //    {
    //        ;
    //    }
    //    public ILogger CreateLogger(string categoryName)
    //    {
    //    }
    //    public void Dispose()
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

}
