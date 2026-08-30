@echo off
setlocal
cd /d "%~dp0.."
set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
if not exist "%CSC_PATH%" (
  echo [ERROR] .NET Framework 3.5 compiler not found: "%CSC_PATH%"
  exit /b 1
)
if not defined LEGACY_EVENT_DIR set "LEGACY_EVENT_DIR=%~dp0..\..\DcsAgent"
if not exist "%LEGACY_EVENT_DIR%\DeltaV\DeltaVReader.cs" (
  echo [ERROR] Verified legacy DcsAgent source not found: "%LEGACY_EVENT_DIR%"
  echo         Set LEGACY_EVENT_DIR or edit acceptance-settings.ps1.
  exit /b 2
)
if not exist "bin" mkdir "bin"
echo Building read-only EventParityExport from legacy DeltaVReader...
"%CSC_PATH%" /nologo /target:exe /platform:x86 /optimize+ ^
  /reference:System.dll /reference:System.Data.dll /reference:System.Web.Extensions.dll ^
  /out:"bin\EventParityExport.exe" ^
  acceptance\EventParityExport.cs ^
  "%LEGACY_EVENT_DIR%\Configuration\AgentConfig.cs" ^
  "%LEGACY_EVENT_DIR%\Models\EventRecord.cs" ^
  "%LEGACY_EVENT_DIR%\Models\SyncCursor.cs" ^
  "%LEGACY_EVENT_DIR%\DeltaV\SourceInfo.cs" ^
  "%LEGACY_EVENT_DIR%\DeltaV\DeltaVReader.cs"
if errorlevel 1 exit /b 1
echo [OK] bin\EventParityExport.exe
exit /b 0

