@echo off
chcp 65001 >nul
rem SyncPlay AutoTimer v2.0 - セットアップスクリプト
rem 目的: ファイアウォールルールを自動設定

echo ======================================================
echo   SyncPlay AutoTimer v2.0 セットアップ
echo ======================================================
echo.
echo このセットアップでは以下を実行します：
echo   1. 管理者権限の確認
echo   2. ポート8080のファイアウォールルール追加
echo.

rem 管理者権限チェック
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [エラー] このバッチファイルは管理者権限で実行する必要があります。
    echo.
    echo [解決方法]
    echo   1. このファイルを右クリック
    echo   2. "管理者として実行" を選択
    echo.
    pause
    exit /b 1
)

echo [OK] 管理者権限で実行中...
echo.

rem PowerShellスクリプトのパスを取得
set "SCRIPT_DIR=%~dp0Script"
set "SETUP_SCRIPT=%SCRIPT_DIR%\setup-firewall.ps1"

rem スクリプトの存在確認
if not exist "%SETUP_SCRIPT%" (
    echo [エラー] セットアップスクリプトが見つかりません。
    echo   パス: %SETUP_SCRIPT%
    echo.
    pause
    exit /b 1
)

echo [実行] ファイアウォールルール設定を開始します...
echo.

rem PowerShellスクリプトを実行
PowerShell -ExecutionPolicy Bypass -File "%SETUP_SCRIPT%"

if %errorLevel% equ 0 (
    echo.
    echo ======================================================
    echo   セットアップ完了
    echo ======================================================
    echo.
    echo [次のステップ]
    echo   1. Script\run.bat を実行してください
    echo   2. または以下のコマンドで手動起動:
    echo      cd Script
    echo      PowerShell -ExecutionPolicy Bypass -File "main.ps1"
    echo.
) else (
    echo.
    echo [エラー] セットアップに失敗しました。
    echo エラーコード: %errorLevel%
    echo.
)

pause
