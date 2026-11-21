@echo off
setlocal
if exist "%~dp0ChargeStrip.exe" (
    start "" "%~dp0ChargeStrip.exe" --settings
    exit /b 0
)
call "%~dp0Start Charge Strip.cmd" /settings
exit /b %errorlevel%
