@echo off
chcp 65001 >nul
rem ========================================================
rem  SyncPlay AutoTimer v2.0 - 初期セットアップ
rem ========================================================
rem  University of Osaka i-CHiLD Project
rem  Automated Video Synchronization System
rem ========================================================

cls
echo.
echo ========================================================
echo   SyncPlay AutoTimer v2.0
echo   初期セットアップウィザード
echo ========================================================
echo.
echo   このセットアップを実行すると、以下の設定が行われます：
echo.
echo   [1] Windowsファイアウォールの設定
echo       - ポート8080の受信接続を許可
echo       - LAN内の他のPCと通信するために必要です
echo.
echo   [2] タスクスケジューラへの登録
echo       - UACダイアログなしで起動できるようになります
echo       - 管理者権限で自動実行されます
echo.
echo ========================================================
echo.
echo   セットアップを開始してもよろしいですか？
echo.
pause

rem ========================================================
rem ステップ 1: 管理者権限チェック
rem ========================================================
cls
echo.
echo ========================================================
echo   [ステップ 1/3] 管理者権限の確認
echo ========================================================
echo.

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo   管理者権限が必要です。
    echo   管理者権限で再起動しています...
    echo.
    echo   ※ UACダイアログが表示されたら「はい」を選択してください
    echo.
    timeout /t 2 /nobreak >nul
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo   [OK] 管理者権限で実行されています
echo.
timeout /t 1 /nobreak >nul

rem ========================================================
rem ステップ 2: ファイアウォール設定
rem ========================================================
cls
echo.
echo ========================================================
echo   [ステップ 2/3] ファイアウォール設定
echo ========================================================
echo.
echo   LAN内の他のPCと通信するため、ポート8080を開放します...
echo.

rem PowerShellスクリプトのパスを取得
set "SCRIPT_DIR=%~dp0Script"
set "SETUP_SCRIPT=%SCRIPT_DIR%\setup-firewall.ps1"

rem スクリプトの存在確認
if not exist "%SETUP_SCRIPT%" (
    echo   [エラー] 設定スクリプトが見つかりません
    echo   パス: %SETUP_SCRIPT%
    echo.
    pause
    exit /b 1
)

rem PowerShellスクリプトを実行
PowerShell -ExecutionPolicy Bypass -File "%SETUP_SCRIPT%"

if %errorLevel% neq 0 (
    echo.
    echo   [エラー] ファイアウォール設定に失敗しました
    echo.
    pause
    exit /b 1
)

echo.
echo   [OK] ファイアウォール設定が完了しました
echo.
timeout /t 2 /nobreak >nul

rem ========================================================
rem ステップ 3: タスクスケジューラ登録
rem ========================================================
cls
echo.
echo ========================================================
echo   [ステップ 3/3] タスクスケジューラへの登録
echo ========================================================
echo.
echo   UACダイアログなしで起動できるよう、タスクを登録します...
echo.

rem PowerShellスクリプトのフルパスを取得
set "PS_MAIN=%SCRIPT_DIR%\main.ps1"

rem スクリプトの存在確認
if not exist "%PS_MAIN%" (
    echo   [エラー] メインスクリプトが見つかりません
    echo   パス: %PS_MAIN%
    echo.
    pause
    exit /b 1
)

rem 既存のタスクを削除（存在する場合）
schtasks /query /tn "SyncPlay_AutoTimer" >nul 2>&1
if %errorLevel% equ 0 (
    echo   既存のタスクを削除しています...
    schtasks /delete /tn "SyncPlay_AutoTimer" /f >nul
)

rem 一時XMLファイルを作成
set "TEMP_XML=%TEMP%\syncplay_task.xml"
(
echo ^<?xml version="1.0" encoding="UTF-16"?^>
echo ^<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"^>
echo   ^<Triggers /^>
echo   ^<Principals^>
echo     ^<Principal id="Author"^>
echo       ^<RunLevel^>HighestAvailable^</RunLevel^>
echo     ^</Principal^>
echo   ^</Principals^>
echo   ^<Settings^>
echo     ^<MultipleInstancesPolicy^>IgnoreNew^</MultipleInstancesPolicy^>
echo     ^<DisallowStartIfOnBatteries^>false^</DisallowStartIfOnBatteries^>
echo     ^<StopIfGoingOnBatteries^>false^</StopIfGoingOnBatteries^>
echo     ^<AllowHardTerminate^>true^</AllowHardTerminate^>
echo     ^<StartWhenAvailable^>false^</StartWhenAvailable^>
echo     ^<RunOnlyIfNetworkAvailable^>false^</RunOnlyIfNetworkAvailable^>
echo     ^<AllowStartOnDemand^>true^</AllowStartOnDemand^>
echo     ^<Enabled^>true^</Enabled^>
echo     ^<Hidden^>false^</Hidden^>
echo     ^<ExecutionTimeLimit^>PT0S^</ExecutionTimeLimit^>
echo   ^</Settings^>
echo   ^<Actions Context="Author"^>
echo     ^<Exec^>
echo       ^<Command^>powershell.exe^</Command^>
echo       ^<Arguments^>-ExecutionPolicy Bypass -File "%PS_MAIN%"^</Arguments^>
echo       ^<WorkingDirectory^>%SCRIPT_DIR%^</WorkingDirectory^>
echo     ^</Exec^>
echo   ^</Actions^>
echo ^</Task^>
) > "%TEMP_XML%"

rem XMLからタスクを登録
echo   タスクを登録しています...
schtasks /create /tn "SyncPlay_AutoTimer" /xml "%TEMP_XML%" /f >nul 2>&1

rem 一時XMLファイルを削除
del "%TEMP_XML%" >nul 2>&1

if %errorLevel% neq 0 (
    echo.
    echo   [エラー] タスクの登録に失敗しました
    echo.
    pause
    exit /b 1
)

echo   - タスク名: SyncPlay_AutoTimer
echo   - 実行レベル: 最上位の特権
echo   - トリガー: 手動実行
echo.
echo   [OK] タスクの登録が完了しました
echo.
timeout /t 2 /nobreak >nul

rem ========================================================
rem セットアップ完了
rem ========================================================
cls
echo.
echo ========================================================
echo   セットアップ完了！
echo ========================================================
echo.
echo   すべての設定が正常に完了しました。
echo.
echo --------------------------------------------------------
echo   使い方
echo --------------------------------------------------------
echo.
echo   [1] 通常起動
echo       start.bat をダブルクリックしてください
echo       - UACダイアログは表示されません
echo       - 管理者権限で自動的に実行されます
echo.
echo   [2] 設定の変更
echo       Script\main.ps1 をテキストエディタで開いてください
echo       - サーバーIP、ポート番号などを変更できます
echo.
echo   [3] Windows起動時に自動実行（オプション）
echo       以下の手順で設定できます：
echo       1. Win+R キーを押す
echo       2. taskschd.msc と入力して Enter
echo       3. SyncPlay_AutoTimer タスクを右クリック
echo       4. プロパティ → トリガー → 編集
echo       5. 「タスクの開始」を「システム起動時」に変更
echo.
echo ========================================================
echo.
echo   それでは、start.bat から起動してお楽しみください！
echo.
echo ========================================================
echo.
pause
