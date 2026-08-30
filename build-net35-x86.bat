@echo off
setlocal
cd /d "%~dp0"

set "CSC_PATH=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
if not exist "%CSC_PATH%" (
  echo [ERROR] .NET Framework 3.5 compiler not found: "%CSC_PATH%"
  exit /b 1
)

if not defined DELTAV_DLL_DIR set "DELTAV_DLL_DIR=C:\DeltaV\bin"
if not exist "%DELTAV_DLL_DIR%\DeltaV.Historian.DvCHDataAccess.dll" if exist "..\dcs_data\hda\DeltaV.Historian.DvCHDataAccess.dll" set "DELTAV_DLL_DIR=%~dp0..\dcs_data\hda"
if not exist "%DELTAV_DLL_DIR%\DeltaV.Historian.DvCHDataAccess.dll" (
  echo [ERROR] DeltaV.Historian.DvCHDataAccess.dll not found.
  echo         Set DELTAV_DLL_DIR to the installed DeltaV Historian assembly directory.
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
  src\DcsDataService\Api\*.cs ^
  src\DcsDataService\Api\Handlers\*.cs ^
  src\DcsDataService\Models\*.cs ^
  src\DcsDataService\Util\*.cs
if errorlevel 1 exit /b 1

echo Staging DeltaV Historian runtime assemblies beside the executable...
for %%D in (
  DeltaV.Historian.DvCHDataAccess.dll
  DeltaV.Historian.Connection.dll
  DeltaV.Historian.Data.dll
  DeltaV.Historian.DataEditing.dll
  DeltaV.Historian.Utility.dll
  DeltaV.Historian.Compression.dll
  DeltaV.Historian.Exceptions.dll
  DeltaV.Historian.ResourceUtility.dll
) do (
  if not exist "%DELTAV_DLL_DIR%\%%D" (
    echo [ERROR] Required DeltaV runtime dependency not found: "%DELTAV_DLL_DIR%\%%D"
    exit /b 3
  )
  copy /y "%DELTAV_DLL_DIR%\%%D" "bin\%%D" >nul
  if errorlevel 1 (
    echo [ERROR] Failed to stage runtime dependency: %%D
    exit /b 3
  )
)
echo [OK] bin\DcsDataService.exe
echo [OK] DeltaV runtime assemblies staged from "%DELTAV_DLL_DIR%"
exit /b 0
