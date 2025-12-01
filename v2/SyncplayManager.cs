using System.Diagnostics;

namespace v2;

/// <summary>
/// Syncplay Server/Client の起動・管理
/// </summary>
public class SyncplayManager : IDisposable
{
    // デフォルト設定（config.ini で上書き可能）
    private const string DefaultSyncplayServerPath = @"C:\Program Files (x86)\Syncplay\syncplayServer.exe";
    private const string DefaultSyncplayClientPath = @"C:\Program Files (x86)\Syncplay\SyncplayConsole.exe";
    private const string DefaultServerIP = "192.168.100.13";
    private const int DefaultServerPort = 8999;
    private const string DefaultUserName = "Server";
    private const string DefaultRoomName = "Test_Run";
    private const string DefaultRoomPassword = "";
    private const string DefaultVideoFilePath = @"..\Video\Terminal0_JP_Video_R_250915_v1.mp4";

    // 実際の設定値（ConfigManager から読み込まれる）
    private string _syncplayServerPath;
    private string _syncplayClientPath;
    private string _serverIP;
    private int _serverPort;
    private string _userName;
    private string _roomName;
    private string _roomPassword;
    private string _videoFilePath;

    // 管理するプロセス
    private Process? _serverProcess;
    private Process? _clientProcess;

    private readonly ProcessManager _processManager;

    public bool IsServerRunning => _serverProcess != null && !_serverProcess.HasExited;
    public bool IsClientRunning => _clientProcess != null && !_clientProcess.HasExited;

    public SyncplayManager(ProcessManager processManager, ConfigManager? configManager = null)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

        // ConfigManager から設定を読み込む、なければデフォルト値を使用
        if (configManager != null)
        {
            _syncplayServerPath = configManager.GetValue("Syncplay", "SyncplayServerPath", DefaultSyncplayServerPath) ?? DefaultSyncplayServerPath;
            _syncplayClientPath = configManager.GetValue("Syncplay", "SyncplayClientPath", DefaultSyncplayClientPath) ?? DefaultSyncplayClientPath;
            _serverIP = configManager.GetValue("Syncplay", "ServerIP", DefaultServerIP) ?? DefaultServerIP;
            _serverPort = configManager.GetInt("Syncplay", "ServerPort", DefaultServerPort);
            _userName = configManager.GetValue("Syncplay", "UserName", DefaultUserName) ?? DefaultUserName;
            _roomName = configManager.GetValue("Syncplay", "RoomName", DefaultRoomName) ?? DefaultRoomName;
            _roomPassword = configManager.GetValue("Syncplay", "RoomPassword", DefaultRoomPassword) ?? "";
            _videoFilePath = configManager.GetValue("Player", "VideoFilePath", DefaultVideoFilePath) ?? DefaultVideoFilePath;
        }
        else
        {
            _syncplayServerPath = DefaultSyncplayServerPath;
            _syncplayClientPath = DefaultSyncplayClientPath;
            _serverIP = DefaultServerIP;
            _serverPort = DefaultServerPort;
            _userName = DefaultUserName;
            _roomName = DefaultRoomName;
            _roomPassword = DefaultRoomPassword;
            _videoFilePath = DefaultVideoFilePath;
        }
    }

    /// <summary>
    /// Syncplay Server を起動
    /// </summary>
    public bool StartServer()
    {
        if (IsServerRunning)
        {
            Console.WriteLine("[SyncplayManager] Server is already running.");
            return false;
        }

        try
        {
            if (!File.Exists(_syncplayServerPath))
            {
                throw new FileNotFoundException($"Syncplay Server executable not found: {_syncplayServerPath}");
            }

            Console.WriteLine("[SyncplayManager] Starting Syncplay Server...");
            Console.WriteLine($"  Path: {_syncplayServerPath}");
            Console.WriteLine($"  Binding to: {_serverIP}:{_serverPort}");

            // サーバー起動：--port, --password, --motd などのオプション可能
            string serverArgs = $"--port {_serverPort}";
            if (!string.IsNullOrEmpty(_roomPassword))
            {
                serverArgs += $" --password \"{_roomPassword}\"";
            }

            _serverProcess = _processManager.StartManagedProcess(
                _syncplayServerPath,
                arguments: serverArgs
            );

            Console.WriteLine("[SyncplayManager] Syncplay Server started (PID: {0})", _serverProcess.Id);

            // サーバー起動待機（2秒）
            System.Threading.Thread.Sleep(2000);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SyncplayManager] Failed to start server: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Syncplay Client を起動
    /// </summary>
    public bool StartClient()
    {
        if (IsClientRunning)
        {
            Console.WriteLine("[SyncplayManager] Client is already running.");
            return false;
        }

        try
        {
            if (!File.Exists(_syncplayClientPath))
            {
                throw new FileNotFoundException($"Syncplay Client executable not found: {_syncplayClientPath}");
            }

            Console.WriteLine("[SyncplayManager] Starting Syncplay Client...");
            Console.WriteLine($"  Path: {_syncplayClientPath}");
            Console.WriteLine($"  Connecting to: {_serverIP}:{_serverPort}");
            Console.WriteLine($"  User: {_userName}");
            Console.WriteLine($"  Room: {_roomName}");
            Console.WriteLine($"  Video: {_videoFilePath}");

            // クライアント起動コマンド例
            // SyncplayConsole.exe --host SERVER_IP --port PORT --name USERNAME --room ROOM_NAME "video_file.mp4"
            string clientArgs = $"--host {_serverIP} --port {_serverPort} --name {_userName} --room {_roomName}";

            if (!string.IsNullOrEmpty(_roomPassword))
            {
                clientArgs += $" --password \"{_roomPassword}\"";
            }

            if (File.Exists(_videoFilePath))
            {
                clientArgs += $" \"{_videoFilePath}\"";
            }
            else
            {
                Console.WriteLine($"[SyncplayManager] Warning: Video file not found: {_videoFilePath}");
            }

            _clientProcess = _processManager.StartManagedProcess(
                _syncplayClientPath,
                arguments: clientArgs
            );

            Console.WriteLine("[SyncplayManager] Syncplay Client started (PID: {0})", _clientProcess.Id);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SyncplayManager] Failed to start client: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Syncplay Server を停止
    /// </summary>
    public void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                Console.WriteLine("[SyncplayManager] Stopping Syncplay Server (PID: {0})", _serverProcess.Id);
                _processManager.TerminateProcess(_serverProcess.Id);
                _serverProcess = null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SyncplayManager] Error stopping server: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Syncplay Client を停止
    /// </summary>
    public void StopClient()
    {
        if (_clientProcess != null && !_clientProcess.HasExited)
        {
            try
            {
                Console.WriteLine("[SyncplayManager] Stopping Syncplay Client (PID: {0})", _clientProcess.Id);
                _processManager.TerminateProcess(_clientProcess.Id);
                _clientProcess = null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SyncplayManager] Error stopping client: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Syncplay Server/Client を両方停止
    /// </summary>
    public void StopAll()
    {
        StopClient();
        StopServer();
        Console.WriteLine("[SyncplayManager] Syncplay stopped.");
    }

    /// <summary>
    /// ステータス確認
    /// </summary>
    public void PrintStatus()
    {
        Console.WriteLine("[SyncplayManager] Status:");
        Console.WriteLine($"  Server: {(IsServerRunning ? "Running" : "Stopped")} {(IsServerRunning ? $"(PID: {_serverProcess?.Id})" : "")}");
        Console.WriteLine($"  Client: {(IsClientRunning ? "Running" : "Stopped")} {(IsClientRunning ? $"(PID: {_clientProcess?.Id})" : "")}");
    }

    public void Dispose()
    {
        StopAll();
        GC.SuppressFinalize(this);
    }

    ~SyncplayManager()
    {
        StopAll();
    }
}
