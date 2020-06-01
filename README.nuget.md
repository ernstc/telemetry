# INTRODUCTION 
__Common.Diagnostics__ component provides __telemetry__ with the full __application flow__ to System.Diagnostic listeners.
Application execution flow can be sent to the most popular logging systems, available with .Net framework.
Execution flow is integrated with diagnostic information coming by any components writing to .Net System.Diagnostic interfaces.
Flexible options are provided to efficiently choose and filter the information to be rendered.

Basic execution flow is gathered by means of compiler generated information (eg. methodnames are obtained with `[CallerMemberName]` attribute).
Start and completion of methods and code sections are gathered by means of `using` statements with a `CodeSection` class.

add telemetry to your methods with the following instruction 

`using (var sec = this.GetCodeSection())`

write information to the listeners with the following instructions
`sec.Debug(&quot;this is a debug trace&quot;, &quot;User&quot;);`
`sec.Information(&quot;this is a Information trace&quot;, &quot;Raw&quot;);`
`sec.Warning(&quot;this is a Warning trace&quot;, &quot;User.Report&quot;);`
`sec.Error(&quot;this is a error trace&quot;, &quot;Resource&quot;);`

Common.Diagnostics component is supported on .Net Framework 4.6.2+ and .Net Core 3.0+.
Visit [telemetry][] for more information.
[telemetry]: https://github.com/diginsight/telemetry/
