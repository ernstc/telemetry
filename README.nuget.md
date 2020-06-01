# INTRODUCTION 
__Common.Diagnostics__ component provides __telemetry__ with the full __application flow__ to System.Diagnostic listeners.
Application execution flow can be sent to the most popular logging systems, available with .Net framework.
Execution flow is integrated with diagnostic information coming by any components writing to .Net System.Diagnostic interfaces.
Flexible options are provided to efficiently choose and filter the information to be rendered.

Basic execution flow is gathered by means of compiler generated information (eg. methodnames are obtained with `[CallerMemberName]` attribute).
Start and completion of methods and code sections are gathered by means of `using` statements with a `CodeSection` class.
References to Execution flow are gathered into a `TraceEntry` structure and sent to the configured listeners by means of .Net framework System.Diagnostics interfaces.

Listeners receive telemetry data as structured TraceEntries.
so, telemetry data is sent to the listeners without being read, serialized or processed anyway by TraceManager and CodeSection classes.
This allows to every single listener *to access, process and display only the information that is required for its specific purpose*.
Also, this allows saving processing cost of data that is not rendered (eg. debug message strings are not even created when debug telemetry is disabled)

Common.Diagnostics component is supported on .Net Framework 4.6.2+ and .Net Core 3.0+.
