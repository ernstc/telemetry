using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
