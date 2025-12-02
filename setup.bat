@echo off
REM SyncPlay_AutoTimer v2.0 - Integrated Setup Script
REM This batch file wraps the PowerShell setup script for ease of use
REM Double-click to run the setup

setlocal enabledelayedexpansion

REM Get the directory where this batch file is located
set SCRIPT_DIR=%~dp0

REM Set console window size and font (improves readability)
REM Width=120 columns, Height=40 rows
mode con: cols=120 lines=40

REM Run PowerShell script with ExecutionPolicy Bypass
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%setup.ps1" %*

REM Pause to show the output
pause
