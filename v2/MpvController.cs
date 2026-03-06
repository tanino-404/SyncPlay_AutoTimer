using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Timers;
using Newtonsoft.Json;

namespace v2;

/// <summary>
/// MPV インスタンスの制御（IPC JSON-RPC 通信）
/// </summary>
public class MpvController : IDisposable
{
    // Phase 3.11.2: Windows API for SetWindowPos (TopMost enforcement)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const string DefaultMpvExePath = @"C:\Program Files\mpv\mpv.exe";
    private Dictionary<int, string> _videoFilePaths = new();

    private class MpvInstance
    {
        public int DisplayIndex { get; set; }
        public Process? Process { get; set; }
        public string PipeName { get; set; } = "";
        public bool IsRunning => Process != null && !Process.HasExited;
        public bool IsPlaying { get; set; } = false;
    }

    private Dictionary<int, MpvInstance> _instances = new();
    private readonly ProcessManager _processManager;
    private string _mpvExePath;
    private bool _isGloballyPlaying = false;  // グローバル再生状態フラグ（全インスタンス統一制御用）

    // Phase 3.11.1: 最前面化タイマー
    private System.Timers.Timer? _topMostTimer;
    private readonly int _topMostIntervalMs;
    private readonly int _mpvStartupWaitMs;
    private bool _isTopMostTimerRunning = false;

    public MpvController(ProcessManager processManager, ConfigManager? configManager = null)
    {
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));

        // ConfigManager から設定を読み込む、なければデフォルト値を使用
        if (configManager != null)
        {
            _mpvExePath = configManager.GetValue("Player", "MpvPath", DefaultMpvExePath) ?? DefaultMpvExePath;
            _topMostIntervalMs = configManager.TopMostIntervalMs;
            _mpvStartupWaitMs = configManager.MpvStartupWaitMs;

            // 各ディスプレイの動画パスを読み込み（DisplayCount に基づく）
            for (int i = 0; i < configManager.DisplayCount; i++)
            {
                string? videoPath = configManager.GetValue("Player", $"Display{i}VideoPath");
                if (!string.IsNullOrWhiteSpace(videoPath))
                {
                    // 相対パス解決
                    string? resolvedPath = configManager.ResolveVideoPath(videoPath);
                    if (!string.IsNullOrWhiteSpace(resolvedPath))
                    {
                        _videoFilePaths[i] = resolvedPath;
                        Console.WriteLine($"[MpvController] Display {i} video path: {resolvedPath}");
                    }
                }
            }
        }
        else
        {
            _mpvExePath = DefaultMpvExePath;
            _topMostIntervalMs = 100; // デフォルト値
            _mpvStartupWaitMs = 2000; // デフォルト値
        }

        Console.WriteLine($"[MpvController] TopMost monitoring interval: {_topMostIntervalMs}ms");
    }

    /// <summary>
    /// MPV インスタンスを起動（指定ディスプレイで動画再生、バックグラウンド起動）
    /// </summary>
    public bool StartInstance(int displayIndex, string videoPath)
    {
        if (_instances.ContainsKey(displayIndex))
        {
            if (_instances[displayIndex].IsRunning)
            {
                Console.WriteLine($"[MpvController] Instance for display {displayIndex} already exists and is running.");
                return false;
            }
            else
            {
                // 停止済みインスタンスを削除して再作成
                _instances.Remove(displayIndex);
            }
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

            // MPV 起動コマンド（バックグラウンド起動）
            // --no-audio-display: 音声表示しない
            // --input-ipc-server=\\.\pipe\PIPE_NAME で IPC パイプ設定
            // --screen={N}: ウィンドウを配置する画面（0ベース）
            // --fs-screen={N}: フルスクリーン表示する画面（0ベース）
            // --no-terminal: コンソール表示しない
            // 起動時に自動再生、フルスクリーンでの起動
            string mpvArgs = $"--input-ipc-server=\\\\.\\pipe\\{pipeName} --screen={displayIndex} --fs-screen={displayIndex} --no-terminal --fullscreen --ontop \"{videoPath}\"";

            var process = _processManager.StartManagedProcess(
                _mpvExePath,
                arguments: mpvArgs
            );

            var instance = new MpvInstance
            {
                DisplayIndex = displayIndex,
                Process = process,
                PipeName = pipeName,
                IsPlaying = false
            };

            _instances[displayIndex] = instance;

            // IPC パイプ接続待機
            System.Threading.Thread.Sleep(_mpvStartupWaitMs);

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
            // MPV IPC JSON 形式（JSON-RPC 2.0標準ではなく、MPV独自形式）
            // 正しい形式: {"command":["set_property","pause",true]}
            var jsonRequest = new
            {
                command = new object[] { command }.Concat(args ?? Array.Empty<object?>()).ToArray()
            };

            string jsonString = JsonConvert.SerializeObject(jsonRequest) + "\n";

            // デバッグ: 送信するJSON全体をログ出力
            Console.WriteLine($"[MpvController] Sending JSON to display {displayIndex}: {jsonString.TrimEnd()}");

            // IPC パイプで送信
            using (var pipeClient = new NamedPipeClientStream(".", instance.PipeName, PipeDirection.InOut))
            {
                pipeClient.Connect(2000); // 2秒タイムアウト

                var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                writer.WriteLine(jsonString);

                // MPV IPC は非同期で送信のみで十分（レスポンス受信は必須ではない）
                Console.WriteLine($"[MpvController] Command sent successfully to display {displayIndex}");

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
    /// 全インスタンスに対する統一的な再生/一時停止トグル
    /// グローバルフラグに基づいて、全インスタンスを同じ状態に制御
    /// インスタンス間の遅延を最小化するため、迅速にコマンド送信
    /// </summary>
    public void TogglePlayPause()
    {
        Console.WriteLine("[MpvController] ========== TogglePlayPause (Global) ==========");

        var allInstances = _instances.Values.ToList();

        // インスタンスが存在しない、または全て停止している場合 → 全て起動 + 再生
        if (allInstances.Count == 0 || allInstances.All(i => !i.IsRunning))
        {
            Console.WriteLine("[MpvController] No running instances. Auto-starting all displays...");

            if (_videoFilePaths.Count == 0)
            {
                Console.Error.WriteLine("[MpvController] No video file paths configured in config.ini.");
                return;
            }

            // 全ディスプレイで起動（並列処理で高速化）
            var startTasks = new List<Task>();
            foreach (var kvp in _videoFilePaths)
            {
                int displayIndex = kvp.Key;
                string videoPath = kvp.Value;

                if (!_instances.ContainsKey(displayIndex) || !_instances[displayIndex].IsRunning)
                {
                    startTasks.Add(Task.Run(() =>
                    {
                        if (!File.Exists(videoPath))
                        {
                            Console.Error.WriteLine($"[MpvController] Display {displayIndex}: Video file not found: {videoPath}");
                            return;
                        }
                        StartInstance(displayIndex, videoPath);
                    }));
                }
            }

            // 全インスタンスの起動完了を待機
            Task.WaitAll(startTasks.ToArray());

            // 起動後、全インスタンスのfullscreen設定（並列処理で高速化）
            var fullscreenTasks = _instances.Values.Where(i => i.IsRunning).Select(instance =>
                Task.Run(() =>
                {
                    SendCommand(instance.DisplayIndex, "set_property", "fullscreen", true);
                })
            ).ToArray();

            Task.WaitAll(fullscreenTasks);

            // 状態更新
            foreach (var instance in _instances.Values.Where(i => i.IsRunning))
            {
                instance.IsPlaying = true;
            }

            _isGloballyPlaying = true;
            Console.WriteLine("[MpvController] All instances started and playing.");

            // Phase 3.11.1: MPV起動完了後、最前面化監視タイマーを開始
            StartTopMostMonitoring();
        }
        // 全インスタンスが再生中 → 全て停止
        else if (allInstances.All(i => i.IsRunning && i.IsPlaying))
        {
            Console.WriteLine("[MpvController] All instances playing. Pausing all...");

            // 並列処理でコマンド送信（遅延削減）
            var pauseTasks = allInstances.Select(instance =>
                Task.Run(() =>
                {
                    SendCommand(instance.DisplayIndex, "set_property", "pause", true);
                    // フルスクリーンを維持（フルスクリーン解除コマンドを削除）
                    // SendCommand(instance.DisplayIndex, "set_property", "fullscreen", false);
                })
            ).ToArray();

            Task.WaitAll(pauseTasks);

            // 状態更新
            foreach (var instance in allInstances)
            {
                instance.IsPlaying = false;
            }

            _isGloballyPlaying = false;
            Console.WriteLine("[MpvController] All instances paused.");
        }
        // 全インスタンスが停止中 → 全て再生
        else if (allInstances.All(i => i.IsRunning && !i.IsPlaying))
        {
            Console.WriteLine("[MpvController] All instances paused. Playing all...");

            // 並列処理でコマンド送信（遅延削減）
            var resumeTasks = allInstances.Select(instance =>
                Task.Run(() =>
                {
                    SendCommand(instance.DisplayIndex, "set_property", "pause", false);
                    SendCommand(instance.DisplayIndex, "set_property", "fullscreen", true);
                })
            ).ToArray();

            Task.WaitAll(resumeTasks);

            // 状態更新
            foreach (var instance in allInstances)
            {
                instance.IsPlaying = true;
            }

            _isGloballyPlaying = true;
            Console.WriteLine("[MpvController] All instances playing.");
        }
        // 混在状態（一部クラッシュ等）→ グローバルフラグで統一制御
        else
        {
            Console.WriteLine($"[MpvController] Mixed state detected. Applying global flag: {(_isGloballyPlaying ? "pause" : "play")} all...");

            bool targetPause = _isGloballyPlaying;
            var runningInstances = allInstances.Where(i => i.IsRunning).ToList();

            var mixedTasks = runningInstances.Select(instance =>
                Task.Run(() =>
                {
                    SendCommand(instance.DisplayIndex, "set_property", "pause", targetPause);
                    if (!targetPause)
                        SendCommand(instance.DisplayIndex, "set_property", "fullscreen", true);
                })
            ).ToArray();

            Task.WaitAll(mixedTasks);

            foreach (var instance in runningInstances)
            {
                instance.IsPlaying = !targetPause;
            }

            _isGloballyPlaying = !targetPause;
            Console.WriteLine($"[MpvController] Mixed state resolved: all instances {(_isGloballyPlaying ? "playing" : "paused")}.");
        }

        Console.WriteLine("[MpvController] ================================================\n");
    }

    /// <summary>
    /// グローバル再生状態フラグの取得
    /// </summary>
    public bool IsGloballyPlaying => _isGloballyPlaying;

    /// <summary>
    /// Phase 5: 全インスタンスが自然終了した時に発火するイベント
    /// ユーザー操作（StopAll/Quit）による終了では発火しない
    /// </summary>
    public event Action? OnAllInstancesExited;

    /// <summary>
    /// 起動中の MPV インスタンスが存在するか確認
    /// Phase 4: スケジュール機能の2重起動防止用
    /// </summary>
    public bool HasRunningInstances()
    {
        return _instances.Values.Any(i => i.IsRunning);
    }

    /// <summary>
    /// Phase 3.11.1: 最前面化監視タイマーを開始
    /// MPV起動中のみタイマーを設定、重複防止あり
    /// </summary>
    private void StartTopMostMonitoring()
    {
        // 既にタイマーが動作中の場合は何もしない（重複防止）
        if (_isTopMostTimerRunning)
        {
            Console.WriteLine("[MpvController] TopMost monitoring already running. Skipping.");
            return;
        }

        // MPVインスタンスが起動していない場合は起動しない
        if (!_instances.Values.Any(i => i.IsRunning))
        {
            Console.WriteLine("[MpvController] No running MPV instances. TopMost monitoring not started.");
            return;
        }

        Console.WriteLine($"[MpvController] Starting TopMost monitoring (interval: {_topMostIntervalMs}ms)...");

        _topMostTimer = new System.Timers.Timer(_topMostIntervalMs);
        _topMostTimer.Elapsed += OnTopMostTimerElapsed;
        _topMostTimer.AutoReset = true;
        _topMostTimer.Start();
        _isTopMostTimerRunning = true;

        Console.WriteLine("[MpvController] TopMost monitoring started.");
    }

    /// <summary>
    /// Phase 3.11.1: 最前面化監視タイマーを停止
    /// </summary>
    private void StopTopMostMonitoring()
    {
        if (_topMostTimer != null)
        {
            Console.WriteLine("[MpvController] Stopping TopMost monitoring...");
            _topMostTimer.Stop();
            _topMostTimer.Elapsed -= OnTopMostTimerElapsed;
            _topMostTimer.Dispose();
            _topMostTimer = null;
            _isTopMostTimerRunning = false;
            Console.WriteLine("[MpvController] TopMost monitoring stopped.");
        }
    }

    /// <summary>
    /// Phase 3.11.2: タイマーイベントハンドラ（定期的に最前面化コマンドを送信）
    /// Windows API SetWindowPos による強制的な最前面化
    /// </summary>
    private void OnTopMostTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // 全ての起動中のインスタンスに最前面化コマンドを送信
        var runningInstances = _instances.Values.Where(i => i.IsRunning).ToList();

        if (runningInstances.Count == 0)
        {
            // _isGloballyPlaying が true のまま全インスタンスが停止 → 動画の自然終了
            bool wasNaturalExit = _isGloballyPlaying;

            // MPVが全て停止している場合はタイマーを停止
            StopTopMostMonitoring();

            if (wasNaturalExit)
            {
                Console.WriteLine("[MpvController] All instances exited naturally (video ended).");
                _isGloballyPlaying = false;
                OnAllInstancesExited?.Invoke();
            }

            return;
        }

        foreach (var instance in runningInstances)
        {
            try
            {
                // Phase 3.11.2: Windows API SetWindowPos方式による最前面化
                if (instance.Process != null && !instance.Process.HasExited)
                {
                    IntPtr hwnd = instance.Process.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    }
                }
            }
            catch (Exception ex)
            {
                // エラー時のみログ出力、タイマーは継続
                Console.Error.WriteLine($"[MpvController] TopMost command error for display {instance.DisplayIndex}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 再生速度設定
    /// </summary>
    public bool SetPlaybackSpeed(int displayIndex, double speed)
    {
        return SendCommand(displayIndex, "set_property", "speed", speed);
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
        // Phase 3.11.1: 最前面化監視タイマーを停止
        StopTopMostMonitoring();

        var displayIndices = _instances.Keys.ToList();

        foreach (var displayIndex in displayIndices)
        {
            StopInstance(displayIndex);
        }

        _isGloballyPlaying = false;
        Console.WriteLine("[MpvController] All MPV instances stopped.");
    }

    /// <summary>
    /// インスタンスを停止
    /// まずJSON-RPC quitコマンドを試行し、失敗した場合はプロセスキルにフォールバック
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

                // まずJSON-RPC quitコマンドを試行（クリーンな終了）
                bool quitSuccess = SendCommand(displayIndex, "quit");

                if (quitSuccess)
                {
                    // quitコマンド送信成功、プロセス終了を待機（最大2秒）
                    bool exited = instance.Process!.WaitForExit(2000);

                    if (exited)
                    {
                        Console.WriteLine($"[MpvController] Instance for display {displayIndex} exited cleanly via quit command");
                    }
                    else
                    {
                        // タイムアウト: プロセスキルにフォールバック
                        Console.WriteLine($"[MpvController] Quit command timed out for display {displayIndex}, forcing termination");
                        _processManager.TerminateProcess(instance.Process!.Id);
                    }
                }
                else
                {
                    // quitコマンド送信失敗: プロセスキルにフォールバック
                    Console.WriteLine($"[MpvController] Quit command failed for display {displayIndex}, forcing termination");
                    _processManager.TerminateProcess(instance.Process!.Id);
                }
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
        StopTopMostMonitoring();
        StopAll();
        GC.SuppressFinalize(this);
    }

    ~MpvController()
    {
        StopTopMostMonitoring();
        StopAll();
    }
}
