@echo off
setlocal
cd /d "%~dp0\.."
call build-net35-x86.bat
if errorlevel 1 exit /b 1
call acceptance\realtime\build-field-tests.bat
if errorlevel 1 exit /b 1
"test-tools\DcsDataService.CoreTests.exe"
exit /b %ERRORLEVEL%
