@echo off
setlocal
cd /d "%~dp0"
chcp 65001 >nul
echo DCS realtime API test: stop
"%~dp0test-tools\DcsRealtimeAcceptance.exe" stop
set "DCS_TEST_EXIT=%ERRORLEVEL%"
echo.
echo Exit code: %DCS_TEST_EXIT%
echo Results are under the TestResults folder.
pause
exit /b %DCS_TEST_EXIT%
