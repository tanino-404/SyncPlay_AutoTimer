using v2;

Console.WriteLine("================================================");
Console.WriteLine("  SyncPlay_AutoTimer v2.0 - Phase 1 & 2 & 3");
Console.WriteLine("  Keyboard Hook + HTTP Server + Syncplay + Config");
Console.WriteLine("================================================\n");

// 設定ファイル読み込み（Phase 3）
var configManager = new ConfigManager("config.ini");
if (!configManager.LoadConfig())
{
    Console.WriteLine("[Program] Warning: Using hardcoded defaults.\n");
}

// プロセスマネージャー初期化（ジョブオブジェクト）
var processManager = new ProcessManager();
processManager.Initialize();

// キーボードフック初期化
var keyboardHook = new KeyboardHook();
keyboardHook.Initialize();

// HTTP サーバー初期化（ポート 8080）
var httpServer = new HttpServer(port: 8080);

// Syncplay マネージャー初期化（ConfigManager を渡す）
var syncplayManager = new SyncplayManager(processManager, configManager);

// MPV コントローラー初期化（ConfigManager を渡す）
var mpvController = new MpvController(processManager, configManager);

// HTTP サーバーのイベントハンドラ設定（Phase 2 統合）
httpServer.OnToggle += (sender, e) =>
{
    Console.WriteLine($"\n[Program] ======== Toggle Event ========");
    Console.WriteLine($"[Program] HTTP Toggle event received from {e.Path}");

    // 全 MPV インスタンスで再生/一時停止をトグル
    for (int display = 0; display < 2; display++)
    {
        mpvController.TogglePlayPause(display);
    }

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

// キーボード入力イベント監視タスク
var monitoringTask = Task.Run(() =>
{
    while (true)
    {
        // キーボードイベントキューを定期的にチェック
        if (keyboardHook.EventQueue.TryDequeue(out var keyEvent))
        {
            Console.WriteLine($"[Program] Keyboard event queued: {keyEvent}");

            // キー入力に応じて HTTP POST を送信するロジック
            // Phase 2-3 で実装予定（ここではログ出力のみ）
        }

        Thread.Sleep(50);
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
