using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace v2;

/// <summary>
/// ネットワーク経由での複数PC間同期制御
/// ServerMode=true の場合のみ、本機のコマンドを別機へHTTP POST転送する
/// </summary>
public class NetworkSyncManager : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly List<string> _clientIPs;
    private readonly int _httpPort;
    private readonly bool _isServerMode;
    private bool _isEnabled;

    public NetworkSyncManager(ConfigManager configManager)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2) // タイムアウト: 2秒
        };

        // config.ini から設定を読み込み
        _httpPort = configManager.HttpServerPort;
        _isServerMode = configManager.ServerMode;

        // ClientIPList を読み込み（カンマ区切り）
        string? clientIPList = configManager.GetValue("Network", "ClientIPList");
        _clientIPs = new List<string>();

        if (!string.IsNullOrWhiteSpace(clientIPList))
        {
            _clientIPs = clientIPList
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .ToList();
        }

        // ServerMode=true かつ ClientIPList が存在する場合のみ有効化
        _isEnabled = _isServerMode && _clientIPs.Any();

        if (_isServerMode)
        {
            if (_isEnabled)
            {
                Console.WriteLine($"[NetworkSyncManager] ServerMode enabled. Broadcasting to {_clientIPs.Count} client(s):");
                foreach (var ip in _clientIPs)
                {
                    Console.WriteLine($"  - {ip}:{_httpPort}");
                }
            }
            else
            {
                Console.WriteLine("[NetworkSyncManager] ServerMode enabled, but no client IPs configured.");
            }
        }
        else
        {
            Console.WriteLine("[NetworkSyncManager] ClientMode. Network sync disabled.");
        }
    }

    /// <summary>
    /// 指定エンドポイントに対して全クライアントへHTTP POSTを送信
    /// ServerMode=false の場合は何もしない
    /// </summary>
    /// <param name="endpoint">エンドポイント (例: "/api/toggle")</param>
    /// <param name="additionalData">追加データ（オプション）</param>
    public async Task BroadcastCommandAsync(string endpoint, Dictionary<string, object>? additionalData = null)
    {
        if (!_isEnabled)
        {
            // ServerMode=false またはクライアントが設定されていない場合は何もしない
            return;
        }

        // 送信データを準備
        var requestData = new Dictionary<string, object>
        {
            { "source", "master" },
            { "timestamp", DateTime.UtcNow.ToString("o") }
        };

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                requestData[kvp.Key] = kvp.Value;
            }
        }

        string jsonContent = JsonConvert.SerializeObject(requestData);

        // 全クライアントへ並列送信
        var tasks = _clientIPs.Select(ip => SendCommandToClientAsync(ip, endpoint, jsonContent));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            // 個々のエラーは SendCommandToClientAsync 内で処理されるため、ここでは全体的なエラーのみ記録
            Console.WriteLine($"[NetworkSyncManager] Broadcast failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 個別クライアントへHTTP POSTを送信
    /// </summary>
    private async Task SendCommandToClientAsync(string ip, string endpoint, string jsonContent)
    {
        string url = $"http://{ip}:{_httpPort}{endpoint}";

        try
        {
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[NetworkSyncManager] ✓ {ip} → {endpoint} (HTTP {(int)response.StatusCode})");
            }
            else
            {
                Console.WriteLine($"[NetworkSyncManager] ✗ {ip} → {endpoint} (HTTP {(int)response.StatusCode})");
            }
        }
        catch (HttpRequestException ex)
        {
            // ネットワークエラー（接続失敗、タイムアウト等）
            Console.WriteLine($"[NetworkSyncManager] ✗ {ip} → {endpoint} (Connection failed: {ex.Message})");
        }
        catch (TaskCanceledException)
        {
            // タイムアウト
            Console.WriteLine($"[NetworkSyncManager] ✗ {ip} → {endpoint} (Timeout)");
        }
        catch (Exception ex)
        {
            // その他のエラー
            Console.WriteLine($"[NetworkSyncManager] ✗ {ip} → {endpoint} (Error: {ex.Message})");
        }
    }

    /// <summary>
    /// toggle コマンドをブロードキャスト（ServerMode=true の場合のみ）
    /// </summary>
    public async Task BroadcastToggleAsync()
    {
        if (_isEnabled)
        {
            Console.WriteLine("[NetworkSyncManager] Broadcasting toggle command...");
        }
        await BroadcastCommandAsync("/api/toggle");
    }

    /// <summary>
    /// quit コマンドをブロードキャスト（ServerMode=true の場合のみ）
    /// </summary>
    public async Task BroadcastQuitAsync()
    {
        if (_isEnabled)
        {
            Console.WriteLine("[NetworkSyncManager] Broadcasting quit command...");
        }
        await BroadcastCommandAsync("/api/quit");
    }

    /// <summary>
    /// restart コマンドをブロードキャスト（ServerMode=true の場合のみ）
    /// </summary>
    public async Task BroadcastRestartAsync()
    {
        if (_isEnabled)
        {
            Console.WriteLine("[NetworkSyncManager] Broadcasting restart command...");
        }
        await BroadcastCommandAsync("/api/restart");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
