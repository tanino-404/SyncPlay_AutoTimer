# VLC Controller Module for SyncPlay AutoTimer v2.0
# Purpose: VLC media player control via SendKeys
# Author: University of Osaka i-CHiLD (Tanino with Claude Sonnet 4.5)

#================================================================================
# VLC制御関数
#================================================================================

# VLC制御用のキー送信基本関数
function VLC-Send-Key {
    param(
        [string]$Key
    )

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue

        # VLCプロセスを取得
        $vlcProcess = Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Select-Object -First 1

        if ($vlcProcess) {
            # VLCウィンドウにフォーカス
            Add-Type @"
            using System;
            using System.Runtime.InteropServices;
            public class Win32VLC {
                [DllImport("user32.dll")]
                public static extern bool SetForegroundWindow(IntPtr hWnd);
            }
"@ -ErrorAction SilentlyContinue

            [Win32VLC]::SetForegroundWindow($vlcProcess.MainWindowHandle)

            # 500ms待機
            Start-Sleep -Milliseconds 500

            # キーを送信
            [System.Windows.Forms.SendKeys]::SendWait($Key)
            Log-Message "VLCにキー送信: $Key" "SUCCESS"
            return $true
        } else {
            Log-Message "VLCプロセスが見つかりません" "WARNING"
            return $false
        }
    }
    catch {
        Log-Message "キー送信に失敗しました: $_" "ERROR"
        return $false
    }
}


# 再生/一時停止トグル
function VLC-Send-Play {
    Log-Message "VLC再生/一時停止コマンドを送信中..." "INFO"
    $result = VLC-Send-Key -Key " "  # スペースキー

    if ($result) {
        # グローバル状態を更新
        if ($script:VLCState -eq "playing") {
            $script:VLCState = "paused"
        } else {
            $script:VLCState = "playing"
        }
    }

    return $result
}


# 停止（一時停止と同じ動作）
function VLC-Send-Stop {
    Log-Message "VLC停止コマンドを送信中..." "INFO"
    $result = VLC-Send-Key -Key " "  # スペースキー

    if ($result) {
        $script:VLCState = "paused"
    }

    return $result
}


# 終了
function VLC-Send-Quit {
    Log-Message "VLCを終了しています..." "INFO"

    try {
        # VLCプロセスを強制終了
        $vlcProcesses = Get-Process -Name "vlc" -ErrorAction SilentlyContinue

        if ($vlcProcesses) {
            $vlcProcesses | Stop-Process -Force
            Log-Message "VLCを終了しました" "SUCCESS"
            $script:VLCState = "stopped"
            return $true
        } else {
            Log-Message "VLCプロセスが見つかりません" "WARNING"
            return $false
        }
    }
    catch {
        Log-Message "VLC終了に失敗しました: $_" "ERROR"
        return $false
    }
}


# 状態取得（プロセス存在確認）
function VLC-Get-Status {
    $vlcProcess = Get-Process -Name "vlc" -ErrorAction SilentlyContinue

    if ($vlcProcess) {
        if ($script:VLCState) {
            return $script:VLCState
        } else {
            return "running"
        }
    } else {
        return "stopped"
    }
}


#================================================================================
# エクスポート
#================================================================================

Export-ModuleMember -Function VLC-Send-Key, VLC-Send-Play, VLC-Send-Stop, VLC-Send-Quit, VLC-Get-Status
