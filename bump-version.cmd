@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0bump-version.ps1" %*
if errorlevel 1 (
    echo.
    echo Bump version failed.
    pause
    exit /b 1
)
echo.
pause
