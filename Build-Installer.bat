@echo off
title Prabhupāda Connect - Build Standalone Installer
cd /d "%~dp0"
echo ========================================================
echo   Prabhupada Connect - Standalone Installer Generator   
echo ========================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Installer generation failed with code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)
pause
