echo on
set env=DEV
IF NOT DEFINED UpdateSetup set UpdateSetup=false

rem set TargetBin=C:\temp\New folder
set TargetBin=E:\dev\01. ABB Port\Ekip Connect 3\EkipConnect\bin\Debug
set env=DEV

IF /I %UpdateSetup%==True echo  robocopy .\bin\Debug\net472 "%TargetBin%" *.dll *.pdb
IF /I %UpdateSetup%==True robocopy .\bin\Debug\net472 "%TargetBin%" Common.Diagnostics*.dll
IF /I %UpdateSetup%==True robocopy .\bin\Debug\net472 "%TargetBin%" Common.Diagnostics*.pdb

 