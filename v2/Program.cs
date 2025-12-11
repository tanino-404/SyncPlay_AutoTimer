using v2;

Console.WriteLine("================================================");
Console.WriteLine("  SyncPlay_AutoTimer v2.0 - Phase 1 & 2 & 3");
Console.WriteLine("  Keyboard Hook + HTTP Server + Syncplay + Config");
Console.WriteLine("================================================\n");

// 初回セットアップチェック
var setupManager = new SetupManager();
if (!setupManager.IsSetupCompleted())
{
    Console.WriteLine("[Program] First-time setup required.\n");
    setupManager.RunInitialSetup();
}

// 設定ファイル読み込み（Phase 3）
// Windows Documents フォルダ内の固定パスを使用
var configManager = new ConfigManager();
Console.WriteLine($"[Program] Config path: {configManager.ConfigPath}\n");
if (!configManager.LoadConfig())
{
    Console.WriteLine("[Program] ⚠️  Warning: Config loading failed. Using hardcoded defaults.\n");
}

// プロセスマネージャー初期化（ジョブオブジェクト）
var processManager = new ProcessManager();
processManager.Initialize();

// キーボードフック初期化（ConfigManager を渡す）
var keyboardHook = new KeyboardHook(configManager);

// HTTP サーバー初期化（config.ini から読み込み）
var httpServer = new HttpServer(port: configManager.HttpServerPort);

// Syncplay マネージャー初期化（ConfigManager を渡す）
var syncplayManager = new SyncplayManager(processManager, configManager);

// MPV コントローラー初期化（ConfigManager を渡す）
var mpvController = new MpvController(processManager, configManager);

// HTTP サーバーのイベントハンドラ設定（Phase 2 統合）
httpServer.OnToggle += (sender, e) =>
{
    Console.WriteLine($"\n[Program] ======== Toggle Event ========");
    Console.WriteLine($"[Program] HTTP Toggle event received from {e.Path}");

    // 全インスタンスに対する統一的な制御（グローバルフラグベース）
    mpvController.TogglePlayPauseAll();

    Console.WriteLine($"[Program] ===================================\n");
};

httpServer.OnQuit += (sender, e) =>
{
    Console.WriteLine($"\n[Program] ======== Quit Event ========");
    Console.WriteLine($"[Program] HTTP Quit event received from {e.Path}");

    // Syncplay と MPV を全停止
    mpvController.StopAll();
    syncplayManager.StopAll();

    Console.WriteLine($"[Program] Quit command processed.");
    Console.WriteLine($"[Program] ===================================\n");
};

httpServer.OnRestart += (sender, e) =>
{
    Console.WriteLine($"\n[Program] ======== Restart Event ========");
    Console.WriteLine($"[Program] HTTP Restart event received from {e.Path}");

    // Syncplay と MPV をリスタート
    mpvController.StopAll();
    syncplayManager.StopAll();

    System.Threading.Thread.Sleep(1000);

    syncplayManager.StartServer();
    System.Threading.Thread.Sleep(2000);
    syncplayManager.StartClient();

    Console.WriteLine($"[Program] Restart command processed.");
    Console.WriteLine($"[Program] ===================================\n");
};

httpServer.OnStatus += (sender, e) =>
{
    Console.WriteLine($"\n[Program] ======== Status Check ========");
    Console.WriteLine($"[Program] HTTP Status check from {e.Path}");

    syncplayManager.PrintStatus();
    mpvController.PrintStatus();

    Console.WriteLine($"[Program] ===================================\n");
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

Console.WriteLine("\n[Program] System is running. Press Ctrl+C to exit.\n");

// Windowsメッセージループを別スレッドで実行（キーボードフックに必要）
// 重要: フック初期化とメッセージループは同じスレッドで実行する必要がある
var messageLoopTask = Task.Run(() =>
{
    keyboardHook.Initialize();  // このスレッドでフックを初期化
    keyboardHook.RunMessageLoop();  // 同じスレッドでメッセージループを実行
});

// キーボード入力イベント監視タスク
var monitoringTask = Task.Run(async () =>
{
    using var httpClient = new HttpClient();
    while (true)
    {
        // キーボードイベントキューを定期的にチェック
        if (keyboardHook.EventQueue.TryDequeue(out var keyEvent))
        {
            Console.WriteLine($"[Program] Keyboard event queued: {keyEvent}");

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

                Console.WriteLine($"[Program] HTTP {(keyEvent.Action == "status" ? "GET" : "POST")} sent to {endpoint}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Program] HTTP request failed: {ex.Message}");
            }
        }

        await Task.Delay(50);
    }
});

// Ctrl+C でアプリケーション終了
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    Console.WriteLine("\n[Program] Shutting down...");
    mpvController.StopAll();
    syncplayManager.StopAll();
    keyboardHook.Shutdown();
    httpServer.Stop();
    httpServer.Dispose();
    keyboardHook.Dispose();
    processManager.Shutdown();
    processManager.Dispose();
    Console.WriteLine("[Program] All resources released. Goodbye!");
};

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\n[Program] Ctrl+C detected. Shutting down...");
    mpvController.StopAll();
    syncplayManager.StopAll();
    keyboardHook.Shutdown();
    httpServer.Stop();
    httpServer.Dispose();
    keyboardHook.Dispose();
    processManager.Shutdown();
    processManager.Dispose();
    Environment.Exit(0);
};

// メインスレッド保持
try
{
    await monitoringTask;
}
catch (OperationCanceledException)
{
    Console.WriteLine("[Program] Monitoring task cancelled.");
}
