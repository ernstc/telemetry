#region using
using Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks; 
#endregion


namespace EkipConnect.Services.Logging
{
    internal static class Log
    {  
        private const int MAX_RECORD_LENGHT = (int)10e6;  

        private static bool _allowRecord;
        private static StringBuilder _logRecorder = new StringBuilder();        //TODO: thread safe ?
        private static readonly object _syncRoot = new object();

        public static LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Debug;
        public static string RecordedLog => _logRecorder.ToString();


        //TODO?: return message for compact writing?
        //Example: var text = Log.Debug(message);  send message to log and assign it to text variable
        public static void Verbose(string message)
        {
            TraceManager.Debug(message, "user");
            log(message, LogEventLevel.Verbose);
        }
        public static void Debug(string message)
        {
            TraceManager.Debug(message, "user");
            log(message, LogEventLevel.Debug);
        }
        public static string Information(string message)
        {
            TraceManager.Information(message, "user");
            log(message, LogEventLevel.Information);
            return message;
        }
        public static string Error(string message)
        {
            TraceManager.Error(message, "user");
            log(message, LogEventLevel.Error);
            return message;
        }
        public static void StartRecord()
        {
            _logRecorder.Clear();
            _allowRecord = true;
        }
        public static void StopRecord()
        {
            _allowRecord = false;
        }

        private static void log(string message, LogEventLevel level)
        {
            if (MinimumLevel <= level)
            {
                message = $"{DateTime.Now:hh:mm:ss:fff}\t\t{message}\r\n";
                Console.WriteLine(message);
                if (_allowRecord)
                    record(message);
            }
        }
        private static void record(string message)
        {
            lock(_syncRoot)
            {
                _logRecorder.Append(message);
                if (_logRecorder.Length >= MAX_RECORD_LENGHT)
                    _logRecorder.Clear();

            }
        }
    }
}
