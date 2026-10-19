@echo off
rem Baut PrzPreview.dll (Explorer-Vorschau) und PrzViewer.exe (Test-Viewer) mit dem .NET Framework 4 Compiler.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:"%FW%\System.IO.Compression.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\WPF\WindowsFormsIntegration.dll" /r:"%FW%\System.Xaml.dll"
cd /d "%~dp0"
if not exist bin mkdir bin

"%FW%\csc.exe" /nologo /target:library /platform:anycpu /optimize+ /out:bin\PrzPreview.dll %REFS% src\AssemblyInfo.cs src\PrzData.cs src\ModelViewer.cs src\PrzPreviewControl.cs src\PreviewHandler.cs src\ShimEntry.cs
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /out:bin\PrzViewer.exe %REFS% /r:bin\PrzPreview.dll src\Viewer.cs
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /out:bin\HandlerTest.exe %REFS% /r:bin\PrzPreview.dll src\HandlerTest.cs
if errorlevel 1 exit /b 1

echo Build OK
