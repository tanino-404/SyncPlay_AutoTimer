using System.Net;
using Newtonsoft.Json;

namespace v2;

/// <summary>
/// System.Net.HttpListener を使用した HTTP サーバー実装
/// </summary>
public class HttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _listenerTask;

    // イベントハンドラ
    public event EventHandler<HttpEventArgs>? OnToggle;
    public event EventHandler<HttpEventArgs>? OnQuit;
    public event EventHandler<HttpEventArgs>? OnRestart;
    public event EventHandler<HttpEventArgs>? OnStatus;

    public bool IsRunning { get; private set; } = false;

    public HttpServer(int port = 8080)
    {
        _port = port;
        _listener = new HttpListener();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// HTTP サーバーを起動
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            Console.WriteLine("[HttpServer] Already running.");
            return;
        }

        try
        {
            // リッスンするプレフィックスを追加
            string prefix = $"http://+:{_port}/";
            _listener.Prefixes.Add(prefix);

            _listener.Start();
            IsRunning = true;

            Console.WriteLine($"[HttpServer] Started on {prefix}");
            Console.WriteLine("[HttpServer] Listening for HTTP requests on:");
            Console.WriteLine($"  - http://localhost:{_port}/api/toggle");
            Console.WriteLine($"  - http://localhost:{_port}/api/quit");
            Console.WriteLine($"  - http://localhost:{_port}/api/restart");
            Console.WriteLine($"  - GET http://localhost:{_port}/api/status");

            // 別スレッドでリクエスト受信
            _listenerTask = Task.Run(() => ListenForRequests(_cancellationTokenSource.Token));
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"[HttpServer] Failed to start: {ex.Message}");
            if (ex.ErrorCode == 5)
            {
                Console.Error.WriteLine("  → Administrator privileges may be required.");
            }
            IsRunning = false;
            throw;
        }
    }

    /// <summary>
    /// リクエスト受信ループ
    /// </summary>
    private async Task ListenForRequests(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    // リクエスト受信（真の非同期: GetContextAsync使用）
                    context = await _listener.GetContextAsync();
                }
                catch (ObjectDisposedException)
                {
                    // _listener.Stop() 呼び出し時に発生 → ループ終了
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await HandleRequest(context);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[HttpServer] Error handling request: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HttpServer] Listener error: {ex.Message}");
        }
    }

    /// <summary>
    /// リクエスト処理
    /// </summary>
    private async Task HandleRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            Console.WriteLine($"[HttpServer] {request.HttpMethod} {request.RawUrl}");

            // ルート解析
            string path = request.Url?.AbsolutePath ?? "/";

            // JSON レスポンス構造
            string responseJson = "";
            int statusCode = 200;

            switch (path.ToLower())
            {
                case "/api/toggle":
                    (statusCode, responseJson) = HandlePostEndpoint(request, path, "toggle", OnToggle);
                    break;

                case "/api/quit":
                    (statusCode, responseJson) = HandlePostEndpoint(request, path, "quit", OnQuit);
                    break;

                case "/api/restart":
                    (statusCode, responseJson) = HandlePostEndpoint(request, path, "restart", OnRestart);
                    break;

                case "/api/status":
                    if (request.HttpMethod == "GET")
                    {
                        OnStatus?.Invoke(this, new HttpEventArgs { Path = path });
                        responseJson = JsonConvert.SerializeObject(new { status = "running", timestamp = DateTime.UtcNow });
                    }
                    else
                    {
                        statusCode = 405;
                        responseJson = JsonConvert.SerializeObject(new { status = "error", message = "Method not allowed. Use GET." });
                    }
                    break;

                default:
                    statusCode = 404;
                    responseJson = JsonConvert.SerializeObject(new { status = "error", message = "Endpoint not found" });
                    break;
            }

            // レスポンス送信
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            Console.WriteLine($"[HttpServer] Response: {statusCode}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HttpServer] Error processing request: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// POST エンドポイントの共通処理（HTTPメソッドチェック → イベント発火 → レスポンス生成）
    /// </summary>
    private (int statusCode, string responseJson) HandlePostEndpoint(
        HttpListenerRequest request, string path, string action, EventHandler<HttpEventArgs>? eventHandler)
    {
        if (request.HttpMethod == "POST")
        {
            eventHandler?.Invoke(this, new HttpEventArgs { Path = path });
            return (200, JsonConvert.SerializeObject(new { status = "success", action, timestamp = DateTime.UtcNow }));
        }
        else
        {
            return (405, JsonConvert.SerializeObject(new { status = "error", message = "Method not allowed. Use POST." }));
        }
    }

    /// <summary>
    /// HTTP サーバーを停止
    /// </summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            Console.WriteLine("[HttpServer] Not running.");
            return;
        }

        try
        {
            _cancellationTokenSource.Cancel();
            _listener.Stop();

            if (_listenerTask != null)
            {
                _listenerTask.Wait(TimeSpan.FromSeconds(5));
            }

            IsRunning = false;
            Console.WriteLine("[HttpServer] Stopped.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HttpServer] Error stopping: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// HTTP イベントハンドラ用の EventArgs
/// </summary>
public class HttpEventArgs : EventArgs
{
    public string Path { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
