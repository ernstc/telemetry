#region using
using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights.Extensibility;
using System.Reflection;
using System.IO;
using Microsoft.ApplicationInsights.DataContracts;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
#endregion

namespace Common
{
    public class AppInsightsTraceListener : TraceListener, ISupportFilters
    {
        #region const
        public const string FILTER_SPLIT_REGEX = @"([-!]?[\""].+?[\""])|([-!]?\w+)";
        public const string CONFIGSETTING_FILTER = "Filter"; public const string CONFIGDEFAULT_FILTER = null;
        public const string CONFIGSETTING_CATEGORYFILTER = "CategoryFilter"; public const string CONFIGDEFAULT_CATEGORYFILTER = null;
        public const string CONFIGSETTING_FLUSHONWRITE = "FlushOnWrite"; public const bool CONFIGDEFAULT_FLUSHONWRITE = false;
        public const string CONFIGSETTING_MAXMESSAGELEVEL = "MaxMessageLevel"; public const int CONFIGDEFAULT_MAXMESSAGELEVEL = 3;
        public const string CONFIGSETTING_MAXMESSAGELEN = "MaxMessageLen"; public const int CONFIGDEFAULT_MAXMESSAGELEN = 256;
        public const string CONFIGSETTING_MAXMESSAGELENINFO = "MaxMessageLenInfo"; public const int CONFIGDEFAULT_MAXMESSAGELENINFO = 512;
        public const string CONFIGSETTING_MAXMESSAGELENWARNING = "MaxMessageLenWarning"; public const int CONFIGDEFAULT_MAXMESSAGELENWARNING = 1024;
        public const string CONFIGSETTING_MAXMESSAGELENERROR = "MaxMessageLenError"; public const int CONFIGDEFAULT_MAXMESSAGELENERROR = -1;

        private const string CONFIGSETTING_APPINSIGHTSKEY = "AppInsightsKey"; private const string CONFIGDEFAULT_APPINSIGHTSKEY = "";
        private const string CONFIGSETTING_TELEMETRYTHREADSLEEP = "TelemetryThreadSleep"; private const int CONFIGDEFAULT_TELEMETRYTHREADSLEEP = 0;
        private const string CONFIGSETTING_DEFAULTCATEGORY = "DefaultCategory"; private const string CONFIGDEFAULT_DEFAULTCATEGORY = "";
        private const string CONFIGSETTING_TRACKEXCEPTIONENABLED = "TrackExceptionEnabled"; private const bool CONFIGDEFAULT_TRACKEXCEPTIONENABLED = true;
        private const string CONFIGSETTING_TRACKTRACEENABLED = "TrackTraceEnabled"; private const bool CONFIGDEFAULT_TRACKTRACEENABLED = true;
        private const string CONFIGSETTING_TRACKEVENTENABLED = "TrackEventEnabled"; private const bool CONFIGDEFAULT_TRACKEVENTENABLED = false;
        #endregion

        #region private fields
        public bool _showNestedFlow, _flushOnWrite;
        public int _traceMessageFormatPrefixLen;
        public string _filter, _categoryFilter;
        public TraceEventType _allowedEventTypes = TraceEventType.Critical | TraceEventType.Error | TraceEventType.Warning | TraceEventType.Information | TraceEventType.Verbose | TraceEventType.Start | TraceEventType.Stop | TraceEventType.Suspend | TraceEventType.Resume | TraceEventType.Transfer;
        private int _telemetrythreadsleep;
        private string _defaultCategory;
        private readonly int _timeout = 10;
        private string _appInsightsKey;
        private TelemetryClient _telemetry;
        TraceEntry lastWrite = default(TraceEntry);

        private bool _trackTraceEnabled, _trackExceptionEnabled, _trackEventEnabled;
        #endregion

        static AppInsightsTraceListener()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly == null) { assembly = Assembly.GetExecutingAssembly(); }
        }
        public AppInsightsTraceListener() : base()
        {
            Init();
        }
        public AppInsightsTraceListener(string userId, string deviceId) : this()
        {
            _telemetry.Context.User.Id = userId ?? string.Empty;
            _telemetry.Context.Device.Id = deviceId ?? string.Empty;
        }
        //public AppInsightsTraceListener() { Init(); }
        public AppInsightsTraceListener(Stream stream) : base() { Init(); }
        public AppInsightsTraceListener(TextWriter writer) { Init(); }
        public AppInsightsTraceListener(string fileName) : base() { Init(); }
        public AppInsightsTraceListener(Stream stream, string name) { Init(); }
        public AppInsightsTraceListener(TextWriter writer, string name) { Init(); }
        //public AppInsightsTraceListener(string fileName, string name) { Init(); }

        //#region Properties
        //public IList<KeyValuePair<string, object>> Metrics { get; private set; }
        //public IDictionary<string, string> Properties { get; set; }
        //#endregion

        #region Filter
        Regex _filterRegex = new Regex(FILTER_SPLIT_REGEX, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        string[] _filterCanInclude;
        string[] _filterMustInclude;
        string[] _filterMustExclude;
        string ISupportFilters.Filter
        {
            get { return _filter; }
            set
            {
                _filter = value;
                if (string.IsNullOrEmpty(_filter)) { _filterCanInclude = null; _filterMustInclude = null; _filterMustExclude = null; }
                var filterCanInclude = new List<string>();
                var filterMustInclude = new List<string>();
                var filterMustExclude = new List<string>();
                var items = _filterRegex.Split(_filter);
                items.ForEach(i =>
                {
                    if (i.StartsWith("!")) { var substr = i.Substring(1); if (!string.IsNullOrEmpty(substr)) { if (substr.IndexOf(' ') >= 0) { substr = substr.Trim('"'); } filterMustInclude.Add(substr); } return; }
                    if (i.StartsWith("-")) { var substr = i.Substring(1); if (!string.IsNullOrEmpty(substr)) { if (substr.IndexOf(' ') >= 0) { substr = substr.Trim('"'); } filterMustExclude.Add(substr); } return; }

                    if (i.IndexOf(' ') >= 0) { i = i.Trim('"'); }
                    if (!string.IsNullOrEmpty(i)) filterCanInclude.Add(i);
                });
                _filterCanInclude = filterCanInclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                _filterMustInclude = filterMustInclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                _filterMustExclude = filterMustExclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            }
        }
        #endregion
        #region CategoryFilter
        Regex _categoryFilterRegex = new Regex(FILTER_SPLIT_REGEX, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        string[] _categoryFilterCanInclude;
        string[] _categoryFilterMustInclude;
        string[] _categoryFilterMustExclude;
        string CategoryFilter
        {
            get { return _categoryFilter; }
            set
            {
                _categoryFilter = value;
                if (string.IsNullOrEmpty(_categoryFilter)) { _categoryFilterCanInclude = null; _categoryFilterMustInclude = null; _categoryFilterMustExclude = null; }
                var categoryFilterCanInclude = new List<string>();
                var categoryFilterMustInclude = new List<string>();
                var categoryFilterMustExclude = new List<string>();
                var items = _categoryFilterRegex.Split(_categoryFilter);
                items.ForEach(i =>
                {
                    if (i.StartsWith("!")) { var substr = i.Substring(1); if (!string.IsNullOrEmpty(substr)) { if (substr.IndexOf(' ') >= 0) { substr = substr.Trim('"'); } categoryFilterMustInclude.Add(substr); } return; }
                    if (i.StartsWith("-")) { var substr = i.Substring(1); if (!string.IsNullOrEmpty(substr)) { if (substr.IndexOf(' ') >= 0) { substr = substr.Trim('"'); } categoryFilterMustExclude.Add(substr); } return; }

                    if (i.IndexOf(' ') >= 0) { i = i.Trim('"'); }
                    if (!string.IsNullOrEmpty(i)) categoryFilterCanInclude.Add(i);
                });
                _categoryFilterCanInclude = categoryFilterCanInclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                _categoryFilterMustInclude = categoryFilterMustInclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                _categoryFilterMustExclude = categoryFilterMustExclude.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            }
        }
        #endregion

        private void Init()
        {
            using (var sec = this.GetCodeSection())
            {
                var filter = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, string>(CONFIGSETTING_FILTER, CONFIGDEFAULT_FILTER);
                if (!string.IsNullOrEmpty(filter)) { ((ISupportFilters)this).Filter = filter; }
                var categoryFilter = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, string>(CONFIGSETTING_CATEGORYFILTER, CONFIGDEFAULT_CATEGORYFILTER);
                if (!string.IsNullOrEmpty(categoryFilter)) { this.CategoryFilter = categoryFilter; }

                _flushOnWrite = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, bool>(CONFIGSETTING_FLUSHONWRITE, CONFIGDEFAULT_FLUSHONWRITE);
                _telemetrythreadsleep = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, int>(CONFIGSETTING_TELEMETRYTHREADSLEEP, CONFIGDEFAULT_TELEMETRYTHREADSLEEP);
                _defaultCategory = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, string>(CONFIGSETTING_DEFAULTCATEGORY, CONFIGDEFAULT_DEFAULTCATEGORY);
                _appInsightsKey = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, string>(CONFIGSETTING_APPINSIGHTSKEY, CONFIGDEFAULT_APPINSIGHTSKEY);

                _trackTraceEnabled = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, bool>(CONFIGSETTING_TRACKTRACEENABLED, CONFIGDEFAULT_TRACKTRACEENABLED);
                _trackExceptionEnabled = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, bool>(CONFIGSETTING_TRACKEXCEPTIONENABLED, CONFIGDEFAULT_TRACKEXCEPTIONENABLED);
                _trackEventEnabled = ConfigurationHelper.GetClassSetting<AppInsightsTraceListener, bool>(CONFIGSETTING_TRACKEVENTENABLED, CONFIGDEFAULT_TRACKEVENTENABLED);

                var configuration = TelemetryConfiguration.CreateDefault();
                configuration.InstrumentationKey = _appInsightsKey;
                _telemetry = new TelemetryClient(configuration);
                //this.Metrics = new List<KeyValuePair<string, object>>();
                //this.Properties = new Dictionary<string, object>();

                var thicksPerMillisecond = TraceManager.Stopwatch.ElapsedTicks / TraceManager.Stopwatch.ElapsedMilliseconds;
                string fileName = null, workingDirectory = null;
                try { fileName = TraceManager.CurrentProcess?.StartInfo?.FileName; } catch { };
                try { workingDirectory = TraceManager.CurrentProcess.StartInfo.WorkingDirectory; } catch { };

                sec.Information($"Starting AppInsightsTraceListener for: ProcessName: '{TraceManager.ProcessName}', ProcessId: '{TraceManager.ProcessId}', FileName: '{fileName}', WorkingDirectory: '{workingDirectory}', EntryAssemblyFullName: '{TraceManager.EntryAssembly?.FullName}', ImageRuntimeVersion: '{TraceManager.EntryAssembly?.ImageRuntimeVersion}', Location: '{TraceManager.EntryAssembly?.Location}', thicksPerMillisecond: '{thicksPerMillisecond}'{Environment.NewLine}"); // "init"
                sec.Debug($"_filter '{_filter}', _categoryFilter '{_categoryFilter}', _allowedEventTypes '{_allowedEventTypes}', _flushOnWrite '{_flushOnWrite}', _trackTraceEnabled '{_trackTraceEnabled}', _trackExceptionEnabled '{_trackExceptionEnabled}', _trackEventEnabled '{_trackEventEnabled}'{Environment.NewLine}"); // "init"
            }
        }

        #region Trace Methods
        //private void TraceMetric(string name, object value)
        //{
        //    if (string.IsNullOrWhiteSpace(name) || value == null)
        //        return;
        //    // For internal purposes.
        //    // this.Metrics.Add(new KeyValuePair<string, object>(name, value));
        //    // Gets or creates a metric.
        //    // TODO detail metrics
        //    _telemetry.GetMetric(name).TrackValue(1);
        //}
        //private void TraceAllMetricValues()
        //{
        //    this.Metrics.ForEach((m) =>
        //    {
        //        _telemetry.GetMetric(m.Key).TrackValue(m.Value);
        //    });
        //}
        //private void TraceException(Exception exception, IDictionary<string, string> properties, bool useMetrics = false)
        //{
        //    if (exception != null)
        //        _telemetry.TrackException(exception, properties, useMetrics ? this.Metrics as IDictionary<string, double> : null);
        //}

        #endregion Trace Methods
        public override void Write(string s) { WriteLine((object)s); }
        public override void Write(object o)
        {
            var entries = new List<object>();
            if (o is IEnumerable<object>) { entries.AddRange((o as IEnumerable<object>)); }
            if (o is TraceEntry) { entries.Add((TraceEntry)o); }
            if (o is string) { entries.Add(o as string); }

            var a = entries.OfTypeChecked<object>().ForEach(e =>
            {
                var entry = default(TraceEntry);
                var message = e as string;
                var category = default(string);

                if (e is TraceEntry)
                {
                    entry = (TraceEntry)e;
                    if (!_allowedEventTypes.HasFlag(entry.TraceEventType)) { return; }
                    category = entry.Category;
                }
                if (base.Filter != null && !base.Filter.ShouldTrace(null, null, entry.TraceEventType != 0 ? entry.TraceEventType : TraceEventType.Verbose, 0, null, null, null, null)) { return; }

                // check the category filter
                if (_categoryFilterMustInclude != null && _categoryFilterMustInclude.Length > 0 && _categoryFilterMustInclude.Any(categoryFilterMustInclude => category == null || category.IndexOf(categoryFilterMustInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                if (_categoryFilterMustExclude != null && _categoryFilterMustExclude.Length > 0 && _categoryFilterMustExclude.Any(categoryFilterMustExclude => category != null && category.IndexOf(categoryFilterMustExclude, StringComparison.CurrentCultureIgnoreCase) >= 0)) { return; }
                if (_categoryFilterCanInclude != null && _categoryFilterCanInclude.Length > 0)
                {
                    if (_categoryFilterCanInclude.All(categoryFilterCanInclude => category == null || category.IndexOf(categoryFilterCanInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                }

                message = entry.Message;
                message += Environment.NewLine;
                lastWrite = entry;
                entry.Message = message;

                // check the global filter
                if (_filterMustInclude != null && _filterMustInclude.Length > 0 && _filterMustInclude.Any(filterMustInclude => message == null || message.IndexOf(filterMustInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                if (_filterMustExclude != null && _filterMustExclude.Length > 0 && _filterMustExclude.Any(filterMustExclude => message != null && message.IndexOf(filterMustExclude, StringComparison.CurrentCultureIgnoreCase) >= 0)) { return; }
                if (_filterCanInclude != null && _filterCanInclude.Length > 0)
                {
                    if (_filterCanInclude.All(filterCanInclude => message == null || message.IndexOf(filterCanInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                }

                var args = new OnWriteArgs();
                var properties = entry.Properties != null ? new Dictionary<string, string>(entry.Properties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString())) : new Dictionary<string, string>();
                //var properties = new Dictionary<string, object>(entry.Properties);
                properties.Add("Entry.Category", entry.Category);
                properties.Add("Entry.ElapsedMilliseconds", entry.ElapsedMilliseconds.ToString());
                //properties.Add("Entry.Exception", entry.Exception?.ToString());
                properties.Add("Entry.Message", entry.Message);
                properties.Add("Entry.Source", entry.Source);
                properties.Add("Entry.SourceLevel", entry.SourceLevel.ToString());
                properties.Add("Entry.Thread", entry.Thread?.ToString());
                properties.Add("Entry.ThreadID", entry.ThreadID.ToString());
                properties.Add("Entry.Timestamp", entry.Timestamp.ToString());
                properties.Add("Entry.TraceEventType", entry.TraceEventType.ToString());
                properties.Add("Entry.TraceSource", entry.TraceSource?.ToString());
                properties.Add("CodeSection.Name", entry.CodeSection?.Name);
                properties.Add("CodeSection.Type.Name", entry.CodeSection?.T?.Name);
                properties.Add("Assembly.Name", entry.CodeSection?.Assembly?.GetName()?.Name);
                properties.Add("CodeSection.NestingLevel", entry.CodeSection?.NestingLevel.ToString());
                properties.Add("CodeSection.OperationDept", entry.CodeSection?.OperationDept.ToString());
                properties.Add("CodeSection.OperationID", entry.CodeSection?.OperationID?.ToString());
                //properties.Add("Entry.ProcessInfo", entry.ProcessInfo);
                //properties.Add("CodeSection.Source", entry.CodeSection?.Source);
                //properties.Add("CodeSection.Payload", entry.CodeSection.Payload);
                //properties.Add("CodeSection.Result", entry.CodeSection.Result);
                //properties.Add("CodeSection.SourceFilePath", entry.CodeSection?.SourceFilePath);
                //properties.Add("CodeSection.SourceLevel", entry.CodeSection?.SourceLevel.ToString());
                //properties.Add("CodeSection.SourceLineNumber", entry.CodeSection?.SourceLineNumber.ToString());

                OnWrite(args);

                if (_trackEventEnabled)
                {
                    var eventName = entry.TraceEventType.ToString();
                    _telemetry.TrackEvent(eventName, properties, null); // this.Metrics as IDictionary<string, double>
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
                if (_trackExceptionEnabled && entry.TraceEventType == TraceEventType.Critical)
                {
                    _telemetry.TrackException(entry.Exception, properties, null);
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
                if (_trackTraceEnabled)
                {
                    var traceSurrogate = GetTraceSurrogate(entry);
                    var entryJson = SerializationHelper.SerializeJson(traceSurrogate);
                    var securityLevel = SeverityLevel.Verbose;
                    if (entry.TraceEventType.HasFlag(TraceEventType.Critical)) { securityLevel = SeverityLevel.Critical; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Error)) { securityLevel = SeverityLevel.Error; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Warning)) { securityLevel = SeverityLevel.Warning; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Information)) { securityLevel = SeverityLevel.Information; }
                    _telemetry.TrackTrace(entryJson, securityLevel, properties);
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
            });
        }

        public override void Flush()
        {
            base.Flush();
            _telemetry?.Flush();
            if (_telemetrythreadsleep > 0)
                Thread.Sleep(_telemetrythreadsleep);
        }

        public override void WriteLine(string s) { WriteLine((object)s); }

        public override void WriteLine(object o)
        {
            var entries = new List<object>();
            if (o is IEnumerable<object>) { entries.AddRange((o as IEnumerable<object>)); }
            if (o is TraceEntry) { entries.Add((TraceEntry)o); }
            if (o is string) { entries.Add(o as string); }

            var a = entries.OfTypeChecked<object>().ForEach(e =>
            {
                var entry = default(TraceEntry);
                var message = e as string;
                var category = default(string);
                if (e is TraceEntry)
                {
                    entry = (TraceEntry)e;
                    if (!_allowedEventTypes.HasFlag(entry.TraceEventType)) { return; }
                    category = entry.Category;
                }
                if (base.Filter != null && !base.Filter.ShouldTrace(null, null, entry.TraceEventType != 0 ? entry.TraceEventType : TraceEventType.Verbose, 0, null, null, null, null)) { return; }

                // check the category filter
                if (_categoryFilterMustInclude != null && _categoryFilterMustInclude.Length > 0 && this._categoryFilterMustInclude.Any(categoryFilterMustInclude => category == null || category.IndexOf(categoryFilterMustInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                if (_categoryFilterMustExclude != null && _categoryFilterMustExclude.Length > 0 && this._categoryFilterMustExclude.Any(categoryFilterMustExclude => category != null && category.IndexOf(categoryFilterMustExclude, StringComparison.CurrentCultureIgnoreCase) >= 0)) { return; }
                if (_categoryFilterCanInclude != null && _categoryFilterCanInclude.Length > 0)
                {
                    if (category == null || this._categoryFilterCanInclude.All(categoryFilterCanInclude => category.IndexOf(categoryFilterCanInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                }

                message = entry.Message;
                lastWrite = entry;

                // check the global filter
                if (_filterMustInclude != null && _filterMustInclude.Length > 0 && this._filterMustInclude.Any(filterMustInclude => message == null || message.IndexOf(filterMustInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                if (_filterMustExclude != null && _filterMustExclude.Length > 0 && this._filterMustExclude.Any(filterMustExclude => message != null && message.IndexOf(filterMustExclude, StringComparison.CurrentCultureIgnoreCase) >= 0)) { return; }
                if (_filterCanInclude != null && _filterCanInclude.Length > 0)
                {
                    if (message == null || this._filterCanInclude.All(filterCanInclude => message.IndexOf(filterCanInclude, StringComparison.CurrentCultureIgnoreCase) < 0)) { return; }
                }

                var args = new OnWriteArgs();
                var properties = entry.Properties != null ? new Dictionary<string, string>(entry.Properties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString())) : new Dictionary<string, string>();
                //var properties = new Dictionary<string, object>(entry.Properties);
                args.Properties = properties;
                properties.Add("Entry.Category", entry.Category);
                properties.Add("Entry.ElapsedMilliseconds", entry.ElapsedMilliseconds.ToString());
                //properties.Add("Entry.Exception", entry.Exception?.ToString());
                //properties.Add("Entry.Message", entry.Message);
                properties.Add("Entry.Source", entry.Source);
                properties.Add("Entry.SourceLevel", entry.SourceLevel.ToString());
                properties.Add("Entry.Thread", entry.Thread?.ToString());
                properties.Add("Entry.ThreadID", entry.ThreadID.ToString());
                properties.Add("Entry.Timestamp", entry.Timestamp.ToString());
                properties.Add("Entry.TraceEventType", entry.TraceEventType.ToString());
                properties.Add("Entry.TraceSource", entry.TraceSource?.ToString());
                properties.Add("CodeSection.Name", entry.CodeSection?.Name);
                properties.Add("CodeSection.Type.Name", entry.CodeSection?.T?.Name);
                properties.Add("Assembly.Name", entry.CodeSection?.Assembly?.GetName()?.Name);
                properties.Add("CodeSection.NestingLevel", entry.CodeSection?.NestingLevel.ToString());
                properties.Add("CodeSection.OperationDept", entry.CodeSection?.OperationDept.ToString());
                properties.Add("CodeSection.OperationID", entry.CodeSection?.OperationID?.ToString());
                //properties.Add("Entry.ProcessInfo", entry.ProcessInfo);
                //properties.Add("CodeSection.Source", entry.CodeSection?.Source);
                //properties.Add("CodeSection.Payload", entry.CodeSection.Payload);
                //properties.Add("CodeSection.Result", entry.CodeSection.Result);
                //properties.Add("CodeSection.SourceFilePath", entry.CodeSection?.SourceFilePath);
                //properties.Add("CodeSection.SourceLevel", entry.CodeSection?.SourceLevel.ToString());
                //properties.Add("CodeSection.SourceLineNumber", entry.CodeSection?.SourceLineNumber.ToString());

                OnWrite(args);

                if (_trackEventEnabled)
                {
                    var eventName = entry.TraceEventType.ToString();
                    _telemetry.TrackEvent(eventName, properties, null); // this.Metrics as IDictionary<string, double>
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
                if (_trackExceptionEnabled && entry.TraceEventType == TraceEventType.Critical)
                {
                    _telemetry.TrackException(entry.Exception, properties, null);
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
                if (_trackTraceEnabled)
                {
                    var traceSurrogate = GetTraceSurrogate(entry);
                    var entryJson = SerializationHelper.SerializeJson(traceSurrogate);
                    var securityLevel = SeverityLevel.Verbose;
                    if (entry.TraceEventType.HasFlag(TraceEventType.Critical)) { securityLevel = SeverityLevel.Critical; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Error)) { securityLevel = SeverityLevel.Error; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Warning)) { securityLevel = SeverityLevel.Warning; }
                    else if (entry.TraceEventType.HasFlag(TraceEventType.Information)) { securityLevel = SeverityLevel.Information; }
                    _telemetry.TrackTrace(entryJson, securityLevel, properties);
                    if (_flushOnWrite) { _telemetry.Flush(); }
                }
            });
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TraceEntrySurrogate GetTraceSurrogate(TraceEntry entry)
        {
            var codeSection = entry.CodeSection;
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
                CodeSection = codeSection != null ? new CodeSectionSurrogate()
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

        protected virtual void OnWrite(OnWriteArgs args)
        {

        }
    }

    public class Account
    {
        public string FullName { get; set; }
        public string EmailAddress { get; set; }

        [JsonIgnore]
        public string PasswordHash { get; set; }
    }

}