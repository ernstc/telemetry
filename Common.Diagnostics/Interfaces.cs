using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common
{
    #region LogEventLevel
    public enum LogEventLevel
    {
        // Summary: Anything and everything you might want to know about a running block of code.
        Verbose = 0,
        // Summary: Internal system events that aren't necessarily observable from the outside.
        Debug = 1,
        // Summary: The lifeblood of operational intelligence - things happen.
        Information = 2,
        // Summary: Service is degraded or endangered.
        Warning = 3,
        // Summary: Functionality is unavailable, invariants are broken or data is lost.
        Error = 4,
        // Summary: If you have a pager, it goes off when one of these occurs.
        Fatal = 5
    }
    #endregion

    public interface ICodeSection {
        bool _isLogEnabled { get; set; } // = true;
        bool? _showNestedFlow { get; set; }
        int? _maxMessageLevel { get; set; }
        int? _maxMessageLen { get; set; }
        int? _maxMessageLenError { get; set; }
        int? _maxMessageLenWarning { get; set; }
        int? _maxMessageLenInfo { get; set; }
        int? _maxMessageLenVerbose { get; set; }
        int? _maxMessageLenDebug { get; set; }

        ICodeSection Caller { get; set; }
        int NestingLevel { get; set; }
        int OperationDept { get; set; }
        object Payload { get; set; }
        // object Exception { get; set; }
        object Result { get; set; }
        string Name { get; set; }
        string MemberName { get; set; }
        string SourceFilePath { get; set; }
        int SourceLineNumber { get; set; }
        string Source { get; set; }
        string Category { get; set; }

        long CallStartMilliseconds { get; set; }
        long CallStartTicks { get; set; }
        DateTime SystemStartTime { get; set; }
        string OperationID { get; set; }
        bool IsDisposed { get; set; }
        bool DisableStartEndTraces { get; set; }

        Type T { get; set; }
        string TypeName { get; set; }
        string ClassName { get; set; }
        Assembly Assembly { get; set; }

        TraceSource TraceSource { get; set; }
        TraceEventType TraceEventType { get; set; }
        IModuleContext ModuleContext { get; set; }
        SourceLevels SourceLevel { get; set; }
        LogLevel LogLevel { get; set; }
        IDictionary<string, object> Properties { get; set; }
        bool IsInnerScope { get; set; }
        ICodeSection InnerScope { get; set; }

        ICodeSection GetInnerSection();
    }

    public interface ICodeSectionLogger
    {
        void Debug(object obj, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Debug(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Debug(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Information(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Information(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);

        void Warning(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Warning(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);

        void Error(NonFormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);
        void Error(FormattableString message, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = false);

        void Exception(Exception exception, string category = null, IDictionary<string, object> properties = null, string source = null, bool disableCRLFReplace = true);
    }

    public interface ISupportFilters
    {
        string Filter { get; set; }
    }

    public interface ISupportInnerListener
    {
        TraceListener InnerListener { get; set; }
    }
    public interface IFormatTraceEntry
    {
        string FormatTraceEntry(TraceEntry entry, Exception ex);
    }
}
