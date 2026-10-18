@echo off
setlocal
cd /d "%~dp0"
echo Offline checks use an in-process fake runtime. No DCS parameter reads.
"%~dp0test-tools\DcsRealtimeAcceptance.exe" offline
set "DCS_TEST_EXIT=%ERRORLEVEL%"
echo.
echo Offline test exit code: %DCS_TEST_EXIT%
echo Results are saved under TestResults.
pause
exit /b %DCS_TEST_EXIT%
