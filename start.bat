@echo off
rem 日本語の文字化け防止（UTF-8に設定）
chcp 65001 >nul
rem SyncPlay AutoTimer - タスクスケジューラ経由で起動（UACダイアログなし）

echo ======================================================
echo   SyncPlay AutoTimer 起動
echo ======================================================
echo.

rem タスクが登録されているか確認
schtasks /query /tn "SyncPlay_AutoTimer" >nul 2>&1
if %errorLevel% neq 0 (
    echo [エラー] タスクが登録されていません
    echo.
    echo セットアップが完了していない可能性があります。
    echo setup.bat を実行してセットアップを完了してください。
    echo.
    pause
    exit /b 1
)

echo タスクスケジューラ経由で起動しています...
echo （UACダイアログは表示されません）
echo.

rem タスクを実行
schtasks /run /tn "SyncPlay_AutoTimer" >nul 2>&1

if %errorLevel% equ 0 (
    echo [成功] 起動しました
    echo.
    echo 数秒後にSyncPlayウィンドウが表示されます...
    timeout /t 3 /nobreak >nul
) else (
    echo [エラー] タスクの実行に失敗しました
    echo.
    echo 以下を確認してください：
    echo 1. setup.bat を実行してセットアップを完了しているか
    echo 2. タスクスケジューラでタスクが有効になっているか
    echo.
    pause
)
