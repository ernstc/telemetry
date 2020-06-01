# INTRODUCTION 
__Common.Diagnostics__ component provides __telemetry__ with application execution flow to configurable listeners.<br>
<br>
Basic execution flow is gathered by means of compiler generated information (eg. methodnames are obtained with `[CallerMemberName]` attribute).<br>
Start and completion of methods and code sections are gathered by means of `using` statements with a `CodeSection` class.<br>
References to Execution flow are gathered into a `TraceEntry` structure and sent to the configured listeners by means of .Net framework System.Diagnostics interfaces.<br>
<br>
Listeners receive telemetry data as structured TraceEntries.<br>
so, telemetry data is sent to the listeners without being read, serialized or processed anyway by TraceManager and CodeSection classes.<br>
This allows to every single listener *to access, process and display only the information that is required for its specific purpose*.<br>
Also, this allows saving processing cost of data that is not rendered (eg. debug message strings are not even created when debug telemetry is disabled)<br>
<br>
Common.Diagnostics component is supported on .Net Framework 4.6.2+ and .Net Core 3.0+.
