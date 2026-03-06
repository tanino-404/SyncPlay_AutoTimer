using System.Diagnostics;
using System.Security.Principal;

namespace v2;

/// <summary>
/// 初回セットアップ管理
/// HTTP ACL設定、ファイアウォール設定、外部アプリケーションチェックを実施
/// </summary>
public class SetupManager
{
    private const string SetupCompletedFlagFile = ".setup_completed";
    private readonly string _appDataFolder;
    private readonly string _setupFlagPath;
    private readonly ConfigManager _configManager;

    public SetupManager(ConfigManager configManager)
    {
        _configManager = configManager;

        // %AppData%\SyncPlay_AutoTimer\ に初回セットアップ完了フラグを配置
        _appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SyncPlay_AutoTimer"
        );
        _setupFlagPath = Path.Combine(_appDataFolder, SetupCompletedFlagFile);
    }

    /// <summary>
    /// 初回セットアップが完了しているかチェック
    /// </summary>
    public bool IsSetupCompleted()
    {
        return File.Exists(_setupFlagPath);
    }

    /// <summary>
    /// 現在のプロセスが管理者権限で実行されているかチェック
    /// </summary>
    public static bool IsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 管理者権限でアプリケーションを再起動
    /// </summary>
    public static void RestartAsAdministrator()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("[SetupManager] Failed to get current executable path.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas" // 管理者として実行
            };

            Process.Start(startInfo);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SetupManager] Failed to restart as administrator: {ex.Message}");
            Console.WriteLine("[SetupManager] Please run the application as Administrator manually for full setup.");
        }
    }

    /// <summary>
    /// 初回セットアップを実行
    /// </summary>
    public void RunInitialSetup()
    {
        Console.WriteLine("================================================");
        Console.WriteLine("  Sync Play Auto Timer v2.5.3 - Initial Setup");
        Console.WriteLine("================================================\n");

        // 管理者権限チェック
        bool isAdmin = IsAdministrator();

        if (!isAdmin)
        {
            Console.WriteLine("[SetupManager] ⚠️  Administrator privileges required for full setup.");
            Console.WriteLine("[SetupManager] The application will restart with elevated privileges.\n");
            Console.Write("[SetupManager] Continue? (Y/N): ");

            var response = Console.ReadLine()?.Trim().ToUpper();
            if (response == "Y" || response == "YES")
            {
                RestartAsAdministrator();
                return;
            }
            else
            {
                Console.WriteLine("[SetupManager] Setup cancelled. Some features (HTTP ACL, Firewall) will be skipped.\n");
            }
        }

        // Step 1: HTTP ACL設定
        if (isAdmin)
        {
            int port = _configManager.HttpServerPort;
            Console.WriteLine($"[Step 1/3] Setting up HTTP ACL for port {port}...");
            SetupHttpAcl(port);
        }
        else
        {
            Console.WriteLine("[Step 1/3] Skipping HTTP ACL setup (requires admin privileges)");
        }

        Console.WriteLine();

        // Step 2: ファイアウォール設定
        if (isAdmin)
        {
            int port = _configManager.HttpServerPort;
            Console.WriteLine("[Step 2/3] Setting up Windows Firewall...");
            SetupFirewall(port);
        }
        else
        {
            Console.WriteLine("[Step 2/3] Skipping Firewall setup (requires admin privileges)");
        }

        Console.WriteLine();

        // Step 3: 外部アプリケーションチェック
        Console.WriteLine("[Step 3/3] Checking required applications...");
        CheckRequiredApps();

        Console.WriteLine();

        // セットアップ完了フラグを作成
        MarkSetupCompleted();

        Console.WriteLine("================================================");
        Console.WriteLine("  Setup Completed!");
        Console.WriteLine("================================================\n");

        Console.WriteLine("[SetupManager] Next steps:");
        Console.WriteLine("  1. Edit config.ini to customize settings");
        Console.WriteLine("  2. Place video files in the Video folder");
        Console.WriteLine("  3. Press any key to start the application...\n");

        Console.ReadKey();
    }

    /// <summary>
    /// HTTP ACL設定（管理者権限が必要）
    /// </summary>
    private void SetupHttpAcl(int port)
    {
        try
        {
            string url = $"http://+:{port}/";

            // 既存の設定を削除（エラーは無視）
            RunNetshCommand($"http delete urlacl url=\"{url}\"", ignoreError: true);

            // 新しい設定を追加
            var result = RunNetshCommand($"http add urlacl url=\"{url}\" user=Everyone");

            if (result)
            {
                Console.WriteLine($"[SetupManager] ✅ HTTP ACL registered for port {port}");
            }
            else
            {
                Console.WriteLine($"[SetupManager] ⚠️  HTTP ACL registration may have failed");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SetupManager] ❌ Error setting up HTTP ACL: {ex.Message}");
        }
    }

    /// <summary>
    /// ファイアウォール設定（管理者権限が必要）
    /// </summary>
    private void SetupFirewall(int port)
    {
        try
        {
            // 既存ルールを削除（エラーは無視）
            RunNetshCommand(
                "advfirewall firewall delete rule name=\"SyncPlay_AutoTimer (HTTP Inbound)\"",
                ignoreError: true
            );
            RunNetshCommand(
                "advfirewall firewall delete rule name=\"SyncPlay_AutoTimer (HTTP Outbound)\"",
                ignoreError: true
            );

            // Inbound ルール追加
            var inboundResult = RunNetshCommand(
                $"advfirewall firewall add rule name=\"SyncPlay_AutoTimer (HTTP Inbound)\" " +
                $"dir=in action=allow protocol=TCP localport={port}"
            );

            // Outbound ルール追加
            var outboundResult = RunNetshCommand(
                $"advfirewall firewall add rule name=\"SyncPlay_AutoTimer (HTTP Outbound)\" " +
                $"dir=out action=allow protocol=TCP localport={port}"
            );

            if (inboundResult && outboundResult)
            {
                Console.WriteLine($"[SetupManager] ✅ Firewall rules added for port {port}");
            }
            else
            {
                Console.WriteLine($"[SetupManager] ⚠️  Firewall setup completed with warnings");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SetupManager] ❌ Error setting up Firewall: {ex.Message}");
        }
    }

    /// <summary>
    /// 外部アプリケーションの存在チェック
    /// </summary>
    private void CheckRequiredApps()
    {
        var apps = new Dictionary<string, string>
        {
            { "MPV", @"C:\Program Files\mpv\mpv.exe" }
        };

        bool allFound = true;

        foreach (var app in apps)
        {
            if (File.Exists(app.Value))
            {
                Console.WriteLine($"[SetupManager] ✅ {app.Key} found: {app.Value}");
            }
            else
            {
                Console.WriteLine($"[SetupManager] ⚠️  {app.Key} not found: {app.Value}");
                allFound = false;
            }
        }

        if (!allFound)
        {
            Console.WriteLine("\n[SetupManager] Please install missing applications:");
            Console.WriteLine("  - MPV: https://mpv.io");
        }
    }

    /// <summary>
    /// netsh コマンドを実行
    /// </summary>
    private bool RunNetshCommand(string arguments, bool ignoreError = false)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();

            if (process.ExitCode == 0 || ignoreError)
            {
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            if (!ignoreError)
            {
                Console.Error.WriteLine($"[SetupManager] Exception running netsh: {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// セットアップ完了フラグを作成
    /// </summary>
    private void MarkSetupCompleted()
    {
        try
        {
            if (!Directory.Exists(_appDataFolder))
            {
                Directory.CreateDirectory(_appDataFolder);
            }

            File.WriteAllText(_setupFlagPath, $"Setup completed on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"[SetupManager] Setup flag created: {_setupFlagPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SetupManager] Failed to create setup flag: {ex.Message}");
        }
    }

    /// <summary>
    /// セットアップ完了フラグをリセット（開発・テスト用）
    /// </summary>
    public void ResetSetupFlag()
    {
        try
        {
            if (File.Exists(_setupFlagPath))
            {
                File.Delete(_setupFlagPath);
                Console.WriteLine($"[SetupManager] Setup flag deleted: {_setupFlagPath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SetupManager] Failed to delete setup flag: {ex.Message}");
        }
    }
}
