
Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$assemblyInfoFile = "..\Common.Diagnostics\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"

# $version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
# if ($null -eq $version) { throw "cannot find AssemblyVersion attribute in file '$assemblyInfoFile'"; }
# $newVersion = "{0}.{1}.{2}.{3}" -f $version.Major, $version.Minor, $version.Build, ($version.Revision + 1)
# SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $newVersion

$version = IncrementVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.v2\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Full\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Core\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Win\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Log4net\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Serilog\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.AppInsights\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

Write-Host "version: $version"
Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

git --version
git config user.email buildagent@microsoft.com
git config user.name "Build Agent" 
Write-Host "before git commit"
git commit -a -m "Build version update"
Write-Host "after git commit"
try {
  Write-Host "before git push origin HEAD:$($env:BUILD_SOURCEBRANCHNAME)"
  git push origin HEAD:$($env:BUILD_SOURCEBRANCHNAME) 
  Write-Host "after git push"
} catch {
  Write-Host "##vso[task.logissue type=warning]failure on git push command."
}


Stop-Transcript

return 0;