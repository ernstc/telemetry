echo on
set env=DEV
IF NOT DEFINED UpdateSetup set UpdateSetup=false

set BinName=EkipConnectService.Full.Bin
set BinPath=.
set TargetBin=..\..\..\Resources\%BinName%
set env=DEV
rem comment

IF NOT EXIST "%TargetBin%" (
	mkdir "%TargetBin%"
)

IF /I %UpdateSetup%==True echo  robocopy . "%TargetBin%" /XF *.pdb *.log *.InstallLog *.cmd *.bat *.xml /PURGE
IF /I %UpdateSetup%==True robocopy . "%TargetBin%" /XF *.pdb *.log *.InstallLog *.cmd *.bat *.xml /PURGE


rem IF /I %UpdateRelease%==True xcopy /S/Y/I/D ".\*.*" "%releasePath%\new\%TargetName%" /EXCLUDE:%TargetFileName%.config
rem     IF /I %UpdateRelease%==True robocopy "%ProjectDir%\Resources\Diagnostic" "%ProgramData%\ABB\EkipConnect3\Tools\Diagnostic" /XF *.config *.log /E
rem     IF /I %UpdateRelease%==True robocopy "%ProjectDir%\Resources\Config" "%ProgramData%\ABB\EkipConnect3\Settings" /XF *.config *.log /E
rem IF /I %UpdateRelease%==True copy .\App.%env%.config "%releasePath%\%version%\%TargetName%\%TargetFileName%.config"
rem IF /I %UpdateRelease%==True copy .\App.DEV.config "%releasePath%\%version%\%TargetName%\%TargetFileName%.DEV.config"
rem IF /I %UpdateRelease%==True copy .\App.CERT.config "%releasePath%\%version%\%TargetName%\%TargetFileName%.CERT.config"
rem IF /I %UpdateRelease%==True copy .\App.PROD.config "%releasePath%\%version%\%TargetName%\%TargetFileName%.PROD.config"

rem IF /I %UpdateRelease%==True xcopy ".\%TargetName%.pdb" "%releasePath%\new" /S/Y/I/D
rem IF /I %UpdateRelease%==True xcopy ".\%TargetName%.dll" "%releasePath%\new" /S/Y/I/D

rem msiinfo "..\..\..\Ecgp.Ra.SkypeWebApp.Cp\%ConfigurationName%\RaCustomerDesktop.msi" /W 10
rem signtool sign  /f "..\..\Certificates\CodeSigningCertificate.%ConfigurationName%.pfx" /P Cariparma01 "..\..\..\Ecgp.Ra.SkypeWebApp.Cp\%ConfigurationName%\RaCustomerDesktop.msi"

rem msiinfo "..\..\..\Ecgp.Ra.SkypeWebAppNet35.Cp\%ConfigurationName%\RaCustomerDesktopNet35.msi" /W 10
rem signtool sign  /f "..\..\Certificates\CodeSigningCertificate.%ConfigurationName%.pfx" /P Cariparma01 "..\..\..\Ecgp.Ra.SkypeWebAppNet35.Cp\%ConfigurationName%\RaCustomerDesktopNet35.msi"
 