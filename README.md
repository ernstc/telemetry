# INTRODUCTION 
__Common.Diagnostics__ is a .Net Standard component that provides telemetry with __full execution flow__ to .Net System.Diagnostics listeners.<br>
<br>
Basic execution flow is gathered by means of compiler generated information (eg. methodnames are obtained with `[CallerMemberName]` attribute).<br>
Start and completion of code sections are gathered by means of `using` statements with a `CodeSection` class.<br>
The Execution flow is described by means of a `TraceEntry` structure that gathers references to information about the running code and variables.
`TraceEntry` structures are sent to the configured listeners by means of .Net framework System.Diagnostics interfaces.<br>
<br>
Listeners receive telemetry data as structured TraceEntries.<br>
So, telemetry data is sent to the listeners without being read, serialized or processed anyway by TraceManager and CodeSection classes.<br>
This allows to every single listener *to access, process and display only the information that is required for its specific purpose*.<br>
Also, this allows saving processing cost of data that is not rendered (eg. debug message strings are not even created when debug telemetry is disabled)<br>
<br>
Common.Diagnostics is supported by any .Net Framework version supporting .Net Standard 2.0.
Examples are provided for .NetCore 3.1+ and .Net Framework 4.6.2+ (including  .Net Framework 5.0).

# GETTING STARTED
<!-- span style="background-color: #FFFF99">TraceManager.Debug</span -->
Steps to use Common.Diagnostics:
1.	Add a package reference to the package __Common.Diagnostics.1.0.\*.\*.nupkg__
2.	Add telemetry to your code with code sections and named sections:
```c#
	- using (var sec = TraceManager.GetCodeSection(T)) // defines a code section within a static method
	- using (var sec = this.GetCodeSection()) // defines a code section within an instance method
	- using (var sec = TraceManager.GetNamedSection(T)) // defines a code section with a custom name within a static method
	- using (var sec = this.GetNamedSection("<sectionName>")) // defines a code section with a custom name within an instance method
```
3.	Add trace statements to your code to send custom data to the listeners
The following statements send telemetry information to the listeners when a code section `sec` is available
```c#
	- sec.Debug 
	- sec.Information 
	- sec.Warning
	- sec.Error 
	- sec.Exception 
```
The following statements send telemetry information to the listeners on methods where no code section instance is available
```c#
	- TraceManager.Debug 
	- TraceManager.Information 
	- TraceManager.Warning
	- TraceManager.Error 
	- TraceManager.Exception 
```
Telemetry is rendered by default to the default System.Diagnostics.DefaultTraceListener.
So, default telemetry is available in the visual studio output window as shown below and, server side, to the azure streaming log.

![alt text](/images/10.%20diginsight%20telemetry%20default%20listener.jpg "Server side telemetry to the default System.Diagnostics.DefaultTraceListener").
<!-- 
width="800" height="700" 
<img src="/diginsight/telemetry/blob/master/images/10. diginsight telemetry default listener.jpg?raw=true" 
	 alt="Starting telemetry in your application"
	 title="Starting telemetry in your application" 
	 style="user-select: auto;" />
-->

# TELEMETRY LISTENERS
The image below shows available diginsight packages with listeners to send data to the most popular logging systems, available with .Net framework.

![alt text](/images/09.%20diginsight%20telemetry%20packages.jpg "diginsight telemetry packages")
<!-- /diginsight/telemetry/blob/master/images/09.%20diginsight%20telemetry%20packages.jpg?raw=true

width="800" height="700" 
<img src="/diginsight/telemetry/blob/master/images/09. diginsight telemetry packages.jpg?raw=true" 
	 alt="Starting telemetry in your application"
	 title="Starting telemetry in your application" 
	 style="user-select: auto;" />
-->

Only `Information`, `Warning`, `Error` and `Exception` 
shoud be sent to cloud based telemetry listeners such as the __Common.Diagnostics.Appinsight__ listener.<br>
Debug information with the full execution flow normally used for listeners that write data to local repositories such as __Common.Diagnostics.Log4net__ and __Common.Diagnostics.Serilog__ listeners.<br>
Additional filters can be specified to select which data should be sent to which listener.<br>
Additional listeners are provided for more specific needs such as the __EventLogTraceListener__ and __TextboxTraceListener__ within __Common.Diagnostics.Win__ package.<br>


# ADDITIONAL INFORMATION

## Starting Telemetry
Just add Code Sections and Trace statements to your code to start telemetry for your application.<br>
__Common.Diagnostics__ will load listeners according to the configuration and start sending data to them.<br>

You can add Traces at different levels to instrument application code.<br>

<!-- 
	public partial class App : Application
	{
		static Type T = typeof(App);

		static App() 
		{ 
			using (var sec = TraceManager.GetCodeSection(T)) { 
			}
		}
	}
-->
![alt text](/images/00a._TraceManager_Traces.jpg "Adding traces to your application")
<!-- 
width="800" height="700" 
<img src="/diginsight/telemetry/blob/master/images/00a._TraceManager_Traces.jpg?raw=true" 
	 alt="Starting telemetry in your application"
	 title="Starting telemetry in your application" 
	 style="user-select: auto;" />
-->

## Instrumenting a code section
Use `GetCodeSection()` within a `using() statement` to add telemetry to any method.
Within the code section `sec`, traces can be added as shown below.

The **method name** is obtained by compiler generated parameters, **parameter names** and **values** can be provided with an unnamed class into the paiload parameter.<br>
If any, a code section **return value** can be provided in the CodeSection `Result` property.<br>
Additional information about the current class is provided an the explicit or a generic **Type argument**.<br>

![alt text](/images/02._CodeSection_with_static_method.jpg "Code section instrumented by means of a GetCodeSection()")
<!-- 
# thumbnail bordered
width="800" height="450" 
<img src="/diginsight/telemetry/blob/master/images/02._CodeSection_with_static_method.jpg?raw=true" 
	alt="Code section instrumented by means of a GetCodeSection()"
	title="Code section instrumented by means of a GetCodeSection()" 
	style="border: 1px solid black;" />
-->

In case of instance methods, the type argument can be omitted and the current class information about the method is taken by this object instance.
<!--
        private string getMessage(PublishResult publishResult)
        {
            string ret = null;
            using (var sec = this.GetCodeSection(new { publishResult }))
            {
                try
                {
					\.\.\.
				}
                finally { sec.Result = ret; }
            }
        }
-->

![alt text](/images/02b._CodeSection_with_instance_method.jpg "Instrumenting an instance code section")
<!-- # thumbnail bordered
<img src="/diginsight/telemetry/blob/master/images/02b._CodeSection_with_instance_method.jpg?raw=true" 
	alt="Instrumenting an instance code section"
	title="Instrumenting an instance code section" 
	style="border: 1px solid black;" />
-->

In the example above, parameters are provided to the code section with their names and values by means of an **unamed class**.
All information gathered with the code section instance, the parameter names and values will the available to the listeners at call start, call completion and every trace statement.

The image below shows Information level trace obtained from file based listeners such as the **Common.Diagnostics.Log4net**.
In this case the listener is configured to show telemetry as a flat log file.

![alt text](/images/03._Information_trace_unnested.jpg "Telemetry with a trace listener rendering") 
<!-- 
<img src="/diginsight/telemetry/blob/master/images/03._Information_trace_unnested.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

The same listeners can be configured to show the entire execution flow, with debug level information, method parameters and return values.
In this case the listener is configured to show the full application flow with methods nesting.

![alt text](/images/04._Debug_trace_with_nesting.jpg "Debug trace with nesting")
<!-- 
# thumbnail bordered
<img src="/diginsight/telemetry/blob/master/images/04._Debug_trace_with_nesting.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

## Configure Telemetry Listeners 
__Common.Diagnostics__ component uses .Net Framework System.Diagnostics components to notify telemetry to its listeners.<br>
This allows integrating Common.Diagnostics structured telemetry with traces from other components writing to System.Diagnostics.<br>
Also, this allows standard System.Diagnostics listeners receive telemetry from Common.Diagnostics component.<br>
As an example, System.Diagnostics.DefaultTraceListener used to send traces to the Visual Studio output window and to the Azure Streaming Log console can receive Common.Diagnostics telemetry along with the traces sent by any other component within the process.

With framework 4.6.2+ applications, Listeners can be configured as standard System.Diagnostics listeners on the application config file.<br>
A similar configuration structure is supported on the appsettings.json file that is supported on both .Net Core and .Net Full Applications.

![alt text](/images/05._Appsettings_configuration_file.jpg "Debug trace with nesting")
<!-- 
# thumbnail bordered
<img src="/diginsight/telemetry/blob/master/images/05._Appsettings_configuration_file.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

All trace listeners include a rich configuration to specify which telemetry information should be rendered and how it should be displayed.
In particular:
- the `ShowNestedFlow` setting: (default: false) allow configuring that the nested application flow should be rendered
- the `CategoryFilter` setting: (default: "") allows configuring exclusion rules based on message category
											  eg. CategoryFilter = "-resource" excludes all messages with category resource
- the `Filter` setting: (default: "") allows configuring exclusion rules based on message text
									  eg. Filter = "-CommunicationManager -Poll" excludes all messages where text includes CommunicationManager or Poll term

# GetLogString(), ISupportLogString and IProvideLogString
When logging object instances you can use the `GetLogString()` extension method.<br>
For primitive types, GetLogString() renders the full object value.<br>
For Arrays, Dictionaries and collections GetLogString() shows the number of items and the first items in the list (the list is truncated according to some configuration values)<br>
For other types, GetLogString() produces a string with the object short type name.<br>
<br>
You can provide log strings for your types by means of `ISupportLogString` interface as shown in figure below.

![alt text](/images/06._Class_with_ISupportLogString.jpg "Debug trace with nesting")
<!-- 
<img src="/diginsight/telemetry/blob/master/images/06._Class_with_ISupportLogString.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

For objects from external libraries You can provide log strings registering a provider with `IProvideLogString` interace as shown below:

![alt text](/images/07._Application_instance_with_IProvideLogString.jpg "Debug trace with nesting")
<!-- 
<img src="/diginsight/telemetry/blob/master/images/07._Application_instance_with_IProvideLogString.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

The image below shows the EasySample where logstrings are provided for Window and Button object instances.

![alt text](/images/08._Application_trace_with_custom_logstrings_from_IProvideLogString.jpg "Debug trace with nesting")
<!-- 
<img src="/diginsight/telemetry/blob/master/images/08._Application_trace_with_custom_logstrings_from_IProvideLogString.jpg?raw=true" 
	alt="Debug trace with nesting"
	title="Debug trace with nesting" 
	style="border: 1px solid black;" />
-->

# Build and Test 
Clone the repository, open and build solution Common.Diagnostics.sln. 
run EasySample and open the log file in your **\Log** folder.

# Contribute
Contribute to the repository with your pull requests. 

- [ASP.NET Core](https://github.com/aspnet/Home)
- [Visual Studio Code](https://github.com/Microsoft/vscode)

# License
See the [LICENSE](LICENSE.md) file for license rights and limitations (MIT).
