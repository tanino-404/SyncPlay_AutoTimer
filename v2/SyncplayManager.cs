using System.Diagnostics;

namespace v2;

/// <summary>
/// Syncplay Server/Client の起動・管理
/// </summary>
public class SyncplayManager : IDisposable
{
    // Syncplay インストールパス（ハードコード：Phase 3 で config.ini に移行）
    private const string SyncplayServerPath = @"C:\Program Files (x86)\Syncplay\syncplayServer.exe";
    private const string SyncplayClientPath = @"C:\Program Files (x86)\Syncplay\SyncplayConsole.exe";

    // サーバー設定
    private const string ServerIP = "192.168.100.13";
    private const int ServerPort = 8999;

    // クライアント設定
    private const string UserName = "Server";
    private const string RoomName = "Test_Run";
    private const string RoomPassword = "";
    private const string VideoFilePath = @"..\Video\Terminal0_JP_Video_R_250915_v1.mp4";

    // 管理するプロセス
    private Process? _serverProcess;
    private Process? _clientProcess;

    private readonly ProcessManager _processManager;

    public bool IsServerRunning => _serverProcess != null && !_serverProcess.HasExited;
    public bool IsClientRunning => _clientProcess != null && !_clientProcess.HasExited;

    public SyncplayManager(ProcessManager processManager)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
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
            if (!File.Exists(SyncplayServerPath))
            {
                throw new FileNotFoundException($"Syncplay Server executable not found: {SyncplayServerPath}");
            }

            Console.WriteLine("[SyncplayManager] Starting Syncplay Server...");
            Console.WriteLine($"  Path: {SyncplayServerPath}");
            Console.WriteLine($"  Binding to: {ServerIP}:{ServerPort}");

            // サーバー起動：--port, --password, --motd などのオプション可能
            string serverArgs = $"--port {ServerPort}";
            if (!string.IsNullOrEmpty(RoomPassword))
            {
                serverArgs += $" --password \"{RoomPassword}\"";
            }

            _serverProcess = _processManager.StartManagedProcess(
                SyncplayServerPath,
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
            if (!File.Exists(SyncplayClientPath))
            {
                throw new FileNotFoundException($"Syncplay Client executable not found: {SyncplayClientPath}");
            }

            Console.WriteLine("[SyncplayManager] Starting Syncplay Client...");
            Console.WriteLine($"  Path: {SyncplayClientPath}");
            Console.WriteLine($"  Connecting to: {ServerIP}:{ServerPort}");
            Console.WriteLine($"  User: {UserName}");
            Console.WriteLine($"  Room: {RoomName}");
            Console.WriteLine($"  Video: {VideoFilePath}");

            // クライアント起動コマンド例
            // SyncplayConsole.exe --host SERVER_IP --port PORT --name USERNAME --room ROOM_NAME "video_file.mp4"
            string clientArgs = $"--host {ServerIP} --port {ServerPort} --name {UserName} --room {RoomName}";

            if (!string.IsNullOrEmpty(RoomPassword))
            {
                clientArgs += $" --password \"{RoomPassword}\"";
            }

            if (File.Exists(VideoFilePath))
            {
                clientArgs += $" \"{VideoFilePath}\"";
            }
            else
            {
                Console.WriteLine($"[SyncplayManager] Warning: Video file not found: {VideoFilePath}");
            }

            _clientProcess = _processManager.StartManagedProcess(
                SyncplayClientPath,
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
