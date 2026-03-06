using System.Timers;

namespace v2;

/// <summary>
/// Phase 4: スケジュール管理クラス
/// 指定時刻に MPV を自動起動（毎日繰り返し）
/// ServerMode=true の場合のみ有効
/// </summary>
public class ScheduleManager : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly Action _onScheduleTriggered;
    private readonly Func<bool> _isMpvRunning;
    private System.Timers.Timer? _checkTimer;
    private TimeSpan[] _targetTimes = Array.Empty<TimeSpan>();
    private HashSet<TimeSpan> _triggeredToday = new();
    private DateTime _lastCheckedDate = DateTime.MinValue;
    private bool _isRunning = false;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="configManager">設定マネージャー</param>
    /// <param name="onScheduleTriggered">スケジュール時刻到達時に呼び出されるアクション</param>
    /// <param name="isMpvRunning">MPV が起動中かどうかを判定する関数</param>
    public ScheduleManager(
        ConfigManager configManager,
        Action onScheduleTriggered,
        Func<bool> isMpvRunning)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _onScheduleTriggered = onScheduleTriggered ?? throw new ArgumentNullException(nameof(onScheduleTriggered));
        _isMpvRunning = isMpvRunning ?? throw new ArgumentNullException(nameof(isMpvRunning));
    }

    /// <summary>
    /// スケジュール監視を開始
    /// </summary>
    public bool Start()
    {
        // ServerMode=false の場合はスケジュール機能を無効化
        if (!_configManager.ServerMode)
        {
            Console.WriteLine("[ScheduleManager] ServerMode=false: Schedule feature disabled (client mode)");
            return false;
        }

        // ScheduleMode=false の場合はスケジュール機能を無効化
        if (!_configManager.ScheduleMode)
        {
            Console.WriteLine("[ScheduleManager] ScheduleMode=false: Schedule feature disabled");
            return false;
        }

        // TargetTimes を読み込み
        _targetTimes = _configManager.TargetTimes;

        // TargetTimes が空の場合はスケジュール機能を無効化
        if (_targetTimes.Length == 0)
        {
            Console.WriteLine("[ScheduleManager] No valid TargetTimes configured: Schedule feature disabled");
            return false;
        }

        Console.WriteLine($"[ScheduleManager] Schedule: {string.Join(", ", _targetTimes.Select(t => t.ToString(@"hh\:mm")))}");


        // 1秒間隔でチェック（秒単位の精度）
        _checkTimer = new System.Timers.Timer(1000);
        _checkTimer.Elapsed += OnCheckTimerElapsed;
        _checkTimer.AutoReset = true;
        _checkTimer.Start();
        _isRunning = true;
        return true;
    }

    /// <summary>
    /// スケジュール監視を停止
    /// </summary>
    public void Stop()
    {
        if (_checkTimer != null)
        {
            _checkTimer.Stop();
            _checkTimer.Elapsed -= OnCheckTimerElapsed;
            _checkTimer.Dispose();
            _checkTimer = null;
            _isRunning = false;
        }
    }

    /// <summary>
    /// スケジュール監視が実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// タイマーイベントハンドラ（1秒ごとに実行）
    /// </summary>
    private void OnCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            DateTime now = DateTime.Now;

            // 日付が変わったら、トリガー済みリストをリセット（毎日繰り返し対応）
            if (now.Date != _lastCheckedDate.Date)
            {
                _triggeredToday.Clear();
                _lastCheckedDate = now.Date;
            }

            // 現在時刻を TimeSpan に変換（時:分:秒）
            TimeSpan currentTime = now.TimeOfDay;

            // 各スケジュール時刻をチェック
            foreach (var targetTime in _targetTimes)
            {
                // 既にトリガー済みの場合はスキップ
                if (_triggeredToday.Contains(targetTime))
                    continue;

                // 時刻が一致するかチェック（秒単位、±1秒の許容範囲）
                double diffSeconds = Math.Abs((currentTime - targetTime).TotalSeconds);
                if (diffSeconds < 1.0)
                {
                    // トリガー済みとしてマーク（同日中の再実行を防止）
                    _triggeredToday.Add(targetTime);

                    // MPV が既に起動中かチェック
                    if (_isMpvRunning())
                    {
                        continue;
                    }

                    Console.WriteLine($"[ScheduleManager] Schedule triggered: {targetTime:hh\\:mm\\:ss}");

                    // スケジュールトリガー実行
                    try
                    {
                        _onScheduleTriggered();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ScheduleManager] Error executing scheduled action: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ScheduleManager] Error in check timer: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のスケジュール状態を表示
    /// </summary>
    public void PrintStatus()
    {
        Console.WriteLine("[ScheduleManager] Status:");
        Console.WriteLine($"  Running: {_isRunning}");
        Console.WriteLine($"  ServerMode: {_configManager.ServerMode}");
        Console.WriteLine($"  ScheduleMode: {_configManager.ScheduleMode}");
        Console.WriteLine($"  Target times ({_targetTimes.Length}):");
        foreach (var time in _targetTimes)
        {
            bool triggered = _triggeredToday.Contains(time);
            Console.WriteLine($"    - {time:hh\\:mm\\:ss} {(triggered ? "(triggered today)" : "")}");
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    ~ScheduleManager()
    {
        Stop();
    }
}
