using v2;

Console.WriteLine("================================================");
Console.WriteLine("  Sync Play Auto Timer v2.5.3");
Console.WriteLine("================================================\n");

// 設定ファイル読み込み（ConfigManager を先に生成してポート番号を確定）
// Windows Documents フォルダ内の固定パスを使用
var configManager = new ConfigManager();
if (!configManager.LoadConfig())
{
    Console.WriteLine("[Program] ⚠️  Warning: Config loading failed. Using hardcoded defaults.\n");
}

// 初回セットアップチェック（ConfigManager 経由でポート番号を渡す）
var setupManager = new SetupManager(configManager);
if (!setupManager.IsSetupCompleted())
{
    Console.WriteLine("[Program] First-time setup required.\n");
    setupManager.RunInitialSetup();
}

// Phase 3.7: コンソールウィンドウの自動最小化
if (configManager.MinimizeStartMode)
{
    var consoleWindowManager = new ConsoleWindowManager();
    Console.WriteLine("[Program] MinimizeStartMode is enabled. Minimizing console window in 2 seconds...\n");
    Thread.Sleep(2000); // セットアップメッセージを表示してから最小化
    consoleWindowManager.MinimizeWindow();
}

// プロセスマネージャー初期化（ジョブオブジェクト）
var processManager = new ProcessManager();
processManager.Initialize();

// キーボードフック初期化（ConfigManager を渡す）
var keyboardHook = new KeyboardHook(configManager);

// HTTP サーバー初期化（config.ini から読み込み）
var httpServer = new HttpServer(port: configManager.HttpServerPort);

// MPV コントローラー初期化（ConfigManager を渡す）
var mpvController = new MpvController(processManager, configManager);

// Phase 3.10: ネットワーク同期マネージャー初期化
var networkSyncManager = new NetworkSyncManager(configManager);

// Phase 4: スケジュールマネージャー初期化
// スケジュールトリガー時は /api/toggle と同等の処理を実行
ScheduleManager? scheduleManager = null;

// HTTP サーバーのイベントハンドラ設定（Phase 3.10: ネットワーク同期統合）
httpServer.OnToggle += async (sender, e) =>
{
    try
    {
        // ローカルMPV制御と別機ブロードキャストを並列実行（遅延削減）
        var localTask = Task.Run(() => mpvController.TogglePlayPause());
        var broadcastTask = networkSyncManager.BroadcastToggleAsync();

        // 両方の完了を待機
        await Task.WhenAll(localTask, broadcastTask);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Program] Error handling Toggle event: {ex.Message}");
    }
};

httpServer.OnQuit += async (sender, e) =>
{
    try
    {
        // ローカルMPV停止と別機ブロードキャストを並列実行（遅延削減）
        var localTask = Task.Run(() => mpvController.StopAll());
        var broadcastTask = networkSyncManager.BroadcastQuitAsync();

        // 両方の完了を待機
        await Task.WhenAll(localTask, broadcastTask);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Program] Error handling Quit event: {ex.Message}");
    }
};

httpServer.OnRestart += async (sender, e) =>
{
    try
    {
        // ローカルMPV停止と別機ブロードキャストを並列実行（遅延削減）
        var localTask = Task.Run(() => mpvController.StopAll());
        var broadcastTask = networkSyncManager.BroadcastRestartAsync();

        // 両方の完了を待機
        await Task.WhenAll(localTask, broadcastTask);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Program] Error handling Restart event: {ex.Message}");
    }
};

httpServer.OnStatus += (sender, e) =>
{
    mpvController.PrintStatus();
};

// HTTP サーバー起動（管理者権限が必要）
// テスト時には以下をコメントアウト
try
{
    httpServer.Start();
}
catch (System.Net.HttpListenerException ex) when (ex.ErrorCode == 5)
{
    Console.WriteLine("[Program] ⚠️  HTTP Server requires administrator privileges.");
    Console.WriteLine("[Program] Keyboard hook will continue to work.");
    Console.WriteLine("[Program] To test HTTP endpoints, run as Administrator.\n");
}

// Phase 5: ループモード（動画終了後にアプリケーション側で自動再起動）
if (configManager.LoopMode)
{
    mpvController.OnAllInstancesExited += () =>
    {
        var localTask = Task.Run(() => mpvController.TogglePlayPause());
        var broadcastTask = networkSyncManager.BroadcastToggleAsync();
        Task.WhenAll(localTask, broadcastTask).Wait();
    };
}

// Phase 4: スケジュールマネージャー開始
// スケジュールトリガー時は /api/toggle と同等の処理を実行（ローカル MPV 起動 + ネットワークブロードキャスト）
scheduleManager = new ScheduleManager(
    configManager,
    onScheduleTriggered: () =>
    {
        // ローカル MPV 制御と別機ブロードキャストを並列実行（/api/toggle と同じ処理）
        var localTask = Task.Run(() => mpvController.TogglePlayPause());
        var broadcastTask = networkSyncManager.BroadcastToggleAsync();

        // 両方の完了を待機
        Task.WhenAll(localTask, broadcastTask).Wait();
    },
    isMpvRunning: mpvController.HasRunningInstances
);
scheduleManager.Start();

Console.WriteLine("\n[Program] System is running. Press Ctrl+C to exit.\n");

// Windowsメッセージループを別スレッドで実行（キーボードフックに必要）
// 重要: フック初期化とメッセージループは同じスレッドで実行する必要がある
var messageLoopTask = Task.Run(() =>
{
    keyboardHook.Initialize();  // このスレッドでフックを初期化
    keyboardHook.RunMessageLoop();  // 同じスレッドでメッセージループを実行
});

// キーボード入力イベント監視タスク
var monitoringCts = new CancellationTokenSource();
var monitoringTask = Task.Run(async () =>
{
    using var httpClient = new HttpClient();
    while (!monitoringCts.Token.IsCancellationRequested)
    {
        // キーボードイベントキューを定期的にチェック
        if (keyboardHook.EventQueue.TryDequeue(out var keyEvent))
        {
            // HTTP POST/GET を送信
            try
            {
                string endpoint = $"http://localhost:{configManager.HttpServerPort}/api/{keyEvent.Action}";
                HttpResponseMessage response;

                // Statusは GET、それ以外は POST
                if (keyEvent.Action == "status")
                {
                    response = await httpClient.GetAsync(endpoint);
                }
                else
                {
                    response = await httpClient.PostAsync(endpoint, null);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Program] HTTP request failed: {ex.Message}");
            }
        }

        await Task.Delay(50, monitoringCts.Token);
    }
}, monitoringCts.Token);

// シャットダウン処理（共通）
void Shutdown()
{
    if (!monitoringCts.IsCancellationRequested)
    {
        monitoringCts.Cancel();
        monitoringCts.Dispose();
    }
    scheduleManager?.Stop();
    mpvController.StopAll();
    keyboardHook.Shutdown();
    httpServer.Stop();
    httpServer.Dispose();
    keyboardHook.Dispose();
    networkSyncManager.Dispose();
    scheduleManager?.Dispose();
    processManager.Shutdown();
    processManager.Dispose();
    Console.WriteLine("[Program] All resources released. Goodbye!");
}

// Ctrl+C でアプリケーション終了
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    Console.WriteLine("\n[Program] Shutting down...");
    Shutdown();
};

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[Program] Ctrl+C detected. Shutting down...");
    Shutdown();
    Environment.Exit(0);
};

// メインスレッド保持
try
{
    await monitoringTask;
}
catch (OperationCanceledException)
{
}
