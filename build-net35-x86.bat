@echo off
setlocal
cd /d "%~dp0"

set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
if not exist "%CSC_PATH%" (
  echo [ERROR] .NET Framework 3.5 compiler not found: "%CSC_PATH%"
  exit /b 1
)

set "DELTAV_DLL_DIR=C:\DeltaV\bin"
if not exist "%DELTAV_DLL_DIR%\DeltaV.Historian.DvCHDataAccess.dll" (
  echo [ERROR] DeltaV.Historian.DvCHDataAccess.dll not found.
  echo         Install DeltaV under C:\DeltaV\bin before building.
  exit /b 2
)

if not exist "bin" mkdir "bin"
echo Building DcsDataService.exe for .NET Framework 3.5 x86...
"%CSC_PATH%" /nologo /target:exe /platform:x86 /optimize+ ^
  /reference:System.dll /reference:System.Data.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll ^
  /reference:"%DELTAV_DLL_DIR%\DeltaV.Historian.DvCHDataAccess.dll" ^
  /reference:"%DELTAV_DLL_DIR%\DeltaV.Historian.Data.dll" ^
  /reference:"%DELTAV_DLL_DIR%\DeltaV.Historian.Connection.dll" ^
  /out:"bin\DcsDataService.exe" ^
  src\DcsDataService\Program.cs ^
  src\DcsDataService\Configuration\*.cs ^
  src\DcsDataService\DeltaV\Historian\*.cs ^
  src\DcsDataService\DeltaV\Events\*.cs ^
  src\DcsDataService\DeltaV\Realtime\*.cs ^
  src\DcsDataService\Api\*.cs ^
  src\DcsDataService\Api\Handlers\*.cs ^
  src\DcsDataService\Models\*.cs ^
  src\DcsDataService\Util\*.cs
if errorlevel 1 exit /b 1

echo [OK] bin\DcsDataService.exe
echo [OK] DeltaV DLL source: C:\DeltaV\bin
exit /b 0
