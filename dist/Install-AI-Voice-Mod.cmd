@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AI-Voice-Mod.ps1" %*
if errorlevel 1 pause
endlocal
