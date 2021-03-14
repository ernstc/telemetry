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
    public struct TraceEntry
    {
        public TraceEventType TraceEventType { get; set; }
        public string Message { get; set; }
        public IDictionary<string, object> Properties { get; set; }
        public string Source { get; set; }
        public string Category { get; set; }
        public SourceLevels SourceLevel { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public long TraceStartTicks { get; set; }
        public Exception Exception { get; set; }
        public int ThreadID { get; set; }
        public ApartmentState ApartmentState { get; set; }
        public bool DisableCRLFReplace { get; set; }
        public Func<string> GetMessage { get; set; }
        [JsonIgnore]
        public Thread Thread { get; set; }
        [JsonIgnore]
        public TraceSource TraceSource { get; set; }
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
}
