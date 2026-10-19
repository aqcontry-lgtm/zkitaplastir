@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set UIAC=
set UIAT=
for /d %%d in ("C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_*") do set UIAC=%%d\UIAutomationClient.dll
for /d %%d in ("C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_*") do set UIAT=%%d\UIAutomationTypes.dll
"%CSC%" /nologo /target:exe /platform:x64 /out:ZKitaplastir.exe /win32manifest:app.manifest /resource:dwebp.exe,dwebp.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:"%UIAC%" /r:"%UIAT%" Program.cs
if errorlevel 1 (echo DERLEME BASARISIZ & pause & exit /b 1)
echo OK: ZKitaplastir.exe
pause
