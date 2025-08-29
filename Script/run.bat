@echo off
rem PowerShellスクリプトを実行するバッチファイル

rem PowerShellスクリプトのパスを指定（同じフォルダにある場合）
set "PS_SCRIPT=%~dp0main.ps1"

rem PowerShellスクリプトが存在するかチェック
if not exist "%PS_SCRIPT%" (
    echo [エラー] PowerShellスクリプトが見つかりません。
    echo 以下のファイルを同じフォルダに配置してください：
    echo - main.ps1
    pause
    exit /b
)

echo ======================================================
echo  SyncPlay自動起動システム
echo ======================================================
echo.
echo PowerShellスクリプトを実行します...
echo.

rem PowerShellを管理者権限で実行（ExecutionPolicyをBypassして実行）
PowerShell -ExecutionPolicy Bypass -File "%PS_SCRIPT%"

rem エラーが発生した場合
if errorlevel 1 (
    echo.
    echo [エラー] スクリプトの実行に失敗しました。
    echo 以下を確認してください：
    echo 1. PowerShellがインストールされているか
    echo 2. スクリプトのパスが正しいか
    echo 3. 管理者権限で実行しているか
    pause
)