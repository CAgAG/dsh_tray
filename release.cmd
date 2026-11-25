@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
if errorlevel 1 (
    echo.
    echo Release failed.
    pause
    exit /b 1
)
echo.
pause
