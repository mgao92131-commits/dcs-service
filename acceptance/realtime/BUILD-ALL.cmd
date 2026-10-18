@echo off
setlocal
cd /d "%~dp0"
call build-net35-x86.bat
if errorlevel 1 goto failed
call acceptance\realtime\build-field-tests.bat
if errorlevel 1 goto failed
echo BUILD PASSED
pause
exit /b 0
:failed
echo BUILD FAILED
pause
exit /b 1
