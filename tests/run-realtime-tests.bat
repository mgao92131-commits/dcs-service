@echo off
setlocal
cd /d "%~dp0\.."
call acceptance\realtime\build-field-tests.bat
if errorlevel 1 exit /b 1
"test-tools\DcsDataService.RealtimeTests.exe"
exit /b %ERRORLEVEL%
