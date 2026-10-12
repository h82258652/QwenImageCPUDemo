@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\PrepareModels.ps1" %*
exit /b %errorlevel%
