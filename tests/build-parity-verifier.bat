@echo off
setlocal
cd /d "%~dp0\.."
set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
if not exist "%CSC_PATH%" exit /b 1
if not exist "test-tools" mkdir "test-tools"
"%CSC_PATH%" /nologo /target:exe /platform:x86 /reference:System.dll /reference:System.Web.Extensions.dll /out:"test-tools\ParityVerifier.exe" tests\ParityVerifier.cs
if errorlevel 1 exit /b 1
echo [OK] test-tools\ParityVerifier.exe
"test-tools\ParityVerifier.exe" history tests\fixtures\history-old.csv tests\fixtures\history-new.csv "China Standard Time"
if errorlevel 1 exit /b 1
"test-tools\ParityVerifier.exe" event tests\fixtures\event-old.json tests\fixtures\event-new.csv "China Standard Time"
if errorlevel 1 exit /b 1
exit /b 0
