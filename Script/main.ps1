# SyncPlay自動起動・自動停止PowerShellスクリプト
# 実行方法: PowerShell -ExecutionPolicy Bypass -File "main.ps1"
# Author: University of Osaka i-CHiLD (Tanino with Claude 4.1 Opus)

#================================================================================
# グローバル変数
#================================================================================

# 起動管理用ハッシュテーブル（起動時刻と停止予定時刻を管理）
$script:LaunchSchedule = @{}

#================================================================================
# 設定項目
#================================================================================

# SyncPlay起動変数
$SyncplayPath = "C:\Program Files (x86)\Syncplay\SyncplayConsole.exe"
$SyncplayServerPath = "C:\Program Files (x86)\Syncplay\syncplayServer.exe"
$PlayerPath = "C:\Program Files\VideoLAN\VLC\vlc.exe"
$ServerIP = "192.168.100.13"
$ServerPort = "8999"
$UserName = "Server"
$RoomName = "Test_Run"
$RoomPassword = ""  # オプション
$VideoFilePath = "..\Video\Terminal.0_Video_250722_v2.mp4"

# 起動時刻のリスト(24時間表記)
$TargetTimes = @("14:16", "00:00", "00:00", "00:00")

# 動作モード切り替え
$ServerMode = $true             # サーバ起動機能
$AutoStopMode = $true           # 自動停止機能
$DebugMode = $false              # デバッグ機能

# 自動停止機能
$AutoStopMinutes = 1           # 起動から何分後に停止するか
$StopServerOnExit = $true       # 最後の停止時にサーバーも停止するか

#================================================================================
# 関数定義
#================================================================================

function Write-ColoredLog {
    param(
        [string]$Message,
        [string]$Level = "INFO",
        [System.ConsoleColor]$Color = "White"
    )
    
    $timestamp = Get-Date -Format "HH:mm:ss"
    
    switch ($Level) {
        "INFO"    { $Color = "Green" }
        "WARNING" { $Color = "Yellow" }
        "ERROR"   { $Color = "Red" }
        "DEBUG"   { $Color = "Cyan" }
        "SUCCESS" { $Color = "Magenta" }
    }
    
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $Color
}

function Stop-SyncplayProcesses {
    param(
        [bool]$StopServer = $false
    )
    
    Write-ColoredLog "プロセスを停止しています..." "INFO"
    
    # SyncplayConsoleを停止
    $syncplayProcesses = Get-Process -Name "SyncplayConsole", "Syncplay" -ErrorAction SilentlyContinue
    if ($syncplayProcesses) {
        $syncplayProcesses | Stop-Process -Force
        Write-ColoredLog "Syncplayを停止しました" "SUCCESS"
    }
    
    # VLCを停止
    $vlcProcess = Get-Process -Name "vlc" -ErrorAction SilentlyContinue
    if ($vlcProcess) {
        $vlcProcess | Stop-Process -Force
        Write-ColoredLog "VLCを停止しました" "SUCCESS"
    }
    
    # サーバーを停止（オプション）
    if ($StopServer) {
        $serverProcess = Get-Process -Name "syncplayServer" -ErrorAction SilentlyContinue
        if ($serverProcess) {
            $serverProcess | Stop-Process -Force
            Write-ColoredLog "Syncplay Serverを停止しました" "SUCCESS"
        }
    }
}

function Start-SyncplayAuto {
    param(
        [string]$ScheduleKey
    )
    
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-ColoredLog "Syncplayを起動します..." "INFO" "Cyan"
    Write-Host "======================================================" -ForegroundColor Cyan
    
    # 既存のプロセスを終了（サーバーは除く）
    Write-ColoredLog "既存のクライアントプロセスを終了しています..." "WARNING"
    Get-Process -Name "SyncplayConsole", "Syncplay" -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    
    # 引数を構築
    $arguments = @(
        "--host", "${ServerIP}:${ServerPort}",
        "--name", "`"${UserName}`"",
        "--room", "`"${RoomName}`"",
        "--player-path", "`"${PlayerPath}`"",
        "--no-store"
    )
    
    if ($RoomPassword) {
        $arguments += "--password"
        $arguments += $RoomPassword
    }
    
    # 動画ファイルを最後に追加
    $arguments += "`"${VideoFilePath}`""
    
    if ($DebugMode) {
        Write-ColoredLog "コマンド: $SyncplayPath" "DEBUG"
        Write-ColoredLog "引数: $($arguments -join ' ')" "DEBUG"
    }
    
    # SyncPlayを起動
    try {
        $process = Start-Process -FilePath "${SyncplayPath}" -ArgumentList "${arguments}" -PassThru
        Start-Sleep -Seconds 3
        
        if ($process.HasExited) {
            Write-ColoredLog "Syncplayが起動に失敗しました。" "ERROR"
            
            # 代替方法: COM オブジェクトを使用
            Write-ColoredLog "代替方法で再試行します..." "WARNING"
            $shell = New-Object -ComObject Shell.Application
            $shell.ShellExecute($SyncplayPath, ($arguments -join ' '))
        } else {
            Write-ColoredLog "Syncplayが正常に起動しました (PID: $($process.Id))" "SUCCESS"
            
            # 自動停止が有効な場合、停止時刻を記録
            if ($AutoStopMode) {
                $stopTime = (Get-Date).AddMinutes($AutoStopMinutes)
                $script:LaunchSchedule[$ScheduleKey] = @{
                    LaunchTime = Get-Date
                    StopTime = $stopTime
                    Stopped = $false
                }
                
                Write-ColoredLog "自動停止予定時刻: $($stopTime.ToString('HH:mm:ss'))" "INFO"
            }
            
            # VLCが起動するまで待機
            Start-Sleep -Seconds 3
        }
    }
    catch {
        Write-ColoredLog "起動中にエラーが発生しました: $_" "ERROR"
    }
}

function Start-SyncplayServer {
    Write-ColoredLog "Syncplay Serverを起動しています..." "INFO"
    
    # 既存のサーバープロセスを確認
    $existingServer = Get-Process -Name "syncplayServer" -ErrorAction SilentlyContinue
    if ($existingServer) {
        Write-ColoredLog "Syncplay Serverは既に起動しています (PID: $($existingServer.Id))" "WARNING"
        return $true
    }
    
    try {
        $process = Start-Process -FilePath "${SyncplayServerPath}" -PassThru
        Start-Sleep -Seconds 5
        
        if ($process.HasExited) {
            Write-ColoredLog "Syncplay Serverが起動に失敗しました。" "ERROR"
            return $false
        } else {
            Write-ColoredLog "Syncplay Serverが正常に起動しました (PID: $($process.Id))" "SUCCESS"
            return $true
        }
    }
    catch {
        Write-ColoredLog "サーバー起動中にエラーが発生しました: $_" "ERROR"
        return $false
    }
}

function Check-AutoStop {
    if (-not $AutoStopMode) {
        return
    }
    
    $currentTime = Get-Date
    $allStopped = $true
    
    foreach ($key in $script:LaunchSchedule.Keys) {
        $schedule = $script:LaunchSchedule[$key]
        
        if (-not $schedule.Stopped -and $currentTime -ge $schedule.StopTime) {
            Write-Host ""
            Write-Host "======================================================" -ForegroundColor Cyan
            Write-ColoredLog "自動停止時刻になりました [$key]" "INFO"
            Write-Host "======================================================" -ForegroundColor Cyan
            
            # プロセスを停止
            Stop-SyncplayProcesses -StopServer:$StopServerOnExit
            
            # 停止済みフラグを設定
            $schedule.Stopped = $true
            
            $duration = [Math]::Round(($currentTime - $schedule.LaunchTime).TotalMinutes, 1)
            Write-ColoredLog "実行時間: $duration 分" "INFO"
            Write-Host "======================================================" -ForegroundColor Cyan
            Write-Host ""
        }
        
        if (-not $schedule.Stopped) {
            $allStopped = $false
        }
    }
    
    # すべてのスケジュールが停止済みで、サーバー停止が有効な場合
    if ($StopServerOnExit -and $allStopped -and $script:LaunchSchedule.Count -gt 0) {
        $allTargetsLaunched = $true
        foreach ($targetTime in $TargetTimes) {
            if ($targetTime -notin $script:LaunchSchedule.Keys) {
                $allTargetsLaunched = $false
                break
            }
        }
        
        if ($allTargetsLaunched) {
            Write-ColoredLog "すべてのスケジュールが完了しました。サーバーを停止します..." "INFO"
            Stop-SyncplayProcesses -StopServer:$true
            
            # スクリプトを終了
            Write-Host ""
            Write-ColoredLog "すべての処理が完了しました。スクリプトを終了します。" "SUCCESS"
            Start-Sleep -Seconds 3
            exit
        }
    }
}

function Show-Status {
    if ($script:LaunchSchedule.Count -eq 0) {
        return
    }
    
    $currentTime = Get-Date
    Write-Host "`r" -NoNewline
    Write-Host "現在時刻: $($currentTime.ToString('HH:mm:ss')) | " -NoNewline -ForegroundColor Gray
    
    $activeCount = 0
    foreach ($key in $script:LaunchSchedule.Keys) {
        $schedule = $script:LaunchSchedule[$key]
        if (-not $schedule.Stopped) {
            $activeCount++
            $remaining = [Math]::Round(($schedule.StopTime - $currentTime).TotalMinutes, 1)
            if ($remaining -gt 0) {
                Write-Host "[$key- 残り ${remaining}分] " -NoNewline -ForegroundColor Cyan
            }
        }
    }
    
    if ($activeCount -eq 0) {
        Write-Host "待機中..." -NoNewline -ForegroundColor Gray
    }
}

#================================================================================
# メイン処理
#================================================================================

Clear-Host

Write-Host ""
Write-Host "======================================================"
Write-Host "   Syncplay 自動起動・停止スクリプト (PowerShell版)"
Write-Host "======================================================"
Write-Host ""
Write-Host "  起動予定時刻      : $($TargetTimes -join ', ')"
Write-Host "  サーバーIP        : ${ServerIP}:${ServerPort}"
Write-Host "  ルーム名          : $RoomName"
Write-Host "  ユーザー名        : $UserName"
Write-Host "  動画ファイルパス  : $(Split-Path -Leaf $VideoFilePath)"
Write-Host ""

if($ServerMode){
    Write-Host "  サーバ-自動起動モード      : 有効" -ForegroundColor Cyan
} else{
    Write-Host "  サーバ-自動起動モード      : 無効" -ForegroundColor Gray
}
if ($AutoStopMode) {
    Write-Host "  動画再生-自動停止モード    : 有効(${AutoStopMinutes}分後)" -ForegroundColor Cyan
    Write-Host "  サーバー-自動停止モード    : $(if($StopServerOnExit){'有効'}else{'無効'})" -ForegroundColor Cyan
} else {
    Write-Host "  動画再生-自動停止モード    : 無効" -ForegroundColor Gray
}
if($DebugMode){
    Write-Host "  デバッグモード             : 有効" -ForegroundColor Cyan
}
else {
    Write-Host "  デバッグモード             : 無効" -ForegroundColor Gray
}

Write-Host ""

# ファイル存在チェック
if (-not (Test-Path $SyncplayPath)) {
    Write-ColoredLog "Syncplay.exeが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}

if (-not (Test-Path $PlayerPath)) {
    Write-ColoredLog "ビデオプレイヤーが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}

if (-not (Test-Path $VideoFilePath)) {
    Write-ColoredLog "動画ファイルが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}

# SyncPlay Serverを起動
if ($ServerMode) {
    if (Start-SyncplayServer) {
        Write-Host ""
        Write-ColoredLog "指定時刻になるまで待機します..." "INFO"
        Write-Host "(Ctrl+C で中止できます)" -ForegroundColor Gray
    } else {
        Write-ColoredLog "サーバーの起動に失敗しました。続行しますか？ (Y/N)" "WARNING"
        $continue = Read-Host
        if ($continue -ne "Y" -and $continue -ne "y") {
            exit
        }
    }
}

# メインループ
while ($true) {
    $currentTime = Get-Date -Format "HH:mm"
    
    # 起動時刻のチェック
    foreach ($targetTime in $TargetTimes) {
        if ($DebugMode) {
            $targetTime = Get-Date -Format "HH:mm"
        }
        
        if ($currentTime -eq $targetTime -and $targetTime -notin $script:LaunchSchedule.Keys) {
            Write-Host ""
            Write-ColoredLog "$currentTime になりました。" "INFO"
            
            # Syncplayを起動
            Start-SyncplayAuto -ScheduleKey $targetTime
            
            Write-Host ""
            Write-ColoredLog "起動処理が完了しました。" "SUCCESS"
            Write-ColoredLog "起動済み時刻: $($script:LaunchSchedule.Keys -join ', ')" "INFO"
            Write-Host "======================================================" -ForegroundColor Cyan
            Write-Host ""
        }
    }
    
    # 自動停止のチェック
    Check-AutoStop
    
    # ステータス表示（オプション）
    Show-Status
    
    # 1秒待機
    Start-Sleep -Seconds 1
}