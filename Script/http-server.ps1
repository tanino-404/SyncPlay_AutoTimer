# HTTP Server Module for SyncPlay AutoTimer v2.0
# Purpose: MESH button command reception and distribution
# Author: University of Osaka i-CHiLD (Tanino with Claude Sonnet 4.5)

#================================================================================
# HTTPサーバー管理
#================================================================================

# グローバル変数
$script:HttpListener = $null
$script:HttpServerRunning = $false
$script:ServerJob = $null

#================================================================================
# HTTPサーバー起動関数
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

        # HttpListenerを作成
        $script:HttpListener = New-Object System.Net.HttpListener
        $script:HttpListener.Prefixes.Add("http://+:${Port}/")
        $script:HttpListener.Start()
        $script:HttpServerRunning = $true

        Log-Message "HTTPサーバーが起動しました (Port: $Port)" "SUCCESS"

        # 非同期でリクエストを処理
        $script:ServerJob = Start-Job -ScriptBlock {
            param($Listener, $OnToggle, $OnQuit, $OnRestart, $OnStatus)

            while ($Listener.IsListening) {
                try {
                    # リクエストを受信（タイムアウト付き）
                    $contextTask = $Listener.GetContextAsync()
                    $timeout = [System.TimeSpan]::FromSeconds(1)

                    if ($contextTask.Wait($timeout)) {
                        $context = $contextTask.Result
                        $request = $context.Request
                        $response = $context.Response

                        # エンドポイント判定
                        $endpoint = $request.Url.AbsolutePath
                        $method = $request.HttpMethod

                        $responseText = ""
                        $statusCode = 200

                        switch ($endpoint) {
                            "/api/toggle" {
                                if ($method -eq "POST") {
                                    & $OnToggle
                                    $responseText = '{"status":"success","action":"toggle","message":"再生/停止コマンドを受信しました"}'
                                } else {
                                    $statusCode = 405
                                    $responseText = '{"status":"error","message":"Method Not Allowed"}'
                                }
                            }
                            "/api/quit" {
                                if ($method -eq "POST") {
                                    & $OnQuit
                                    $responseText = '{"status":"success","action":"quit","message":"終了コマンドを受信しました"}'
                                } else {
                                    $statusCode = 405
                                    $responseText = '{"status":"error","message":"Method Not Allowed"}'
                                }
                            }
                            "/api/restart" {
                                if ($method -eq "POST") {
                                    & $OnRestart
                                    $responseText = '{"status":"success","action":"restart","message":"再起動コマンドを受信しました"}'
                                } else {
                                    $statusCode = 405
                                    $responseText = '{"status":"error","message":"Method Not Allowed"}'
                                }
                            }
                            "/api/status" {
                                if ($method -eq "GET") {
                                    $status = & $OnStatus
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
                    }
                }
                catch {
                    # エラーは無視（サーバー継続）
                }
            }
        } -ArgumentList $script:HttpListener, $OnToggle, $OnQuit, $OnRestart, $OnStatus

        return $true
    }
    catch {
        Log-Message "HTTPサーバーの起動に失敗しました: $_" "ERROR"
        return $false
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

        if ($script:ServerJob) {
            Stop-Job -Job $script:ServerJob -ErrorAction SilentlyContinue
            Remove-Job -Job $script:ServerJob -ErrorAction SilentlyContinue
            $script:ServerJob = $null
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
                $response = Invoke-RestMethod -Uri $uri -Method Post -TimeoutSec ($TimeoutMs / 1000)

                if ($response.status -eq "success") {
                    Log-Message "コマンド送信成功: $ip → $Command" "SUCCESS"
                    $successCount++
                    $success = $true
                    break
                }
            }
            catch {
                if ($i -eq $Retry) {
                    Log-Message "コマンド送信失敗: $ip → $Command (リトライ終了)" "WARNING"
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

Export-ModuleMember -Function Start-HttpServer, Stop-HttpServer, Send-CommandToClients
