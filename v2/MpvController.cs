using System.Diagnostics;
using System.IO.Pipes;
using Newtonsoft.Json;

namespace v2;

/// <summary>
/// MPV インスタンスの制御（IPC JSON-RPC 通信）
/// </summary>
public class MpvController : IDisposable
{
    private const string DefaultMpvExePath = @"C:\Program Files\mpv\mpv.exe";

    private class MpvInstance
    {
        public int DisplayIndex { get; set; }
        public Process? Process { get; set; }
        public string PipeName { get; set; } = "";
        public bool IsRunning => Process != null && !Process.HasExited;
    }

    private Dictionary<int, MpvInstance> _instances = new();
    private readonly ProcessManager _processManager;
    private string _mpvExePath;

    public MpvController(ProcessManager processManager, ConfigManager? configManager = null)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

        // ConfigManager から設定を読み込む、なければデフォルト値を使用
        if (configManager != null)
        {
            _mpvExePath = configManager.GetValue("Syncplay", "MpvPath", DefaultMpvExePath) ?? DefaultMpvExePath;
        }
        else
        {
            _mpvExePath = DefaultMpvExePath;
        }
    }

    /// <summary>
    /// MPV インスタンスを起動（指定ディスプレイで動画再生）
    /// </summary>
    public bool StartInstance(int displayIndex, string videoPath)
    {
        if (_instances.ContainsKey(displayIndex))
        {
            Console.WriteLine($"[MpvController] Instance for display {displayIndex} already exists.");
            return false;
        }

        try
        {
            if (!File.Exists(_mpvExePath))
            {
                throw new FileNotFoundException($"MPV executable not found: {_mpvExePath}");
            }

            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException($"Video file not found: {videoPath}");
            }

            // IPC パイプ名（通常は mpvpipe で固定、複数インスタンスの場合はサフィックスを追加）
            string pipeName = displayIndex == 0 ? "mpvpipe" : $"mpvpipe{displayIndex}";

            Console.WriteLine($"[MpvController] Starting MPV instance for display {displayIndex}...");
            Console.WriteLine($"  Video: {videoPath}");
            Console.WriteLine($"  IPC Pipe: {pipeName}");

            // MPV 起動コマンド
            // --input-ipc-server=\\.\pipe\PIPE_NAME で IPC パイプ設定
            // --screen=0,1,... で表示画面指定
            string mpvArgs = $"--input-ipc-server=\\\\.\\pipe\\{pipeName} --screen={displayIndex} \"{videoPath}\"";

            var process = _processManager.StartManagedProcess(
                _mpvExePath,
                arguments: mpvArgs
            );

            var instance = new MpvInstance
            {
                DisplayIndex = displayIndex,
                Process = process,
                PipeName = pipeName
            };

            _instances[displayIndex] = instance;

            // IPC パイプ接続待機（1秒）
            System.Threading.Thread.Sleep(1000);

            Console.WriteLine($"[MpvController] MPV instance started for display {displayIndex} (PID: {process.Id})");

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MpvController] Failed to start instance for display {displayIndex}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// MPV インスタンスに JSON-RPC コマンドを送信
    /// </summary>
    public bool SendCommand(int displayIndex, string command, params object?[] args)
    {
        if (!_instances.TryGetValue(displayIndex, out var instance))
        {
            Console.WriteLine($"[MpvController] Instance for display {displayIndex} not found.");
            return false;
        }

        if (!instance.IsRunning)
        {
            Console.WriteLine($"[MpvController] Instance for display {displayIndex} is not running.");
            return false;
        }

        try
        {
            // JSON-RPC リクエスト構築
            var jsonRequest = new
            {
                jsonrpc = "2.0",
                method = "command",
                @params = new object[] { command }.Concat(args ?? Array.Empty<object?>()).ToArray(),
                id = 1
            };

            string jsonString = JsonConvert.SerializeObject(jsonRequest) + "\n";

            // IPC パイプで送信
            using (var pipeClient = new NamedPipeClientStream(".", instance.PipeName, PipeDirection.InOut))
            {
                pipeClient.Connect(2000); // 2秒タイムアウト

                var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                writer.WriteLine(jsonString);

                // レスポンス受信（タイムアウト: 1秒）
                pipeClient.ReadTimeout = 1000;
                var reader = new StreamReader(pipeClient);
                string? response = reader.ReadLine();

                Console.WriteLine($"[MpvController] Command sent to display {displayIndex}: {command}");
                if (response != null)
                {
                    Console.WriteLine($"[MpvController] Response: {response}");
                }

                return true;
            }
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"[MpvController] Timeout communicating with display {displayIndex}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MpvController] Error sending command to display {displayIndex}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 再生/一時停止トグル
    /// </summary>
    public bool TogglePlayPause(int displayIndex)
    {
        return SendCommand(displayIndex, "cycle", "pause");
    }

    /// <summary>
    /// 再生速度設定
    /// </summary>
    public bool SetPlaybackSpeed(int displayIndex, double speed)
    {
        return SendCommand(displayIndex, "set", "speed", speed);
    }

    /// <summary>
    /// シーク（秒単位）
    /// </summary>
    public bool Seek(int displayIndex, double seconds, string mode = "absolute")
    {
        return SendCommand(displayIndex, "seek", seconds, mode);
    }

    /// <summary>
    /// 全インスタンスを停止
    /// </summary>
    public void StopAll()
    {
        var displayIndices = _instances.Keys.ToList();

        foreach (var displayIndex in displayIndices)
        {
            StopInstance(displayIndex);
        }

        Console.WriteLine("[MpvController] All MPV instances stopped.");
    }

    /// <summary>
    /// インスタンスを停止
    /// </summary>
    public void StopInstance(int displayIndex)
    {
        if (!_instances.TryGetValue(displayIndex, out var instance))
        {
            return;
        }

        try
        {
            if (instance.IsRunning)
            {
                Console.WriteLine($"[MpvController] Stopping instance for display {displayIndex}");
                _processManager.TerminateProcess(instance.Process!.Id);
            }

            _instances.Remove(displayIndex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MpvController] Error stopping instance for display {displayIndex}: {ex.Message}");
        }
    }

    /// <summary>
    /// ステータス確認
    /// </summary>
    public void PrintStatus()
    {
        Console.WriteLine("[MpvController] MPV Instances:");
        if (_instances.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var kvp in _instances)
        {
            var instance = kvp.Value;
            Console.WriteLine($"  Display {kvp.Key}: {(instance.IsRunning ? "Running" : "Stopped")} (PID: {instance.Process?.Id})");
        }
    }

    public void Dispose()
    {
        StopAll();
        GC.SuppressFinalize(this);
    }

    ~MpvController()
    {
        StopAll();
    }
}
