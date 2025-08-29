# SyncPlay自動起動PowerShellスクリプト
# 実行方法: PowerShell -ExecutionPolicy Bypass -File "main.ps1"

#================================================================================
# 設定項目
#================================================================================

# SyncPlay 起動変数
$SyncplayPath = "C:\Program Files (x86)\Syncplay\SyncplayConsole.exe"
$SyncplayServerPath = "C:\Program Files (x86)\Syncplay\syncplayServer.exe"
$VLCPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
$ServerIP = "192.168.100.13"
$ServerPort = "8999"
$UserName = "Server"
$RoomName = "Test_Run"
$RoomPassword = ""  # オプション
$VideoFilePath = "D:\Lemon\Documents\SyncPlay_AutoTimer\Video\Terminal.0_Video_250722_v2.mp4"

# 起動時刻のリスト（24時間表記）
$TargetTimes = @("00:00", "00:00", "00:00")

# Severモード
$SeverMode = $true

#================================================================================
# 関数定義
#================================================================================

function Start-SyncplayAuto {
    
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "[INFO] Syncplayを起動します..." -ForegroundColor Green
    Write-Host "======================================================" -ForegroundColor Cyan
    
    # 既存のプロセスを終了
    Write-Host "[INFO] 既存のプロセスを終了しています..." -ForegroundColor Yellow
    Get-Process -Name "Syncplay" -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    
    # 引数を構築
    $arguments = @(
        "--host", "${ServerIP}:${ServerPort}",
        "--name", "`"${UserName}`"",
        "--room", "`"${RoomName}`"",
        "--player-path", "`"${VLCPath}`"",
        "--no-store"
    )
    
    if ($RoomPassword) {
        $arguments += "--password"
        $arguments += $RoomPassword
    }
    
    # 動画ファイルを最後に追加
    $arguments += "`"${VideoFilePath}`""
    
    Write-Host "[DEBUG] コマンド: $SyncplayPath" -ForegroundColor Yellow
    Write-Host "[DEBUG] 引数: $($arguments -join ' ')" -ForegroundColor Blue
    
    # SyncPlayを起動
    try {
        $process = Start-Process -FilePath "${SyncplayPath}" -ArgumentList "${arguments}" -PassThru
        Start-Sleep -Seconds 3
        
        if ($process.HasExited) {
            Write-Host "[ERROR] Syncplayが起動に失敗しました。" -ForegroundColor Red
            
            # 代替方法: COM オブジェクトを使用
            Write-Host "[INFO] 代替方法で再試行します..." -ForegroundColor Yellow
            $shell = New-Object -ComObject Shell.Application
            $shell.ShellExecute($SyncplayPath, ($arguments -join ' '))
        } else {
            Write-Host "[INFO] Syncplayが正常に起動しました。" -ForegroundColor Green
            
            # VLCが起動するまで待機
            Start-Sleep -Seconds 3
        }
    }
    catch {
        Write-Host "[ERROR] 起動中にエラーが発生しました: $_" -ForegroundColor Red
    }
}

#================================================================================
# メイン処理
#================================================================================

Clear-Host

Write-Host ""
Write-Host "======================================================"
Write-Host "       Syncplay 定刻起動スクリプト (PowerShell版)"
Write-Host "======================================================"
Write-Host ""
Write-Host "  起動予定時刻: $($TargetTimes -join ', ')"
Write-Host "  サーバー    : ${ServerIP}:${ServerPort}"
Write-Host "  ユーザー名  : $UserName"
Write-Host "  ルーム名    : $RoomName"
Write-Host "  動画ファイル: $(Split-Path -Leaf $VideoFilePath)"
Write-Host ""

# ファイル存在チェック
if (-not (Test-Path $SyncplayPath)) {
    Write-Host "[ERROR] Syncplay.exeが見つかりません。" -ForegroundColor Red
    Read-Host "Enterキーを押して終了"
    exit
}

if (-not (Test-Path $VLCPath)) {
    Write-Host "[ERROR] VLCが見つかりません。" -ForegroundColor Red
    Read-Host "Enterキーを押して終了"
    exit
}

if (-not (Test-Path $VideoFilePath)) {
    Write-Host "[ERROR] 動画ファイルが見つかりません。" -ForegroundColor Red
    Read-Host "Enterキーを押して終了"
    exit
}

Write-Host "指定時刻になるまで待機します..." -ForegroundColor Cyan
Write-Host "(Ctrl+C で中止できます)" -ForegroundColor Gray
Write-Host ""

# 起動済み時刻を記録
$LaunchedTimes = @()

# SyncPlay Serverを起動
if ($SeverMode){
    $process = Start-Process -FilePath "${SyncplayServerPath}"
    Start-Sleep -Seconds 5
        
    if ($process.HasExited) {
        Write-Host "[ERROR] Syncplay Serverが起動に失敗しました。" -ForegroundColor Red
    } else {
        Write-Host "[INFO] Syncplay Serverが正常に起動しました。" -ForegroundColor Green
            
        # VLCが起動するまで待機
        Start-Sleep -Seconds 3
    }
}

# メインループ
while ($true) {
    $currentTime = Get-Date -Format "HH:mm"
    
    foreach ($targetTime in $TargetTimes) {
        $targetTime = Get-Date -Format "HH:mm"
        if ($currentTime -eq $targetTime -and $targetTime -notin $LaunchedTimes) {
            Write-Host ""
            Write-Host "[INFO] $currentTime になりました。" -ForegroundColor Green
            
            # Syncplayを起動
            Start-SyncplayAuto
            
            # 起動済みリストに追加
            $LaunchedTimes += $targetTime
            
            Write-Host ""
            Write-Host "[INFO] 起動処理が完了しました。" -ForegroundColor Green
            Write-Host "[INFO] 起動済み時刻: $($LaunchedTimes -join ', ')" -ForegroundColor Gray
            Write-Host "======================================================"
            Write-Host ""
        }
    }
    
    # 1秒待機
    Start-Sleep -Seconds 1
    
    # 現在時刻を表示（オプション）
    # Write-Host "`r現在時刻: $currentTime" -NoNewline
}