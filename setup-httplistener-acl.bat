@echo off
REM SyncPlay_AutoTimer v2.0 - HTTP ACL Setup Script
REM This batch file wraps the PowerShell HTTP ACL setup script
REM Run as Administrator for proper configuration

setlocal enabledelayedexpansion

REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0

REM Check if running as Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo ===================================================
    echo ERROR: Administrator privileges required!
    echo ===================================================
    echo.
    echo This script requires administrator privileges to configure HTTP ACL.
    echo Please right-click this file and select "Run as Administrator"
    echo.
    pause
    exit /b 1
)

REM Run PowerShell script with ExecutionPolicy Bypass
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%setup-httplistener-acl.ps1" %*

REM Pause to show the output
pause
