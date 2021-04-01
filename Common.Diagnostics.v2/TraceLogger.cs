#region using
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
#endregion

// options for final naming:
// . Diginsight.System.Diagnostics 
// . Diginsight.System.Diagnostics.Log4Net 
// . Diginsight.System.Diagnostics.Serilog
// . Diginsight.Dotnet.Logger
// . Diginsight.Dotnet.Xxxx
// Microsoft.Extensions.Logging.ExecutionFlow
// EFDF => TraceEntry 
// Execution entry { MethodName, ClassName, TID   ,,,,,,,,, }

namespace Common
{
    public class TraceLogger : ILogger
    {
        #region internal state
        private static Type T = typeof(TraceLogger);
        private static readonly string _traceSourceName = "TraceSource";
        public string Name { get; set; }
        public static TraceSource TraceSource { get; set; }
        public static Stopwatch Stopwatch = new Stopwatch();
        public static IHost Host { get; set; }
        internal static Process CurrentProcess { get; set; }
        internal static Assembly EntryAssembly { get; set; }
        public static SystemDiagnosticsConfig Config { get; set; }
        public static string ProcessName = null;
        public static string EnvironmentName = null;
        public static int ProcessId = -1;
        public IList<ILogger> Listeners { get; } = new List<ILogger>();
        public ILoggerProvider Provider { get; set; }
        public static IConfiguration Configuration { get; private set; }

        internal static ConcurrentQueue<TraceEntry> _pendingEntries = new ConcurrentQueue<TraceEntry>();
        internal static Reference<bool> _lockListenersNotifications = new Reference<bool>(true);
        internal static Reference<bool> _isInitializing = new Reference<bool>(false);
        internal static Reference<bool> _isInitializeComplete = new Reference<bool>(false);
        #endregion

        #region .ctor
        static TraceLogger()
        {
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
        public TraceLogger(ILoggerProvider provider, string name)
        {
            this.Provider = provider;
            this.Name = name;
        }
        #endregion
        #region Init
        public static void Init(IConfiguration configuration)
        {
            if (TraceLogger.Configuration != null) { return; }
            using (new SwitchOnDispose(_lockListenersNotifications, true))
            using (new SwitchOnDispose(_isInitializing, true))
            using (new SwitchOnDispose(_isInitializeComplete, false))
            using (var sc = TraceLogger.BeginMethodScope(T))
            {
                try
                {
                    _lockListenersNotifications.PropertyChanged += _lockListenersNotifications_PropertyChanged;
                    if (configuration == null) { configuration = GetConfiguration(); }
                    TraceLogger.Configuration = configuration;
                    ConfigurationHelper.Init(configuration);
                }
                catch (Exception ex)
                {
                    var message = $"Exception '{ex.GetType().Name}' occurred: {ex.Message}\r\nAdditional Information:\r\n{ex}";
                    sc.LogException(new InvalidDataException(message, ex));
                    //Trace.WriteLine(message);
                }
            }
        }
        private static void _lockListenersNotifications_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var pendingEntries = new ConcurrentQueue<TraceEntry>();
            var pendingEntriesTemp = _pendingEntries;
            _pendingEntries = pendingEntries;

            pendingEntriesTemp.ForEach(entry =>
            {
                //var traceSource = entry.TraceSource;
                //if (traceSource?.Listeners != null) foreach (TraceListener listener in traceSource.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } }
                //if (Trace.Listeners != null) { foreach (TraceListener listener in Trace.Listeners) { try { listener.WriteLine(entry); } catch (Exception) { } } }
            });
        }
        #endregion


        // ILogger
        public IDisposable BeginScope<TState>(TState state)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            var classNameIndex = this.Name.LastIndexOf('.') + 1;
            var source = classNameIndex >= 0 ? this.Name.Substring(0, classNameIndex) : this.Name;
            var sec = new SectionScope(this, this.Name, null, null, TraceLogger.TraceSource, SourceLevels.Verbose, LogLevel.Trace, this.Name, null, source, startTicks, state?.ToString(), null, -1);
            return sec;
        }
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var entry = default(TraceEntry);
            if (state is TraceEntry e) { entry = e; }
            else
            {
                var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
                var type = typeof(Application);
                var caller = SectionScope.Current.Value as SectionScope;

                var classNameIndex = this.Name.LastIndexOf('.');
                var source = classNameIndex >= 0 ? this.Name.Substring(0, classNameIndex) : this.Name;
                var innerSectionScope = caller = caller != null ? caller.GetInnerScope() : new SectionScope(this, this.Name, null, null, TraceLogger.TraceSource, SourceLevels.Verbose, LogLevel.Debug, this.Name, null, source, startTicks, "Unknown", null, -1, true) { IsInnerScope = true };

                var stateFormatter = formatter != null ? formatter : (s, exc) => { return s.GetLogString(); };

                entry = new TraceEntry()
                {
                    GetMessage = () => { return stateFormatter(state, null); },
                    TraceEventType = TraceEventType.Verbose,
                    SourceLevel = SourceLevels.Verbose,
                    Properties = null,
                    Source = source,
                    Category = this.Name,
                    SectionScope = innerSectionScope,
                    Thread = Thread.CurrentThread,
                    ThreadID = Thread.CurrentThread.ManagedThreadId,
                    ApartmentState = Thread.CurrentThread.GetApartmentState(),
                    DisableCRLFReplace = false,
                    ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds,
                    TraceStartTicks = startTicks
                };
            }

            if (this.Listeners != null && this.Listeners.Count > 0)
            {
                foreach (var listener in this.Listeners)
                {
                    try
                    {
                        if (!TraceLogger._lockListenersNotifications.Value) //  && _logger != null
                        {
                            IFormatTraceEntry entryFormatter = Provider as IFormatTraceEntry;
                            listener.Log(logLevel, eventId, entry, exception, entryFormatter != null ? (Func<TraceEntry, Exception, string>)entryFormatter.FormatTraceEntry : null);
                            //if (state is TraceEntry) { }
                            //listener.Log(logLevel, eventId, state, exception, formatter);
                        }
                        else
                        {
                            TraceLogger._pendingEntries.Enqueue(entry);
                            if (TraceLogger._isInitializeComplete.Value == false && TraceLogger._isInitializing.Value == false) { TraceLogger.Init(null); }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }

            return;
        }

        // helpers
        public static string GetMethodName([CallerMemberName] string memberName = "") { return memberName; }
        public static SectionScope BeginMethodScope<T>(object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel LogLevel = LogLevel.Trace, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            ILogger<T> logger = null;
            if (logger == null && TraceLogger.Host != null)
            {
                var host = TraceLogger.Host;
                logger = host.Services.GetRequiredService<ILogger<T>>();
            }

            var sec = new SectionScope(logger, typeof(T), null, payload, TraceLogger.TraceSource, sourceLevel, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            var stopTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var delta = stopTicks - startTicks;
            return sec;
        }
        public static SectionScope BeginMethodScope(Type t, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel LogLevel = LogLevel.Trace, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            ILogger logger = null;
            if (TraceLogger.Host != null)
            {
                Type loggerType = typeof(ILogger<>);
                loggerType = loggerType.MakeGenericType(new[] { t });
                var host = TraceLogger.Host;
                logger = host.Services.GetRequiredService(loggerType) as ILogger;
            }

            var sec = new SectionScope(logger, t, null, payload, TraceLogger.TraceSource, sourceLevel, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            var stopTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var delta = stopTicks - startTicks;
            return sec;
        }

        public static IConfiguration GetConfiguration()
        {
            IConfiguration configuration = null;
            var jsonFileName = "appsettings";
            var currentDirectory = Directory.GetCurrentDirectory();
            var appdomainFolder = System.AppDomain.CurrentDomain.BaseDirectory.Trim('\\');

            var environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.ToLower();
            if (string.IsNullOrEmpty(environment)) { environment = System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.ToLower(); }
            if (string.IsNullOrEmpty(environment)) { environment = System.Environment.GetEnvironmentVariable("ENVIRONMENT")?.ToLower(); }
            if (string.IsNullOrEmpty(environment)) { environment = "production"; }

            var jsonFile = currentDirectory == appdomainFolder ? $"{jsonFileName}.json" : Path.Combine(appdomainFolder, $"{jsonFileName}.json");
            var builder = default(IConfigurationBuilder);
            DebugHelper.IfDebug(() =>
            {   // for debug build only check environment setting on appsettings.json
                builder = new ConfigurationBuilder()
                              .AddJsonFile(jsonFile, true, true)
                              .AddInMemoryCollection();

                builder.AddEnvironmentVariables();
                configuration = builder.Build();
                var jsonEnvironment = configuration.GetValue($"AppSettings:Environment", "");
                if (string.IsNullOrEmpty(jsonEnvironment)) { environment = jsonEnvironment; }
            });

            var environmentJsonFile = currentDirectory == appdomainFolder ? $"{jsonFileName}.json" : Path.Combine(appdomainFolder, $"{jsonFileName}.{environment}.json");
            builder = new ConfigurationBuilder()
                      .AddJsonFile(jsonFile, true, true);
            if (File.Exists(environmentJsonFile)) { builder = builder.AddJsonFile(environmentJsonFile, true, true); }
            builder = builder.AddInMemoryCollection();
            builder.AddEnvironmentVariables();
            configuration = builder.Build();

            TraceLogger.EnvironmentName = environment;

            return configuration;
        }

    }
    public static class TraceLoggerExtensions
    {
        public static SectionScope BeginMethodScope<T>(this ILogger<T> logger, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel LogLevel = LogLevel.Trace, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

            if (logger == null && TraceLogger.Host != null)
            {
                var host = TraceLogger.Host;
                logger = host.Services.GetRequiredService<ILogger<T>>();
            }

            var sec = new SectionScope(logger, typeof(T), null, payload, TraceLogger.TraceSource, sourceLevel, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
            var stopTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var delta = stopTicks - startTicks;
            return sec;
        }

        public static ILogger<T> GetLogger<T>(this IHost host)
        {
            TraceLogger.Host = host;
            var logger = host.Services.GetRequiredService<ILogger<T>>();
            return logger;
        }
        public static void InitTraceLogger(this IHost Host)
        {
            TraceLogger.Host = Host;
            return;
        }

        //public static SectionScope BeginMethodScope(Type t, object payload = null, SourceLevels sourceLevel = SourceLevels.Verbose, LogLevel LogLevel = LogLevel.Trace, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        //{
        //    var startTicks = TraceLogger.Stopwatch.ElapsedTicks;

        //    var host = (App.Current as App).Host;
        //    var logger = host.Services.GetRequiredService<ILogger<MainWindow>>();
        //    ILogger logger

        //    var sec = new SectionScope(logger, typeof(T), null, payload, TraceLogger.TraceSource, sourceLevel, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber);
        //    var stopTicks = TraceLogger.Stopwatch.ElapsedTicks;
        //    var delta = stopTicks - startTicks;
        //    return sec;
        //}

        public static void Debug<T>(this ILogger<T> logger, object obj, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Verbose, LogLevel.Debug, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogDebug(obj, category, properties, source);
        }
        public static void Debug<T>(this ILogger<T> logger, NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Verbose, LogLevel.Debug, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogDebug(message, category, properties, source);
        }
        public static void Debug<T>(this ILogger<T> logger, FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Verbose, LogLevel.Debug, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogDebug(message, category, properties, source);
        }
        public static void Information<T>(this ILogger<T> logger, NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Information, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogInformation(message, category, properties, source);
        }
        public static void Information<T>(this ILogger<T> logger, FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Information, LogLevel.Information, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogInformation(message, category, properties, source);
        }
        public static void Warning<T>(this ILogger<T> logger, NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Warning, LogLevel.Warning, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogWarning(message, category, properties, source);
        }
        public static void Warning<T>(this ILogger<T> logger, FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Warning, LogLevel.Warning, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogWarning(message, category, properties, source);
        }
        public static void Error<T>(this ILogger<T> logger, NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Error, LogLevel.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogError(message, category, properties, source);
        }
        public static void Error<T>(this ILogger<T> logger, FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Error, LogLevel.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogError(message, category, properties, source);
        }
        public static void Exception<T>(this ILogger<T> logger, Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, [CallerMemberName] string memberName = "", [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            var startTicks = TraceLogger.Stopwatch.ElapsedTicks;
            var type = typeof(Application);
            SectionScope caller = SectionScope.Current.Value as SectionScope;
            SectionScope innerSectionScope = caller != null ? caller = caller.GetInnerScope() : caller = new SectionScope(logger, typeof(T), null, null, null, SourceLevels.Error, LogLevel.Error, category, properties, source, startTicks, memberName, sourceFilePath, sourceLineNumber, true);
            innerSectionScope.LogException(exception, category, properties, source);
        }

        public static ILoggingBuilder AddDiginsightFormatted(this ILoggingBuilder builder, ILoggerProvider logProvider, IConfiguration config = null, string configurationPrefix = null) // , IServiceProvider serviceProvider
        {
            TraceLogger.Init(config);

            var traceLoggerProvider = new TraceLoggerFormatProvider() { ConfigurationSuffix = configurationPrefix };
            traceLoggerProvider.AddProvider(logProvider);

            builder.AddProvider(traceLoggerProvider);
            return builder;
        }
        public static ILoggingBuilder AddDiginsightJson(this ILoggingBuilder builder, ILoggerProvider logProvider, IConfiguration config = null, string configurationPrefix = null) // , IServiceProvider serviceProvider
        {
            TraceLogger.Init(config);

            var traceLoggerProvider = new TraceLoggerJsonProvider() { ConfigurationSuffix = configurationPrefix };
            traceLoggerProvider.AddProvider(logProvider);

            builder.AddProvider(traceLoggerProvider);
            return builder;
        }
    }
    public static class TraceLoggerFactoryExtensions
    {
        public static ILoggerFactory AddDiginsight(this ILoggerFactory factory, IServiceProvider serviceProvider, LogLevel minLevel)
        {
            return null;
        }
    }
    public class TraceLoggerFormatProvider : ILoggerProvider, IFormatTraceEntry
    {
        #region const
        public const string CONFIGSETTING_CRREPLACE = "CRReplace"; public const string CONFIGDEFAULT_CRREPLACE = "\\r";
        public const string CONFIGSETTING_LFREPLACE = "LFReplace"; public const string CONFIGDEFAULT_LFREPLACE = "\\n";
        public const string CONFIGSETTING_TIMESTAMPFORMAT = "TimestampFormat"; public const string CONFIGDEFAULT_TIMESTAMPFORMAT = "HH:mm:ss.fff"; // dd/MM/yyyy 
        public const string CONFIGSETTING_FLUSHONWRITE = "FlushOnWrite"; public const bool CONFIGDEFAULT_FLUSHONWRITE = false;
        public const string CONFIGSETTING_SHOWNESTEDFLOW = "ShowNestedFlow"; public const bool CONFIGDEFAULT_SHOWNESTEDFLOW = false;
        public const string CONFIGSETTING_SHOWTRACECOST = "ShowTraceCost"; public const bool CONFIGDEFAULT_SHOWTRACECOST = false;
        public const string CONFIGSETTING_MAXMESSAGELEVEL = "MaxMessageLevel"; public const int CONFIGDEFAULT_MAXMESSAGELEVEL = 3;
        public const string CONFIGSETTING_MAXMESSAGELEN = "MaxMessageLen"; public const int CONFIGDEFAULT_MAXMESSAGELEN = 256;
        public const string CONFIGSETTING_MAXMESSAGELENINFO = "MaxMessageLenInfo"; public const int CONFIGDEFAULT_MAXMESSAGELENINFO = 512;
        public const string CONFIGSETTING_MAXMESSAGELENWARNING = "MaxMessageLenWarning"; public const int CONFIGDEFAULT_MAXMESSAGELENWARNING = 1024;
        public const string CONFIGSETTING_MAXMESSAGELENERROR = "MaxMessageLenError"; public const int CONFIGDEFAULT_MAXMESSAGELENERROR = -1;
        public const string CONFIGSETTING_PROCESSNAMEPADDING = "ProcessNamePadding"; public const int CONFIGDEFAULT_PROCESSNAMEPADDING = 15;
        public const string CONFIGSETTING_SOURCEPADDING = "SourcePadding"; public const int CONFIGDEFAULT_SOURCEPADDING = 5;
        public const string CONFIGSETTING_CATEGORYPADDING = "CategoryPadding"; public const int CONFIGDEFAULT_CATEGORYPADDING = 5;
        public const string CONFIGSETTING_SOURCELEVELPADDING = "SourceLevelPadding"; public const int CONFIGDEFAULT_SOURCELEVELPADDING = 11;
        public const string CONFIGSETTING_DELTAPADDING = "DeltaPadding"; public const int CONFIGDEFAULT_DELTAPADDING = 5;
        public const string CONFIGSETTING_LASTWRITECONTINUATIONENABLED = "LastWriteContinuationEnabled"; public const bool CONFIGDEFAULT_LASTWRITECONTINUATIONENABLED = false;

        public const string CONFIGSETTING_TRACEMESSAGEFORMATPREFIX = "TraceMessageFormatPrefix"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATPREFIX = "[{now}] {source} {category} {tidpid} - {sourceLevel} - {lastLineDeltaPadded} {deltaPadded} {nesting} {messageNesting}";
        public const string CONFIGSETTING_TRACEMESSAGEFORMAT = "TraceMessageFormat"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMAT = "[{now}] {source} {category} {tidpid} - {sourceLevel} - {lastLineDeltaPadded} {deltaPadded} {nesting} {messageNesting}{message}";
        public const string CONFIGSETTING_TRACEMESSAGEFORMATVERBOSE = "TraceMessageFormatVerbose"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATVERBOSE = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATINFORMATION = "TraceMessageFormatInformation"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATINFORMATION = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATWARNING = "TraceMessageFormatWarning"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATWARNING = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATERROR = "TraceMessageFormatError"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATERROR = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATCRITICAL = "TraceMessageFormatCritical"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATCRITICAL = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATSTART = "TraceMessageFormatStart"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATSTART = "[{now}] {source} {category} {tidpid} - {sourceLevel} - {lastLineDeltaPadded} {deltaPadded} {nesting} {messageNesting}{message}";
        public const string CONFIGSETTING_TRACEMESSAGEFORMATSTOP = "TraceMessageFormatStop"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATSTOP = "[{now}] {source} {category} {tidpid} - {sourceLevel} - {lastLineDeltaPadded} {deltaPadded} {nesting} {messageNesting}{message}{result}";
        public const string CONFIGSETTING_TRACEMESSAGEFORMATINLINESTOP = "TraceMessageFormatInlineStop"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATINLINESTOP = "... END ({delta} secs){result}";
        public const string CONFIGSETTING_TRACEMESSAGEFORMATSUSPEND = "TraceMessageFormatSuspend"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATSUSPEND = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATRESUME = "TraceMessageFormatResume"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATRESUME = null;
        public const string CONFIGSETTING_TRACEMESSAGEFORMATTRANSFER = "TraceMessageFormatTransfer"; public const string CONFIGDEFAULT_TRACEMESSAGEFORMATTRANSFER = null;
        #endregion
        #region internal state
        private static Type T = typeof(TraceLogger);
        private static readonly string _traceSourceName = "TraceSource";
        public static Func<string, string> CRLF2Space = (string s) => { return s?.Replace("\r", " ")?.Replace("\n", " "); };
        public static Func<string, string> CRLF2Encode = (string s) => { return s?.Replace("\r", "\\r")?.Replace("\n", "\\n"); };
        public string Name { get; set; }
        public string ConfigurationSuffix { get; set; }
        bool _lastWriteContinuationEnabled;
        public string _CRReplace, _LFReplace;
        public string _timestampFormat;
        public bool _showNestedFlow, _showTraceCost, _flushOnWrite;
        public int _processNamePadding, _sourcePadding, _categoryPadding, _sourceLevelPadding, _deltaPadding, _traceDeltaPadding, _traceMessageFormatPrefixLen;
        public string _traceMessageFormatPrefix, _traceMessageFormat, _traceMessageFormatVerbose, _traceMessageFormatInformation, _traceMessageFormatWarning, _traceMessageFormatError, _traceMessageFormatCritical;
        public string _traceMessageFormatStart, _traceMessageFormatStop, _traceMessageFormatInlineStop, _traceMessageFormatSuspend, _traceMessageFormatResume, _traceMessageFormatTransfer;
        public string _traceDeltaDefault;
        public TraceEventType _allowedEventTypes = TraceEventType.Critical | TraceEventType.Error | TraceEventType.Warning | TraceEventType.Information | TraceEventType.Verbose | TraceEventType.Start | TraceEventType.Stop | TraceEventType.Suspend | TraceEventType.Resume | TraceEventType.Transfer;
        TraceEntry lastWrite = default(TraceEntry);
        ILoggerProvider _provider;

        public static ConcurrentDictionary<string, object> Properties { get; set; } = new ConcurrentDictionary<string, object>();
        public IList<ILogger> Listeners { get; } = new List<ILogger>();
        #endregion

        public TraceLoggerFormatProvider() { }
        public TraceLoggerFormatProvider(IConfiguration configuration)
        {
            TraceLogger.Init(configuration);
        }

        public void AddProvider(ILoggerProvider provider)
        {
            using (var scope = TraceLogger.BeginMethodScope<TraceLoggerFormatProvider>())
            {
                if (string.IsNullOrEmpty(ConfigurationSuffix))
                {
                    var prefix = provider?.GetType()?.Name?.Split('.')?.Last();
                    this.ConfigurationSuffix = prefix;
                }

                _CRReplace = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_CRREPLACE, CONFIGDEFAULT_CRREPLACE, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _LFReplace = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_LFREPLACE, CONFIGDEFAULT_LFREPLACE, CultureInfo.InvariantCulture, this.ConfigurationSuffix);  // ConfigurationHelper.GetSetting<int>(CONFIGSETTING_LFREPLACE, CONFIGDEFAULT_LFREPLACE);
                _timestampFormat = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TIMESTAMPFORMAT, CONFIGDEFAULT_TIMESTAMPFORMAT, CultureInfo.InvariantCulture, this.ConfigurationSuffix);  // ConfigurationHelper.GetSetting<int>(CONFIGSETTING_TIMESTAMPFORMAT, CONFIGDEFAULT_TIMESTAMPFORMAT);
                _showNestedFlow = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, bool>(CONFIGSETTING_SHOWNESTEDFLOW, CONFIGDEFAULT_SHOWNESTEDFLOW, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _showTraceCost = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, bool>(CONFIGSETTING_SHOWTRACECOST, CONFIGDEFAULT_SHOWTRACECOST, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _flushOnWrite = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, bool>(CONFIGSETTING_FLUSHONWRITE, CONFIGDEFAULT_FLUSHONWRITE, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _processNamePadding = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, int>(CONFIGSETTING_PROCESSNAMEPADDING, CONFIGDEFAULT_PROCESSNAMEPADDING, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _sourcePadding = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, int>(CONFIGSETTING_SOURCEPADDING, CONFIGDEFAULT_SOURCEPADDING, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _categoryPadding = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, int>(CONFIGSETTING_CATEGORYPADDING, CONFIGDEFAULT_CATEGORYPADDING, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _sourceLevelPadding = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, int>(CONFIGSETTING_SOURCELEVELPADDING, CONFIGDEFAULT_SOURCELEVELPADDING, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _deltaPadding = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, int>(CONFIGSETTING_DELTAPADDING, CONFIGDEFAULT_DELTAPADDING, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _lastWriteContinuationEnabled = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, bool>(CONFIGSETTING_LASTWRITECONTINUATIONENABLED, CONFIGDEFAULT_LASTWRITECONTINUATIONENABLED, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _traceMessageFormatPrefix = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATPREFIX, CONFIGDEFAULT_TRACEMESSAGEFORMATPREFIX, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _traceMessageFormat = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMAT, CONFIGDEFAULT_TRACEMESSAGEFORMAT, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _traceMessageFormatVerbose = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATVERBOSE, CONFIGDEFAULT_TRACEMESSAGEFORMATVERBOSE, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatVerbose)) { _traceMessageFormatVerbose = _traceMessageFormat; }
                _traceMessageFormatInformation = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATINFORMATION, CONFIGDEFAULT_TRACEMESSAGEFORMATINFORMATION, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatInformation)) { _traceMessageFormatInformation = _traceMessageFormat; }
                _traceMessageFormatWarning = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATWARNING, CONFIGDEFAULT_TRACEMESSAGEFORMATWARNING, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatWarning)) { _traceMessageFormatWarning = _traceMessageFormat; }
                _traceMessageFormatError = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATERROR, CONFIGDEFAULT_TRACEMESSAGEFORMATERROR, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatError)) { _traceMessageFormatError = _traceMessageFormat; }
                _traceMessageFormatCritical = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATCRITICAL, CONFIGDEFAULT_TRACEMESSAGEFORMATCRITICAL, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatCritical)) { _traceMessageFormatCritical = _traceMessageFormat; }
                _traceMessageFormatStart = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATSTART, CONFIGDEFAULT_TRACEMESSAGEFORMATSTART, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatStart)) { _traceMessageFormatStart = _traceMessageFormat; }
                _traceMessageFormatStop = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATSTOP, CONFIGDEFAULT_TRACEMESSAGEFORMATSTOP, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatStop)) { _traceMessageFormatStop = _traceMessageFormat; }
                _traceMessageFormatInlineStop = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATINLINESTOP, CONFIGDEFAULT_TRACEMESSAGEFORMATINLINESTOP, CultureInfo.InvariantCulture, this.ConfigurationSuffix); // if (string.IsNullOrEmpty(_traceMessageFormatInlineStop)) { _traceMessageFormatInlineStop = _traceMessageFormat; }
                _traceMessageFormatSuspend = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATSUSPEND, CONFIGDEFAULT_TRACEMESSAGEFORMATSUSPEND, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatSuspend)) { _traceMessageFormatSuspend = _traceMessageFormat; }
                _traceMessageFormatResume = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATRESUME, CONFIGDEFAULT_TRACEMESSAGEFORMATRESUME, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatResume)) { _traceMessageFormatResume = _traceMessageFormat; }
                _traceMessageFormatTransfer = ConfigurationHelper.GetClassSetting<TraceLoggerFormatProvider, string>(CONFIGSETTING_TRACEMESSAGEFORMATTRANSFER, CONFIGDEFAULT_TRACEMESSAGEFORMATTRANSFER, CultureInfo.InvariantCulture, this.ConfigurationSuffix); if (string.IsNullOrEmpty(_traceMessageFormatTransfer)) { _traceMessageFormatTransfer = _traceMessageFormat; }

                var thicksPerMillisecond = TraceLogger.Stopwatch.ElapsedTicks / TraceLogger.Stopwatch.ElapsedMilliseconds;
                string fileName = null, workingDirectory = null;
                try { fileName = TraceLogger.CurrentProcess?.StartInfo?.FileName; } catch { };
                try { workingDirectory = TraceLogger.CurrentProcess.StartInfo.WorkingDirectory; } catch { };

                scope.LogInformation($"Starting TraceLoggerProvider for: ProcessName: '{TraceLogger.ProcessName}', ProcessId: '{TraceLogger.ProcessId}', FileName: '{fileName}', WorkingDirectory: '{workingDirectory}', EntryAssemblyFullName: '{TraceLogger.EntryAssembly?.FullName}', ImageRuntimeVersion: '{TraceLogger.EntryAssembly?.ImageRuntimeVersion}', Location: '{TraceLogger.EntryAssembly?.Location}', thicksPerMillisecond: '{thicksPerMillisecond}'{Environment.NewLine}"); // "init"
                // scope.LogDebug($"_filter '{_filter}', _categoryFilter '{_categoryFilter}', _allowedEventTypes '{_allowedEventTypes}', _showNestedFlow '{_showNestedFlow}', _flushOnWrite '{_flushOnWrite}', _cRReplace '{_CRReplace}', _lFReplace '{_LFReplace}', _timestampFormat '{_timestampFormat}'{Environment.NewLine}"); // "init"
                scope.LogDebug($"_processNamePadding '{_processNamePadding}', _sourcePadding '{_sourcePadding}', _categoryPadding '{_categoryPadding}', _sourceLevelPadding '{_sourceLevelPadding}'{Environment.NewLine}"); // "init"
                scope.LogDebug($"_traceMessageFormat '{_traceMessageFormat}', _traceMessageFormatVerbose '{_traceMessageFormatVerbose}', _traceMessageFormatWarning '{_traceMessageFormatWarning}', _traceMessageFormatError '{_traceMessageFormatError}', _traceMessageFormatCritical '{_traceMessageFormatCritical}', _traceMessageFormatStart '{_traceMessageFormatStart}', _traceMessageFormatStop '{_traceMessageFormatStop}, _traceMessageFormatInlineStop '{_traceMessageFormatInlineStop}'{Environment.NewLine}"); // "init"

                if (!string.IsNullOrEmpty(_traceMessageFormatPrefix))
                {
                    _traceMessageFormat = _traceMessageFormat.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatVerbose = _traceMessageFormatVerbose.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatInformation = _traceMessageFormatInformation.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatWarning = _traceMessageFormatWarning.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatError = _traceMessageFormatError.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatCritical = _traceMessageFormatCritical.Substring(_traceMessageFormatPrefix.Length);

                    _traceMessageFormatStart = _traceMessageFormatStart.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatStop = _traceMessageFormatStop.Substring(_traceMessageFormatPrefix.Length);
                    //_traceMessageFormatInlineStop = _traceMessageFormatInlineStop.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatSuspend = _traceMessageFormatSuspend.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatResume = _traceMessageFormatResume.Substring(_traceMessageFormatPrefix.Length);
                    _traceMessageFormatTransfer = _traceMessageFormatTransfer.Substring(_traceMessageFormatPrefix.Length);
                }

                int i = 0;
                var variables = "now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, result, messageNesting".Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Select((s) => new { name = s, position = i++ }).ToList();
                variables.ForEach(v =>
                {
                    _traceMessageFormatPrefix = _traceMessageFormatPrefix.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormat = _traceMessageFormat.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatVerbose = _traceMessageFormatVerbose.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatInformation = _traceMessageFormatInformation.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatWarning = _traceMessageFormatWarning.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatError = _traceMessageFormatError.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatCritical = _traceMessageFormatCritical.Replace($"{{{v.name}}}", $"{{{v.position}}}");

                    _traceMessageFormatStart = _traceMessageFormatStart.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatStop = _traceMessageFormatStop.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatInlineStop = _traceMessageFormatInlineStop.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatSuspend = _traceMessageFormatSuspend.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatResume = _traceMessageFormatResume.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                    _traceMessageFormatTransfer = _traceMessageFormatTransfer.Replace($"{{{v.name}}}", $"{{{v.position}}}");
                });
                _provider = provider;
            }
        }
        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _provider.CreateLogger(categoryName);
            var logger = new TraceLogger(this, categoryName); // TODO: use provider to get config
            logger.Listeners.Add(innerLogger);

            return logger;
        }
        public void Dispose()
        {
            ;
        }

        #region GetMaxMessageLen
        public static int? GetMaxMessageLen(SectionScope section, TraceEventType traceEventType)
        {
            var maxMessageLenSpecific = default(int?);
            switch (traceEventType)
            {
                case TraceEventType.Error:
                case TraceEventType.Critical:
                    var maxMessageLenError = section?._maxMessageLenError ?? section?.ModuleContext?.MaxMessageLenError;
                    if (maxMessageLenError == null)
                    {
                        var val = ConfigurationHelper.GetSetting("MaxMessageLenError", TraceLoggerFormatProvider.CONFIGDEFAULT_MAXMESSAGELENERROR);
                        if (val != 0) { maxMessageLenError = val; if (section != null) { section._maxMessageLenError = maxMessageLenError; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenError = maxMessageLenError; } } }
                    }
                    if (maxMessageLenError != 0) { maxMessageLenSpecific = maxMessageLenError; }
                    break;
                case TraceEventType.Warning:
                    var maxMessageLenWarning = section?._maxMessageLenWarning ?? section?.ModuleContext?.MaxMessageLenWarning;
                    if (maxMessageLenWarning == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenWarning", TraceLoggerFormatProvider.CONFIGDEFAULT_MAXMESSAGELENWARNING);
                        if (val != 0) { maxMessageLenWarning = val; if (section != null) { section._maxMessageLenWarning = maxMessageLenWarning; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenWarning = maxMessageLenWarning; } } }
                    }
                    if (maxMessageLenWarning != 0) { maxMessageLenSpecific = maxMessageLenWarning; }
                    break;
                case TraceEventType.Information:
                    var maxMessageLenInfo = section?._maxMessageLenInfo ?? section?.ModuleContext?.MaxMessageLenInfo;
                    if (maxMessageLenInfo == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenInfo", TraceLoggerFormatProvider.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenInfo = val; if (section != null) { section._maxMessageLenInfo = maxMessageLenInfo; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenInfo = maxMessageLenInfo; } } }
                    }
                    if (maxMessageLenInfo != 0) { maxMessageLenSpecific = maxMessageLenInfo; }
                    break;
                case TraceEventType.Verbose:
                    var maxMessageLenVerbose = section?._maxMessageLenVerbose ?? section?.ModuleContext?.MaxMessageLenVerbose;
                    if (maxMessageLenVerbose == null)
                    {
                        var val = ConfigurationHelper.GetSetting<int>("MaxMessageLenVerbose", TraceLoggerFormatProvider.CONFIGDEFAULT_MAXMESSAGELENINFO);
                        if (val != 0) { maxMessageLenVerbose = val; if (section != null) { section._maxMessageLenVerbose = maxMessageLenVerbose; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLenVerbose = maxMessageLenVerbose; } } }
                    }
                    if (maxMessageLenVerbose != 0) { maxMessageLenSpecific = maxMessageLenVerbose; }
                    break;
            }
            var maxMessageLen = maxMessageLenSpecific ?? section?._maxMessageLen ?? section?.ModuleContext?.MaxMessageLen;
            if (maxMessageLen == null)
            {
                maxMessageLen = ConfigurationHelper.GetSetting<int>("MaxMessageLen", TraceLoggerFormatProvider.CONFIGDEFAULT_MAXMESSAGELEN);
                if (section != null) { section._maxMessageLen = maxMessageLen; if (section.ModuleContext != null) { section.ModuleContext.MaxMessageLen = maxMessageLen; } }
            }
            if (section != null) { section._maxMessageLen = maxMessageLen; }
            return maxMessageLen;
        }
        #endregion
        public string FormatTraceEntry(TraceEntry entry, Exception ex)
        {
            var message = null as string;
            var category = "general";
            var isLastWriteContinuation = false;

            category = entry.Category;

            message = getEntryMessage(entry, lastWrite, out isLastWriteContinuation);
            message += Environment.NewLine;

            // check the global filter
            //if (isLastWriteContinuation) { sbMessages.Remove(sbMessages.Length - 2, 2); }

            lastWrite = entry;
            if (entry.Equals(default(TraceEntry))) { lastWrite.ElapsedMilliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds; }

            var traceDeltaPadded = default(string);

            var endTraceTicks = TraceLogger.Stopwatch.ElapsedTicks;
            traceDeltaPadded = (endTraceTicks - entry.TraceStartTicks).ToString("###0"); // .PadLeft(5)
            if (traceDeltaPadded != null && traceDeltaPadded.Length < _traceDeltaPadding) { traceDeltaPadded = traceDeltaPadded.PadLeft(_traceDeltaPadding); }
            if (traceDeltaPadded.Length > _traceDeltaPadding) { _traceDeltaPadding = traceDeltaPadded.Length; _traceDeltaDefault = new string(' ', _traceDeltaPadding); }

            var fullMessage = _showTraceCost ? $"{traceDeltaPadded}.{message}" : message;
            return fullMessage;
        }
        private string getEntryMessageRaw(TraceEntry entry) { return entry.GetMessage != null ? entry.GetMessage() : entry.Message; }
        public string getEntryMessage(TraceEntry entry, TraceEntry lastWrite, out bool isLastWriteContinuation)
        {
            isLastWriteContinuation = false;
            string line = null;
            string category = entry.Category ?? "general";
            var processName = TraceLogger.ProcessName + ".exe";
            var source = entry.Source ?? "unknown";
            var codeSection = entry.SectionScope;
            if (processName != null && processName.Length < _processNamePadding) { processName = processName.PadRight(_processNamePadding); }
            if (source != null && source.Length < _sourcePadding) { source = source.PadRight(_sourcePadding); }
            if (source.Length > _sourcePadding) { _sourcePadding = source.Length; }
            if (category != null && category.Length < _categoryPadding) { category = category.PadRight(_categoryPadding); }
            if (category.Length > _categoryPadding) { _categoryPadding = category.Length; }
            var sourceLevel = entry.SourceLevel.ToString();
            if (sourceLevel != null && sourceLevel.Length < _sourceLevelPadding) { sourceLevel = sourceLevel.PadRight(_sourceLevelPadding); }
            if (sourceLevel.Length > _sourceLevelPadding) { _sourceLevelPadding = sourceLevel.Length; }

            var tidpid = string.Format("{0,5} {1,4} {2}", TraceLogger.ProcessId, entry.ThreadID, entry.ApartmentState);
            var maxMessageLen = TraceLoggerFormatProvider.GetMaxMessageLen(codeSection, entry.TraceEventType);

            var message = codeSection.IsInnerScope ? "... " + getEntryMessageRaw(entry) : getEntryMessageRaw(entry);
            if (maxMessageLen > 0 && message != null && message.Length > maxMessageLen) { message = message.Substring(0, maxMessageLen.Value - 3) + "..."; }

            var nesting = getNesting(entry);
            string operationID = !string.IsNullOrEmpty(entry.RequestContext?.RequestId) ? entry.RequestContext?.RequestId : "null";
            var now = DateTime.Now.ToString(_timestampFormat);

            var delta = ""; var lastLineDelta = ""; var lastLineDeltaSB = new StringBuilder();
            var deltaPadded = ""; var lastLineDeltaPadded = "";
            var resultString = "";
            var messageNesting = _showNestedFlow ? new string(' ', entry.SectionScope != null ? entry.SectionScope.NestingLevel * 2 : 0) : "";

            lastLineDeltaPadded = lastLineDelta = getLastLineDeltaOptimized(entry, lastWrite); // .PadLeft(5)
            if (lastLineDeltaPadded != null && lastLineDeltaPadded.Length < _deltaPadding) { lastLineDeltaPadded = lastLineDeltaPadded.PadLeft(_deltaPadding); }
            if (lastLineDeltaPadded.Length > _deltaPadding) { _deltaPadding = lastLineDeltaPadded.Length; }

            var linePrefix = ""; var lineSuffix = "";
            switch (entry.TraceEventType)
            {
                case TraceEventType.Start:
                    // line = $"[{now}] {processName} {source} {category} {tidpid} - {sourceLevel} - {nesting} {message}"; "[{0}] {1} {2} {3} - {4,-11} - {5} {6}"
                    string section = !string.IsNullOrEmpty(codeSection.Name) ? string.Format(".{0}", codeSection.Name) : null;
                    message = $"{codeSection.getClassName()}.{codeSection.MemberName}{section}() START ";
                    if (codeSection.Payload != null)
                    {
                        var maxPayloadLen = maxMessageLen >= 0 ? (int)maxMessageLen - Min((int)maxMessageLen, message.Length) : -1;
                        var payloadString = $"{codeSection.Payload.GetLogString()}";
                        if (payloadString.Length > maxPayloadLen)
                        {
                            var deltaLen = maxPayloadLen > 3 ? maxPayloadLen - 3 : maxPayloadLen;
                            if (deltaLen > 0) { payloadString = payloadString.Substring(0, deltaLen) + "..."; }
                        }
                        message = $"{codeSection.getClassName()}.{codeSection.MemberName}{section}({payloadString}) START ";
                    }

                    deltaPadded = delta = ""; // .PadLeft(5)
                    if (deltaPadded != null && deltaPadded.Length < _deltaPadding) { deltaPadded = deltaPadded.PadLeft(_deltaPadding); }
                    if (deltaPadded.Length > _deltaPadding) { _deltaPadding = deltaPadded.Length; }

                    if (!string.IsNullOrEmpty(_traceMessageFormatPrefix)) { linePrefix = string.Format(_traceMessageFormatPrefix, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    if (!string.IsNullOrEmpty(_traceMessageFormatStart)) { lineSuffix = string.Format(_traceMessageFormatStart, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    line = $"{linePrefix}{lineSuffix}";
                    break;
                case TraceEventType.Stop:
                    // line = $"[{now}] {processName} {source} {category} {tidpid} - {sourceLevel} - {nesting} {message}"; "[{0}] {1} {2} {3} - {4,-11} - {5} {6} ({7} secs)"
                    section = !string.IsNullOrEmpty(codeSection.Name) ? string.Format(".{0}", codeSection.Name) : null;
                    message = string.Format("{0}.{1}{2}() END", !string.IsNullOrWhiteSpace(codeSection.getClassName()) ? codeSection.getClassName() : string.Empty, codeSection.MemberName, section);
                    if (codeSection.Result != null)
                    {
                        var maxResultLen = maxMessageLen >= 0 ? (int)maxMessageLen - Min((int)maxMessageLen, message.Length) : -1;
                        resultString = $" returned {codeSection.Result.GetLogString()}";
                        if (resultString.Length > maxResultLen)
                        {
                            var deltaLen = maxResultLen > 3 ? maxResultLen - 13 : maxResultLen;
                            if (deltaLen > 0) { resultString = resultString.Substring(0, deltaLen) + "..."; }
                        }
                    }

                    var milliseconds = TraceLogger.Stopwatch.ElapsedMilliseconds - codeSection.CallStartMilliseconds;
                    deltaPadded = delta = ((float)milliseconds / 1000).ToString("###0.00"); // .PadLeft(5)
                    if (deltaPadded != null && deltaPadded.Length < _deltaPadding) { deltaPadded = deltaPadded.PadLeft(_deltaPadding); }
                    if (deltaPadded.Length > _deltaPadding) { _deltaPadding = deltaPadded.Length; }

                    var traceMessageFormat = _traceMessageFormatStop;
                    if (_lastWriteContinuationEnabled == true && lastWrite.SectionScope == codeSection && lastWrite.TraceEventType == TraceEventType.Start)
                    {
                        isLastWriteContinuation = true;
                        traceMessageFormat = _traceMessageFormatInlineStop;
                    }

                    if (!string.IsNullOrEmpty(_traceMessageFormatPrefix)) { linePrefix = string.Format(_traceMessageFormatPrefix, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    if (!string.IsNullOrEmpty(traceMessageFormat)) { lineSuffix = string.Format(traceMessageFormat, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    line = $"{linePrefix}{lineSuffix}";
                    break;
                default: // case TraceEntryType.Message:
                    // line = $"[{now}] {processName} {source} {category} {tidpid} - {sourceLevel} - {nesting}   {message}"; "[{0}] {1} {2} {3} - {4,-11} - {5}   {6}"
                    deltaPadded = delta = ""; // .PadLeft(5)
                    if (deltaPadded != null && deltaPadded.Length < _deltaPadding) { deltaPadded = deltaPadded.PadLeft(_deltaPadding); }
                    if (deltaPadded.Length > _deltaPadding) { _deltaPadding = deltaPadded.Length; }
                    if (_showNestedFlow) { messageNesting += "  "; }
                    if (!string.IsNullOrEmpty(_traceMessageFormatPrefix)) { linePrefix = string.Format(_traceMessageFormatPrefix, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    if (!string.IsNullOrEmpty(_traceMessageFormatInformation)) { lineSuffix = string.Format(_traceMessageFormatInformation, now, processName, source, category, tidpid, sourceLevel, nesting, message, lastLineDelta, lastLineDeltaPadded, delta, deltaPadded, resultString, messageNesting); }
                    line = $"{linePrefix}{lineSuffix}";
                    break;
            }

            if (!entry.DisableCRLFReplace)
            {
                if (line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0)
                {
                    if (!string.IsNullOrEmpty(_CRReplace)) { line = line?.Replace("\r", _CRReplace); }
                    if (!string.IsNullOrEmpty(_LFReplace)) { line = line?.Replace("\n", _LFReplace); }
                }
            }
            else
            {
                var prefixFill = new string(' ', linePrefix.Length);
                if (line.IndexOf('\n') >= 0) { line = line?.Replace("\n", $"\n{prefixFill}"); }
            }
            return line;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string getNesting(TraceEntry entry)
        {
            var requestInfo = entry.RequestContext;
            var dept = requestInfo != null ? requestInfo.RequestDept : 0;

            var section = entry.SectionScope;
            var showNestedFlow = _showNestedFlow;
            string deptString = $"{dept}.{(section != null ? section.NestingLevel : 0)}".PadLeftExact(4, ' ');
            // if (showNestedFlow == false) { return deptString; }

            return deptString;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string getLastLineDeltaOptimized(TraceEntry entry, TraceEntry lastWrite)
        {
            var milliseconds = entry.ElapsedMilliseconds - lastWrite.ElapsedMilliseconds;
            if (milliseconds <= 0) { return ""; }

            var seconds = (float)milliseconds / 1000; // .PadLeft(5) 
            int minutes = 0, hours = 0, days = 0; // months = 0, years = 0;
            string lastLineDelta = null;

            if (seconds < 0.005) { return ""; }
            if (seconds < 60) { lastLineDelta = seconds.ToString("#0.00"); return lastLineDelta; }

            minutes = ((int)seconds / 60);
            seconds = seconds % 60;
            lastLineDelta = minutes >= 60 ? $"{seconds:00.00}" : $"{seconds:#0.00}";

            var lastLineDeltaSB = new StringBuilder(lastLineDelta);
            if (minutes >= 60)
            {
                hours = ((int)seconds / 60);
                minutes = minutes % 60;
                lastLineDeltaSB.Insert(0, hours >= 24 ? $"{minutes:00}:" : $"{minutes:#0}:");
            }
            else if (minutes > 0) { lastLineDeltaSB.Insert(0, $"{minutes:#0}:"); }
            if (hours >= 24)
            {
                days = ((int)hours / 24);
                hours = hours % 24;
                lastLineDeltaSB.Insert(0, $"{hours:#0}:");
            }
            else if (hours > 0) { lastLineDeltaSB.Insert(0, $"{hours:#0}:"); }
            if (days > 0)
            {
                lastLineDeltaSB.Insert(0, $"{days:#.##0}:");
            }
            return lastLineDeltaSB.ToString();
        }
        #region Min
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Min(int a, int b) { return a < b ? a : b; }
        #endregion
    }
    public class TraceLoggerJsonProvider : ILoggerProvider, IFormatTraceEntry
    {
        #region const
        public const string CONFIGSETTING_CRREPLACE = "CRReplace"; public const string CONFIGDEFAULT_CRREPLACE = "\\r";
        public const string CONFIGSETTING_LFREPLACE = "LFReplace"; public const string CONFIGDEFAULT_LFREPLACE = "\\n";
        public const string CONFIGSETTING_TIMESTAMPFORMAT = "TimestampFormat"; public const string CONFIGDEFAULT_TIMESTAMPFORMAT = "HH:mm:ss.fff"; // dd/MM/yyyy 
        public const string CONFIGSETTING_FLUSHONWRITE = "FlushOnWrite"; public const bool CONFIGDEFAULT_FLUSHONWRITE = false;
        #endregion
        #region internal state
        private static Type T = typeof(TraceLogger);
        private static readonly string _traceSourceName = "TraceSource";
        public static Func<string, string> CRLF2Space = (string s) => { return s?.Replace("\r", " ")?.Replace("\n", " "); };
        public static Func<string, string> CRLF2Encode = (string s) => { return s?.Replace("\r", "\\r")?.Replace("\n", "\\n"); };
        public string Name { get; set; }
        public string ConfigurationSuffix { get; set; }
        bool _lastWriteContinuationEnabled;
        public string _CRReplace, _LFReplace;
        public string _timestampFormat;
        public bool _showNestedFlow, _showTraceCost, _flushOnWrite;
        public string _traceDeltaDefault;
        public TraceEventType _allowedEventTypes = TraceEventType.Critical | TraceEventType.Error | TraceEventType.Warning | TraceEventType.Information | TraceEventType.Verbose | TraceEventType.Start | TraceEventType.Stop | TraceEventType.Suspend | TraceEventType.Resume | TraceEventType.Transfer;
        TraceEntry lastWrite = default(TraceEntry);
        ILoggerProvider _provider;

        public static ConcurrentDictionary<string, object> Properties { get; set; } = new ConcurrentDictionary<string, object>();
        public IList<ILogger> Listeners { get; } = new List<ILogger>();
        #endregion

        public TraceLoggerJsonProvider() { }
        public TraceLoggerJsonProvider(IConfiguration configuration)
        {
            TraceLogger.Init(configuration);
        }

        public void AddProvider(ILoggerProvider provider)
        {
            using (var scope = TraceLogger.BeginMethodScope<TraceLoggerJsonProvider>())
            {
                if (string.IsNullOrEmpty(ConfigurationSuffix))
                {
                    var prefix = provider?.GetType()?.Name?.Split('.')?.Last();
                    this.ConfigurationSuffix = prefix;
                }

                _CRReplace = ConfigurationHelper.GetClassSetting<TraceLoggerJsonProvider, string>(CONFIGSETTING_CRREPLACE, CONFIGDEFAULT_CRREPLACE, CultureInfo.InvariantCulture, this.ConfigurationSuffix);
                _LFReplace = ConfigurationHelper.GetClassSetting<TraceLoggerJsonProvider, string>(CONFIGSETTING_LFREPLACE, CONFIGDEFAULT_LFREPLACE, CultureInfo.InvariantCulture, this.ConfigurationSuffix);  // ConfigurationHelper.GetSetting<int>(CONFIGSETTING_LFREPLACE, CONFIGDEFAULT_LFREPLACE);
                _timestampFormat = ConfigurationHelper.GetClassSetting<TraceLoggerJsonProvider, string>(CONFIGSETTING_TIMESTAMPFORMAT, CONFIGDEFAULT_TIMESTAMPFORMAT, CultureInfo.InvariantCulture, this.ConfigurationSuffix);  // ConfigurationHelper.GetSetting<int>(CONFIGSETTING_TIMESTAMPFORMAT, CONFIGDEFAULT_TIMESTAMPFORMAT);

                var thicksPerMillisecond = TraceLogger.Stopwatch.ElapsedTicks / TraceLogger.Stopwatch.ElapsedMilliseconds;
                string fileName = null, workingDirectory = null;
                try { fileName = TraceLogger.CurrentProcess?.StartInfo?.FileName; } catch { };
                try { workingDirectory = TraceLogger.CurrentProcess.StartInfo.WorkingDirectory; } catch { };

                scope.LogInformation($"Starting TraceLoggerProvider for: ProcessName: '{TraceLogger.ProcessName}', ProcessId: '{TraceLogger.ProcessId}', FileName: '{fileName}', WorkingDirectory: '{workingDirectory}', EntryAssemblyFullName: '{TraceLogger.EntryAssembly?.FullName}', ImageRuntimeVersion: '{TraceLogger.EntryAssembly?.ImageRuntimeVersion}', Location: '{TraceLogger.EntryAssembly?.Location}', thicksPerMillisecond: '{thicksPerMillisecond}'{Environment.NewLine}"); // "init"
                _provider = provider;
            }
        }
        public ILogger CreateLogger(string categoryName)
        {
            var innerLogger = _provider.CreateLogger(categoryName);
            var logger = new TraceLogger(this, categoryName); // TODO: use provider to get config
            logger.Listeners.Add(innerLogger);

            return logger;
        }
        public void Dispose()
        {
            ;
        }

        public string FormatTraceEntry(TraceEntry entry, Exception ex)
        {
            var traceSurrogate = GetTraceSurrogate(entry);
            var entryJson = SerializationHelper.SerializeJson(traceSurrogate);
            return entryJson;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TraceEntrySurrogate GetTraceSurrogate(TraceEntry entry)
        {
            var codeSection = entry.SectionScope;
            return new TraceEntrySurrogate()
            {
                TraceEventType = entry.TraceEventType,
                TraceSourceName = entry.TraceSource?.Name,
                Message = entry.Message,
                Properties = entry.Properties,
                Source = entry.Source,
                Category = entry.Category,
                SourceLevel = entry.SourceLevel,
                ElapsedMilliseconds = entry.ElapsedMilliseconds,
                Timestamp = entry.Timestamp,
                Exception = entry.Exception,
                ThreadID = entry.ThreadID,
                ApartmentState = entry.ApartmentState,
                DisableCRLFReplace = entry.DisableCRLFReplace,
                CodeSection = codeSection != null ? new SectionScopeSurrogate()
                {
                    NestingLevel = codeSection.NestingLevel,
                    OperationDept = codeSection.OperationDept,
                    Payload = codeSection.Payload,
                    Result = codeSection.Result,
                    Name = codeSection.Name,
                    MemberName = codeSection.MemberName,
                    SourceFilePath = codeSection.SourceFilePath,
                    SourceLineNumber = codeSection.SourceLineNumber,
                    DisableStartEndTraces = codeSection.DisableStartEndTraces,
                    TypeName = codeSection.T?.Name,
                    TypeFullName = codeSection.T?.FullName,
                    AssemblyName = codeSection.Assembly?.GetName()?.Name,
                    AssemblyFullName = codeSection.Assembly?.FullName,
                    TraceSourceName = codeSection.TraceSource?.Name,
                    TraceEventType = codeSection.TraceEventType,
                    SourceLevel = codeSection.SourceLevel,
                    Properties = codeSection.Properties,
                    Source = codeSection.Source,
                    Category = codeSection.Category,
                    CallStartMilliseconds = codeSection.CallStartMilliseconds,
                    SystemStartTime = codeSection.SystemStartTime,
                    OperationID = codeSection.OperationID,
                    IsInnerScope = codeSection.IsInnerScope
                } : null
            };
        }
        #region Min
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Min(int a, int b) { return a < b ? a : b; }
        #endregion
    }
}
