@echo off
setlocal
cd /d "%~dp0\..\.."
if not exist "test-tools" mkdir "test-tools"
copy /y "bin\DcsDataService.exe" "test-tools\" >nul
if exist "C:\DeltaV\bin\DeltaV.Historian.DvCHDataAccess.dll" copy /y "C:\DeltaV\bin\DeltaV.Historian.*.dll" "test-tools\" >nul
set "FIELD_CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
"%FIELD_CSC%" /nologo /platform:x86 /reference:System.Web.Extensions.dll /out:"test-tools\DcsRealtimeAcceptance.exe" acceptance\realtime\RealtimeAcceptance.cs
if errorlevel 1 exit /b 1
"%FIELD_CSC%" /nologo /platform:x86 /reference:System.dll /reference:"bin\DcsDataService.exe" /out:"test-tools\DcsDataService.CoreTests.exe" tests\CoreTests.cs
if errorlevel 1 exit /b 1
"%FIELD_CSC%" /nologo /platform:x86 /reference:System.dll /reference:"bin\DcsDataService.exe" /out:"test-tools\DcsDataService.RealtimeTests.exe" tests\RealtimeTests.cs
exit /b %ERRORLEVEL%
