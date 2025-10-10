# HTTP Server Module for SyncPlay AutoTimer v2.0 (FIXED)
# Purpose: MESH button command reception and distribution
# Author: University of Osaka i-CHiLD (Tanino with Claude Sonnet 4.5)

#================================================================================
# HTTPサーバー管理
#================================================================================

# グローバル変数
$script:HttpListener = $null
$script:HttpServerRunning = $false
$script:PendingContextTask = $null

# コールバック関数を保存
$script:OnToggleCallback = $null
$script:OnQuitCallback = $null
$script:OnRestartCallback = $null
$script:OnStatusCallback = $null

#================================================================================
# HTTPサーバー起動関数（同期版）
#================================================================================

function Start-HttpServer {
    param(
        [int]$Port = 8080,
        [scriptblock]$OnToggle = {},
        [scriptblock]$OnQuit = {},
        [scriptblock]$OnRestart = {},
        [scriptblock]$OnStatus = {}
    )

    try {
        # 既存のリスナーをクリーンアップ
        if ($script:HttpListener) {
            Stop-HttpServer
        }

        # コールバック関数を保存
        $script:OnToggleCallback = $OnToggle
        $script:OnQuitCallback = $OnQuit
        $script:OnRestartCallback = $OnRestart
        $script:OnStatusCallback = $OnStatus

        # HttpListenerを作成
        $script:HttpListener = New-Object System.Net.HttpListener

        # localhost限定でリスニング（管理者権限不要）
        $script:HttpListener.Prefixes.Add("http://localhost:${Port}/")
        # 全インターフェースでリスニング（外部からのアクセス許可）
        $script:HttpListener.Prefixes.Add("http://+:${Port}/")

        $script:HttpListener.Start()
        $script:HttpServerRunning = $true

        Log-Message "HTTPサーバーが起動しました (Port: $Port)" "SUCCESS"
        Log-Message "  - localhost:${Port} (ローカルアクセス)" "INFO"
        Log-Message "  - 全インターフェース:${Port} (外部アクセス)" "INFO"

        return $true
    }
    catch {
        Log-Message "HTTPサーバーの起動に失敗しました: $_" "ERROR"
        Log-Message "管理者権限でPowerShellを実行していますか？" "WARNING"
        return $false
    }
}


#================================================================================
# HTTPリクエスト処理（メインループから呼び出す）
#================================================================================

function Process-HttpRequest {
    if (-not $script:HttpListener -or -not $script:HttpListener.IsListening) {
        return
    }

    # 新しいリクエスト待機タスクを開始（初回またはnullの場合のみ）
    if (-not $script:PendingContextTask) {
        $script:PendingContextTask = $script:HttpListener.GetContextAsync()
    }

    # タスクが完了しているかチェック（ノンブロッキング）
    if ($script:PendingContextTask.IsCompleted) {
        try {
            # コンテキストを取得
            $context = $script:PendingContextTask.Result
            $request = $context.Request
            $response = $context.Response

            # 先にタスクをリセット（処理前に次のリクエスト待機開始）
            $script:PendingContextTask = $null

            try {
                # エンドポイント判定
                $endpoint = $request.Url.AbsolutePath
                $method = $request.HttpMethod

                Log-Message "HTTPリクエスト受信: $method $endpoint from $($request.RemoteEndPoint)" "INFO"

                $responseText = ""
                $statusCode = 200

                switch ($endpoint) {
                    "/api/toggle" {
                        if ($method -eq "POST") {
                            # コールバック実行
                            if ($script:OnToggleCallback) {
                                & $script:OnToggleCallback
                            }
                            $responseText = '{"status":"success","action":"toggle","message":"再生/停止コマンドを受信しました"}'
                        } else {
                            $statusCode = 405
                            $responseText = '{"status":"error","message":"Method Not Allowed"}'
                        }
                    }
                    "/api/quit" {
                        if ($method -eq "POST") {
                            if ($script:OnQuitCallback) {
                                & $script:OnQuitCallback
                            }
                            $responseText = '{"status":"success","action":"quit","message":"終了コマンドを受信しました"}'
                        } else {
                            $statusCode = 405
                            $responseText = '{"status":"error","message":"Method Not Allowed"}'
                        }
                    }
                    "/api/restart" {
                        if ($method -eq "POST") {
                            if ($script:OnRestartCallback) {
                                & $script:OnRestartCallback
                            }
                            $responseText = '{"status":"success","action":"restart","message":"再起動コマンドを受信しました"}'
                        } else {
                            $statusCode = 405
                            $responseText = '{"status":"error","message":"Method Not Allowed"}'
                        }
                    }
                    "/api/status" {
                        if ($method -eq "GET") {
                            $status = "stopped"
                            if ($script:OnStatusCallback) {
                                $status = & $script:OnStatusCallback
                            }
                            $responseText = "{`"status`":`"success`",`"vlc_status`":`"$status`"}"
                        } else {
                            $statusCode = 405
                            $responseText = '{"status":"error","message":"Method Not Allowed"}'
                        }
                    }
                    default {
                        $statusCode = 404
                        $responseText = '{"status":"error","message":"Endpoint Not Found"}'
                    }
                }

                # レスポンスを返送
                $response.StatusCode = $statusCode
                $response.ContentType = "application/json; charset=utf-8"
                $buffer = [System.Text.Encoding]::UTF8.GetBytes($responseText)
                $response.ContentLength64 = $buffer.Length
                $response.OutputStream.Write($buffer, 0, $buffer.Length)
                $response.OutputStream.Close()

                Log-Message "HTTPレスポンス送信: $statusCode" "SUCCESS"
            }
            catch {
                # レスポンス処理中のエラー
                Log-Message "HTTPレスポンス処理エラー: $_" "ERROR"
                try {
                    $response.StatusCode = 500
                    $response.Close()
                } catch {}
            }
        }
        catch {
            # コンテキスト取得エラー
            Log-Message "HTTPコンテキスト取得エラー: $_" "ERROR"
        }
    }
}


#================================================================================
# HTTPサーバー停止関数
#================================================================================

function Stop-HttpServer {
    try {
        if ($script:HttpListener) {
            $script:HttpListener.Stop()
            $script:HttpListener.Close()
            $script:HttpListener = $null
            $script:HttpServerRunning = $false
        }

        Log-Message "HTTPサーバーを停止しました" "INFO"
        return $true
    }
    catch {
        Log-Message "HTTPサーバーの停止に失敗しました: $_" "ERROR"
        return $false
    }
}


#================================================================================
# HTTPクライアント: コマンド送信関数
#================================================================================

function Send-CommandToClients {
    param(
        [string]$Command,
        [array]$ClientIPs,
        [int]$Port = 8080,
        [int]$TimeoutMs = 500,
        [int]$Retry = 2
    )

    $successCount = 0
    $failCount = 0

    foreach ($ip in $ClientIPs) {
        $uri = "http://${ip}:${Port}/api/${Command}"
        $success = $false

        for ($i = 0; $i -le $Retry; $i++) {
            try {
                $response = Invoke-RestMethod -Uri $uri -Method Post -TimeoutSec ($TimeoutMs / 1000) -ErrorAction Stop

                if ($response.status -eq "success") {
                    Log-Message "コマンド送信成功: $ip → $Command" "SUCCESS"
                    $successCount++
                    $success = $true
                    break
                }
            }
            catch {
                if ($i -eq $Retry) {
                    Log-Message "コマンド送信失敗: $ip → $Command ($_)" "WARNING"
                    $failCount++
                }
                Start-Sleep -Milliseconds 100
            }
        }
    }

    return @{
        Success = $successCount
        Failed = $failCount
        Total = $ClientIPs.Count
    }
}


#================================================================================
# エクスポート
#================================================================================

Export-ModuleMember -Function Start-HttpServer, Stop-HttpServer, Send-CommandToClients, Process-HttpRequest
