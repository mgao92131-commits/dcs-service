@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp003-History-Parity.ps1" -Mode AutoSplit
pause

