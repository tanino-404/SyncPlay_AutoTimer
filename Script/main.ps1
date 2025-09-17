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
$VideoFilePath = "..\Video\Terminal0_JP_Video_R_250915_v1.mp4"

# 起動時刻のリスト(24時間表記)
$TargetTimes = @("13:20", "00:00", "00:00", "00:00")

# 動作モード設定
$ServerMode = $true             # サーバ起動機能
$AutoStopMode = $true           # 自動停止機能
$DebugMode = $false             # デバッグ機能
$MinimizeStartMode = $true      # コンソール画面を最小化する機能

# 自動停止機能
$AutoStopMinutes = 1            # 動画再生から停止するまでの時間(秒)

# 自動再生設定
$AutoPlayDelay = 3              # 動画再生ソフトの起動から自動再生までのディレイ(秒)

#================================================================================
# 関数定義
#================================================================================


# ログ出力
function Log-Message {
    param(
        [string]$Message,
        [string]$Level = "INFO",
        [System.ConsoleColor]$Color = "White"
    )

    # 時刻書式を設定
    $timestamp = Get-Date -Format "HH:mm:ss"

    # 文字色を設定
    switch ($Level) {
        "INFO"    { $Color = "Green" }
        "WARNING" { $Color = "Yellow" }
        "ERROR"   { $Color = "Red" }
        "DEBUG"   { $Color = "Cyan" }
        "SUCCESS" { $Color = "Magenta" }
    }

    #出力
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $Color
}


# ウィンドウ最小化
function Minimize-Window {
    $sig = @'
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();
'@
    try {
        Add-Type -MemberDefinition $sig -Name NativeMethods -Namespace Win32 -ErrorAction SilentlyContinue
        $hwnd = [Win32.NativeMethods]::GetConsoleWindow()
        # 6 = SW_MINIMIZE
        [Win32.NativeMethods]::ShowWindow($hwnd, 6) | Out-Null
        Log-Message "コンソールウィンドウを最小化しました" "INFO"
    }
    catch {
        Log-Message "ウィンドウの最小化に失敗しました: $_" "WARNING"
    }
}


# ウィンドウ復元
function Restore-Window {
    try {
        Add-Type -MemberDefinition @'
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();
'@ -Name NativeMethods -Namespace Win32 -ErrorAction SilentlyContinue
        $hwnd = [Win32.NativeMethods]::GetConsoleWindow()
        # 9 = SW_RESTORE
        [Win32.NativeMethods]::ShowWindow($hwnd, 9) | Out-Null
    }
    catch {}
}


# VLCに再生コマンドを送信
function VLC-Send-Play {
    Log-Message "VLCに再生コマンドを送信しています..." "INFO"
    try {
        Add-Type -AssemblyName System.Windows.Forms

        # VLCウィンドウをアクティブにする
        $vlcProcess = Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($vlcProcess) {
            # VLCウィンドウにフォーカス
            Add-Type @"
            using System;
            using System.Runtime.InteropServices;
            public class Win32 {
                [DllImport("user32.dll")]
                public static extern bool SetForegroundWindow(IntPtr hWnd);
                [DllImport("user32.dll")]
                public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            }
"@
            [Win32]::SetForegroundWindow($vlcProcess.MainWindowHandle)

            # 500ms待機
            Start-Sleep -Milliseconds 500

            # スペースキーを送信（再生/一時停止）
            [System.Windows.Forms.SendKeys]::SendWait(" ")
            Log-Message "再生コマンドを送信しました" "SUCCESS"
        }
    }
    catch {
        Log-Message "再生コマンドの送信に失敗しました: $_" "WARNING"
    }
}


# Syncplayサーバーを起動
function Start-SyncplayServer {
    Log-Message "Syncplay Serverを起動しています..." "INFO"
    
    # 既存のサーバープロセスを確認
    $existingServer = Get-Process -Name "syncplayServer" -ErrorAction SilentlyContinue
    if ($existingServer) {
        Log-Message "Syncplay Serverは既に起動しています (PID: $($existingServer.Id))" "WARNING"
        return $true
    }
    
    try {
        # サーバーを最小化ウィンドウで起動
        $process = Start-Process -FilePath "${SyncplayServerPath}" -WindowStyle Minimized -PassThru
        # 1秒待機
        Start-Sleep -Milliseconds 1000
        
        if ($process.HasExited) {
            Log-Message "Syncplay Serverが起動に失敗しました。" "ERROR"
            return $false
        } else {
            Log-Message "Syncplay Serverが正常に起動しました (PID: $($process.Id))" "SUCCESS"
            return $true
        }
    }
    catch {
        Log-Message "サーバー起動中にエラーが発生しました: $_" "ERROR"
        return $false
    }
}


# Syncplay, VLCの自動起動
function Start-Syncplay {
    param(
        [string]$ScheduleKey
    )

    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Log-Message "Syncplayを起動します..." "INFO" "Cyan"
    Write-Host "======================================================" -ForegroundColor Cyan

    # 既存のプロセスを終了（サーバーは除く）
    Log-Message "既存のクライアントプロセスを終了しています..." "WARNING"
    Get-Process -Name "SyncplayConsole", "Syncplay" -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Stop-Process -Force

    # 0.5秒待機
    Start-Sleep -Milliseconds 500

    # 起動コマンドを定義
    $arguments = @(
        "--host", "${ServerIP}:${ServerPort}",
        "--name", "`"${UserName}`"",
        "--room", "`"${RoomName}`"",
        "--player-path", "`"${PlayerPath}`"",
        "--no-store"
    )
    
    # ルームパスワードを定義
    if ($RoomPassword) {
        $arguments += "--password"
        $arguments += $RoomPassword
    }
    
    # 動画ファイルを最後に追加
    $arguments += "`"${VideoFilePath}`""
    
    # ===デバッグ用出力===
    if ($DebugMode) {
        Log-Message "コマンド: $SyncplayPath" "DEBUG"
        Log-Message "引数: $($arguments -join ' ')" "DEBUG"
    }

    # Clientの場合のみ1.5秒ディレイ
    if (!$ServerMode)
    {
        Start-Sleep -Milliseconds 1500
    }
    
    # SyncPlayを起動
    try {
        $process = Start-Process -FilePath "${SyncplayPath}" -ArgumentList "${arguments}" -PassThru
        # 1秒待機
        Start-Sleep -Milliseconds 1000
        
        if ($process.HasExited) {
            Log-Message "Syncplayが起動に失敗しました。" "ERROR"
            # 代替方法: COM オブジェクトを使用
            Log-Message "代替方法で再試行します..." "WARNING"
            $shell = New-Object -ComObject Shell.Application
            $shell.ShellExecute($SyncplayPath, ($arguments -join ' '))
        } else {
            Log-Message "Syncplayが正常に起動しました (PID: $($process.Id))" "SUCCESS"
            
            # VLCが起動するまで待機
            if(!$ServerMode){ 
                $AutoPlayDelay = $AutoPlayDelay - 1.5 
            }
            Log-Message "VLCの起動を待機中... (${AutoPlayDelay}秒)" "INFO"
            Start-Sleep -Seconds $AutoPlayDelay
            
            # 自動再生コマンドをVLCに送信
            VLC-Send-Play
            
            # 自動停止が有効な場合、停止時刻を記録
            if ($AutoStopMode) {
                $stopTime = (Get-Date).AddMinutes($AutoStopMinutes)
                $script:LaunchSchedule[$ScheduleKey] = @{
                    LaunchTime = Get-Date
                    StopTime = $stopTime
                    Stopped = $false
                }
                Log-Message "自動停止予定時刻: $($stopTime.ToString('HH:mm:ss'))" "INFO"
            }
        }
    }
    catch {
        Log-Message "起動中にエラーが発生しました: $_" "ERROR"
    }
}


# Syncplay,VLCの自動停止
function Stop-Syncplay {
    param(
        [bool]$StopServer = $false
    )
    
    Log-Message "プロセスを停止しています..." "INFO"

    # 停止時にウィンドウを最小化
    if ($MinimizeStartMode) {
        Minimize-Window
    }
    
    # SyncplayConsoleを停止
    $syncplayProcesses = Get-Process -Name "SyncplayConsole", "Syncplay" -ErrorAction SilentlyContinue
    if ($syncplayProcesses) {
        $syncplayProcesses | Stop-Process -Force
        Log-Message "Syncplayを停止しました" "SUCCESS"
    }
    
    # VLCを停止
    $vlcProcess = Get-Process -Name "vlc" -ErrorAction SilentlyContinue
    if ($vlcProcess) {
        $vlcProcess | Stop-Process -Force
        Log-Message "VLCを停止しました" "SUCCESS"
    }
    
    # サーバーを停止（オプション）
    if ($StopServer) {
        $serverProcess = Get-Process -Name "syncplayServer" -ErrorAction SilentlyContinue
        if ($serverProcess) {
            $serverProcess | Stop-Process -Force
            Log-Message "Syncplay Serverを停止しました" "SUCCESS"
        }
    }
}


# 自動停止されているかチェック
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
            Log-Message "自動停止時刻になりました [$key]" "INFO"
            Write-Host "======================================================" -ForegroundColor Cyan
            
            # プロセスを停止（ウィンドウ最小化含む）
            Stop-Syncplay -StopServer:$false
            
            # 停止済みフラグを設定
            $schedule.Stopped = $true
            
            $duration = [Math]::Round(($currentTime - $schedule.LaunchTime).TotalMinutes, 1)
            Log-Message "実行時間: $duration 分" "INFO"
            Write-Host "======================================================" -ForegroundColor Cyan
            Write-Host ""
        }
        
        if (-not $schedule.Stopped) {
            $allStopped = $false
        }
    }
    
    # すべてのスケジュールが停止済みで、サーバー停止が有効な場合
    if ($ServerMode -and $AutoStopMode -and $allStopped -and $script:LaunchSchedule.Count -gt 0) {
        $allTargetsLaunched = $true
        foreach ($targetTime in $TargetTimes) {
            if ($targetTime -ne "00:00" -and $targetTime -notin $script:LaunchSchedule.Keys) {
                $allTargetsLaunched = $false
                break
            }
        }
        
        <#if ($allTargetsLaunched) {
            Log-Message "すべてのスケジュールが完了しました。サーバーを停止します..." "INFO"
            Stop-Syncplay -StopServer:$true
            
            # スクリプトを終了
            Write-Host ""
            Log-Message "すべての処理が完了しました。スクリプトを終了します。" "SUCCESS"
            Start-Sleep -Seconds 3
            exit
        }#>
    }
}


# ステータス表示
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

# スタート表示
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

# モード表示
if($ServerMode){
    Write-Host "  サーバ-自動起動モード      : 有効" -ForegroundColor Cyan
} else{
    Write-Host "  サーバ-自動起動モード      : 無効" -ForegroundColor Gray
}
if ($AutoStopMode) {
    Write-Host "  動画再生-自動停止モード    : 有効(${AutoStopMinutes}分後)" -ForegroundColor Cyan
} else {
    Write-Host "  動画再生-自動停止モード    : 無効" -ForegroundColor Gray
}
if ($MinimizeStartMode){
    Write-Host "  画面最小化モード           : 有効" -ForegroundColor Cyan
} else{
    Write-Host "  画面最小化モード           : 無効" -ForegroundColor Cyan
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
    Log-Message "Syncplay.exeが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}
if (-not (Test-Path $PlayerPath)) {
    Log-Message "ビデオプレイヤーが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}
if (-not (Test-Path $VideoFilePath)) {
    Log-Message "動画ファイルが見つかりません。" "ERROR"
    Read-Host "Enterキーを押して終了"
    exit
}

# ウィンドウを最小化
if ($MinimizeStartMode) {
    Minimize-Window
}

# SyncPlay Serverを起動
if ($ServerMode) {
    if (Start-SyncplayServer) {
        Write-Host ""
        Log-Message "指定時刻になるまで待機します..." "INFO"
        Write-Host "(Ctrl+C で中止できます)" -ForegroundColor Gray
    } else {
        Log-Message "サーバーの起動に失敗しました。続行しますか？ (Y/N)" "WARNING"
        $continue = Read-Host
        if ($continue -ne "Y" -and $continue -ne "y") {
            exit
        }
    }
} else {
    Write-Host ""
    Log-Message "指定時刻になるまで待機します..." "INFO"
    Write-Host "(Ctrl+C で中止できます)" -ForegroundColor Gray
}

# メインループ
while ($true) {
    $currentTime = Get-Date -Format "HH:mm"
    
    # 起動時刻のチェック
    foreach ($targetTime in $TargetTimes) {
        if ($DebugMode) {
            $targetTime = Get-Date -Format "HH:mm"
        }
        
        if ($targetTime -ne "00:00" -and $currentTime -eq $targetTime -and $targetTime -notin $script:LaunchSchedule.Keys) {
            Write-Host ""
            Log-Message "$currentTime になりました。" "INFO"
            
            # Syncplayを起動
            Start-Syncplay -ScheduleKey $targetTime
            
            Write-Host ""
            Log-Message "起動処理が完了しました。" "SUCCESS"
            Log-Message "起動済み時刻: $($script:LaunchSchedule.Keys -join ', ')" "INFO"
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