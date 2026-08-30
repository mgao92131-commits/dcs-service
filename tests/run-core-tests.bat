@echo off
setlocal
cd /d "%~dp0\.."
call build-net35-x86.bat
if errorlevel 1 exit /b 1
set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
"%CSC_PATH%" /nologo /target:exe /platform:x86 /reference:System.dll /reference:"bin\DcsDataService.exe" /out:"bin\DcsDataService.CoreTests.exe" tests\CoreTests.cs
if errorlevel 1 exit /b 1
"bin\DcsDataService.CoreTests.exe"
exit /b %ERRORLEVEL%
