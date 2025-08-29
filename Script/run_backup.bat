@echo off
setlocal enabledelayedexpansion
rem 日本語の文字化け防止
chcp 65001 > nul

:================================================================================
: 設定項目
:================================================================================

: Syncplay.exeのフルパスを指定
set "SYNCPLAY_PATH=C:\Program Files (x86)\Syncplay\Syncplay.exe"

: Syncplayの起動時刻を指定(24時間表記(書式:HH:MM HH:MM ...))
set "TARGET_TIMES=14:37 14:38 14:39"

: サーバIPアドレスを指定
set "SERVER_IP=192.168.100.13"

: サーバーポート番号を指定（デフォルトは8999）
set "SERVER_PORT=8999"

: 接続時のユーザー名を指定
set "USER_NAME=Server"

: ルーム名を指定
set "ROOM_NAME=Test_Run"

: ルームパスワード（オプション：空欄可）
set "ROOM_PASSWORD="

: 動画のフルパスを指定
set "VIDEO_FILE_PATH=D:\Lemon\Documents\SyncPlay_AutoTimer\Video\Terminal.0_Video_250722_v2.mp4"

: VLCプレイヤーのパスを指定
set "VLC_PATH=C:\Program Files\VideoLAN\VLC\vlc.exe"

:================================================================================
: Syncplay設定ファイルを生成
:================================================================================

rem 設定ファイルのパス
set "CONFIG_FILE=%TEMP%\syncplay_config.ini"

echo [INFO] Syncplay設定ファイルを生成しています...

rem 設定ファイルを作成
(
echo [server_data]
echo host=%SERVER_IP%
echo port=%SERVER_PORT%
echo.
echo [client_settings]
echo name=%USER_NAME%
echo room=%ROOM_NAME%
if defined ROOM_PASSWORD echo password=%ROOM_PASSWORD%
echo playerPath=%VLC_PATH%
echo autoplay=true
echo readyatstart=true
echo.
echo [general]
echo language=ja
) > "%CONFIG_FILE%"

echo [INFO] 設定ファイルを生成しました: %CONFIG_FILE%

:================================================================================
: メイン処理
:================================================================================
cls

rem 起動済み時刻を記録する変数を初期化
set "LAUNCHED_TIMES="

echo.
echo ======================================================
echo  Syncplay 定刻起動バッチ (設定ファイル版)
echo ======================================================
echo.
echo   起動予定時刻: %TARGET_TIMES%
echo   サーバー    : %SERVER_IP%:%SERVER_PORT%
echo   ユーザー名  : "%USER_NAME%"
echo   ルーム名    : "%ROOM_NAME%"
if defined ROOM_PASSWORD (
    echo   パスワード  : [設定済み]
)
if defined VIDEO_FILE_PATH (
    echo   動画ファイル: "%VIDEO_FILE_PATH%"
)
echo   プレイヤー  : VLC
echo.
echo.

rem 必要なファイルの存在チェック
if not exist "%SYNCPLAY_PATH%" (
    echo [エラー] Syncplay.exe が見つかりません: "%SYNCPLAY_PATH%"
    pause
    exit /b
)

if not exist "%VLC_PATH%" (
    echo [エラー] VLC が見つかりません: "%VLC_PATH%"
    pause
    exit /b
)

if defined VIDEO_FILE_PATH (
    if not exist "%VIDEO_FILE_PATH%" (
        echo [エラー] 動画ファイルが見つかりません: "%VIDEO_FILE_PATH%"
        pause
        exit /b
    )
)

echo 指定時刻になるまで待機します...
echo (Ctrl+C で中止できます)
echo.

:LOOP
rem 現在の時刻を HH:MM の形式で取得
set "CURRENT_TIME=%TIME:~0,5%"
rem 時刻の先頭がスペースの場合、0に置換
if "%CURRENT_TIME:~0,1%"==" " set "CURRENT_TIME=0%CURRENT_TIME:~1,4%"

rem 設定された各時刻をチェック
for %%T in (%TARGET_TIMES%) do (
    if "%%T"=="!CURRENT_TIME!" (
        rem この時刻がまだ起動されていないかチェック
        echo !LAUNCHED_TIMES! | find "%%T" > nul
        if errorlevel 1 (
            echo.
            echo ======================================================
            echo [INFO] !CURRENT_TIME! になりました。処理を開始します...
            echo ======================================================
            
            rem 既存のSyncplayとVLCを終了
            echo [INFO] 既存のプロセスを終了しています...
            taskkill /F /IM Syncplay.exe 2>nul
            taskkill /F /IM vlc.exe 2>nul
            timeout /t 2 /nobreak > nul
            
            rem 方法1: 設定ファイル経由で起動
            echo [INFO] 方法1: 設定ファイル経由で起動を試みます...
            start "" "%SYNCPLAY_PATH%" --load-config "%CONFIG_FILE%" "%VIDEO_FILE_PATH%"
            
            timeout /t 5 /nobreak > nul
            
            rem 起動確認
            tasklist /FI "IMAGENAME eq Syncplay.exe" 2>NUL | find /I /N "Syncplay.exe">NUL
            if errorlevel 1 (
                echo [警告] 方法1が失敗しました。方法2を試します...
                
                rem 方法2: コマンドライン引数で直接起動
                echo [INFO] 方法2: 直接引数で起動を試みます...
                start "" "%SYNCPLAY_PATH%" ^
                    --host "%SERVER_IP%" ^
                    --port %SERVER_PORT% ^
                    --name "%USER_NAME%" ^
                    --room "%ROOM_NAME%" ^
                    --player-path "%VLC_PATH%" ^
                    --no-store ^
                    "%VIDEO_FILE_PATH%"
                
                timeout /t 5 /nobreak > nul
                
                rem 再度確認
                tasklist /FI "IMAGENAME eq Syncplay.exe" 2>NUL | find /I /N "Syncplay.exe">NUL
                if errorlevel 1 (
                    echo [エラー] Syncplayの起動に失敗しました。
                    
                    rem 方法3: AutoHotkeyスクリプトを生成して実行（オプション）
                    echo [INFO] 方法3: 手動操作をシミュレートします...
                    call :CREATE_AHK_SCRIPT
                ) else (
                    echo [INFO] Syncplayが起動しました（方法2）。
                )
            ) else (
                echo [INFO] Syncplayが起動しました（方法1）。
            )
            
            rem 起動リストに追加
            set "LAUNCHED_TIMES=!LAUNCHED_TIMES! %%T"
            echo.
            echo [INFO] 起動処理が完了しました。
            echo [INFO] 起動済み時刻: !LAUNCHED_TIMES!
            echo ======================================================
            echo.
        )
    )
)

rem 1秒待機してループ
timeout /t 1 /nobreak > nul
goto :LOOP

:================================================================================
: AutoHotkeyスクリプト生成（オプション）
:================================================================================
:CREATE_AHK_SCRIPT
rem AutoHotkeyがインストールされている場合のみ使用
set "AHK_SCRIPT=%TEMP%\syncplay_auto.ahk"

echo [INFO] AutoHotkeyスクリプトを生成しています...

(
echo ; Syncplay自動操作スクリプト
echo Run, "%SYNCPLAY_PATH%" "%VIDEO_FILE_PATH%"
echo Sleep, 3000
echo.
echo ; サーバー情報を入力
echo Send, %SERVER_IP%
echo Send, {Tab}
echo Send, %SERVER_PORT%
echo Send, {Tab}
echo.
echo ; ユーザー名を入力
echo Send, %USER_NAME%
echo Send, {Tab}
echo.
echo ; ルーム名を入力
echo Send, %ROOM_NAME%
echo Send, {Tab}
echo.
if defined ROOM_PASSWORD (
    echo ; パスワードを入力
    echo Send, %ROOM_PASSWORD%
    echo Send, {Tab}
)
echo.
echo ; 接続ボタンをクリック
echo Send, {Enter}
echo Sleep, 2000
echo.
echo ; 再生開始
echo Send, {Space}
) > "%AHK_SCRIPT%"

rem AutoHotkeyが存在する場合は実行
if exist "C:\Program Files\AutoHotkey\AutoHotkey.exe" (
    echo [INFO] AutoHotkeyスクリプトを実行します...
    start "" "C:\Program Files\AutoHotkey\AutoHotkey.exe" "%AHK_SCRIPT%"
) else (
    echo [INFO] AutoHotkeyがインストールされていません。
    echo [INFO] 手動での接続が必要です。
)

goto :EOF