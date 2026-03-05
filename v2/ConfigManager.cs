using System.Text;
using System.Text.RegularExpressions;

namespace v2;

/// <summary>
/// config.ini ファイルの読み込みと管理
/// Windows Documents フォルダ内の固定パスを使用
/// </summary>
public class ConfigManager
{
    private readonly string _configPath;
    private readonly string _configDirectory;
    private Dictionary<string, Dictionary<string, string>> _config = new();

    public ConfigManager()
    {
        // Windows Documents フォルダを取得
        string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _configDirectory = Path.Combine(documentsFolder, "SyncPlay_AutoTimer", "Setting");
        _configPath = Path.Combine(_configDirectory, "config.ini");
    }

    /// <summary>
    /// 設定ファイルパスを取得
    /// </summary>
    public string ConfigPath => _configPath;

    /// <summary>
    /// 設定ディレクトリを取得
    /// </summary>
    public string ConfigDirectory => _configDirectory;

    /// <summary>
    /// HTTPサーバーポート番号を取得
    /// </summary>
    public int HttpServerPort => int.Parse(GetValue("Network", "HttpServerPort", "8080") ?? "8080");

    /// <summary>
    /// 起動時の最小化モードを取得
    /// </summary>
    public bool MinimizeStartMode => GetBool("Mode", "MinimizeStartMode", false);

    /// <summary>
    /// サーバーモードを取得
    /// </summary>
    public bool ServerMode => GetBool("Mode", "ServerMode", false);

    /// <summary>
    /// 最前面化タイマーの実行間隔（ミリ秒）を取得
    /// </summary>
    public int TopMostIntervalMs => GetInt("Player", "TopMostIntervalMs", 100);

    /// <summary>
    /// スケジュールモードを取得（ServerMode=true の場合のみ有効）
    /// </summary>
    public bool ScheduleMode => GetBool("Mode", "ScheduleMode", false);

    /// <summary>
    /// ループモードを取得（動画終了後にアプリケーション側で自動再起動）
    /// </summary>
    public bool LoopMode => GetBool("Mode", "LoopMode", false);

    /// <summary>
    /// スケジュール時刻リストを取得（HH:MM または HH:MM:SS 形式）
    /// </summary>
    public TimeSpan[] TargetTimes => ParseTargetTimes(GetValue("Timing", "TargetTimes", ""));

    /// <summary>
    /// config.ini ファイルを読み込む
    /// ファイル/フォルダがない場合は自動作成
    /// </summary>
    public bool LoadConfig()
    {
        try
        {
            // フォルダが存在しない場合は作成
            if (!Directory.Exists(_configDirectory))
            {
                Console.WriteLine($"[ConfigManager] Config directory not found: {_configDirectory}");
                Console.WriteLine($"[ConfigManager] Creating directory...");
                Directory.CreateDirectory(_configDirectory);
                Console.WriteLine($"[ConfigManager] ✅ Directory created: {_configDirectory}");
            }

            // Videoフォルダの自動生成
            EnsureVideoFolderExists();

            // ファイルが存在しない場合はテンプレートを作成
            if (!File.Exists(_configPath))
            {
                Console.WriteLine($"[ConfigManager] Config file not found: {_configPath}");
                Console.WriteLine($"[ConfigManager] Creating template file...");
                GenerateTemplate(_configPath);
                Console.WriteLine($"[ConfigManager] ✅ Template created: {_configPath}");
            }

            _config.Clear();
            string currentSection = "";

            foreach (string line in File.ReadLines(_configPath, Encoding.UTF8))
            {
                string trimmedLine = line.Trim();

                // コメント行スキップ
                if (trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                    continue;

                // 空行スキップ
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                // セクション行処理
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
                    if (!_config.ContainsKey(currentSection))
                    {
                        _config[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                // キー=値 行処理
                if (string.IsNullOrWhiteSpace(currentSection))
                    continue;

                int eqIdx = trimmedLine.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = trimmedLine.Substring(0, eqIdx).Trim();
                    string value = trimmedLine.Substring(eqIdx + 1).Trim();
                    _config[currentSection][key] = value;
                }
            }

            Console.WriteLine($"[ConfigManager] ✅ Config loaded: {_configPath}");
            PrintLoadedConfig();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigManager] ❌ Error loading config: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// セクション内のキーの値を取得
    /// </summary>
    public string? GetValue(string section, string key, string? defaultValue = null)
    {
        if (_config.TryGetValue(section, out var sectionData))
        {
            if (sectionData.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// 値を整数として取得
    /// </summary>
    public int GetInt(string section, string key, int defaultValue = 0)
    {
        string? value = GetValue(section, key);
        if (int.TryParse(value, out int result))
        {
            return result;
        }

        return defaultValue;
    }

    /// <summary>
    /// 値をブール値として取得
    /// </summary>
    public bool GetBool(string section, string key, bool defaultValue = false)
    {
        string? value = GetValue(section, key);
        if (value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               value.Equals("1", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (value != null && (value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                               value.Equals("0", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return defaultValue;
    }

    /// <summary>
    /// キーバインド文字列を解析して修飾キーとキーコードを返す
    /// 形式: "alt+ctrl+shift+p" → (Alt=true, Ctrl=true, Shift=true, KeyCode=0x50)
    /// 対応修飾キー: alt, ctrl, shift
    /// 対応キー: a-z, 0-9 (大文字小文字区別なし)
    /// </summary>
    public (bool Alt, bool Ctrl, bool Shift, int KeyCode) ParseKeyBinding(string keyBinding)
    {
        bool alt = false, ctrl = false, shift = false;
        int keyCode = 0;

        var parts = keyBinding.ToLower().Split('+');
        foreach (var part in parts)
        {
            switch (part.Trim())
            {
                case "alt":
                    alt = true;
                    break;
                case "ctrl":
                    ctrl = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                default:
                    // 最後の部分が実際のキー
                    if (part.Length == 1)
                    {
                        char key = part[0];
                        if (key >= 'a' && key <= 'z')
                        {
                            keyCode = 0x41 + (key - 'a'); // A=0x41
                        }
                        else if (key >= '0' && key <= '9')
                        {
                            keyCode = 0x30 + (key - '0'); // 0=0x30
                        }
                    }
                    break;
            }
        }

        return (alt, ctrl, shift, keyCode);
    }

    /// <summary>
    /// カンマ区切りの値を配列として取得
    /// </summary>
    public string[] GetArray(string section, string key, string[]? defaultValue = null)
    {
        string? value = GetValue(section, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue ?? Array.Empty<string>();
        }

        return value.Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    /// <summary>
    /// TargetTimes 文字列を TimeSpan 配列にパース
    /// 対応形式: HH:MM または HH:MM:SS（カンマ区切り）
    /// </summary>
    private TimeSpan[] ParseTargetTimes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<TimeSpan>();
        }

        var result = new List<TimeSpan>();
        var parts = value.Split(',');

        foreach (var part in parts)
        {
            string trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // HH:MM:SS または HH:MM 形式をパース
            if (TimeSpan.TryParse(trimmed, out TimeSpan time))
            {
                // 00:00:00 は無効値として扱う（スキップ）
                if (time != TimeSpan.Zero)
                {
                    result.Add(time);
                }
            }
            else
            {
                Console.WriteLine($"[ConfigManager] Invalid time format: '{trimmed}' (expected HH:MM or HH:MM:SS)");
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Videoフォルダの存在を確認し、なければ作成
    /// </summary>
    private void EnsureVideoFolderExists()
    {
        try
        {
            string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string videoFolder = Path.Combine(documentsFolder, "SyncPlay_AutoTimer", "Video");

            if (!Directory.Exists(videoFolder))
            {
                Console.WriteLine($"[ConfigManager] Video folder not found: {videoFolder}");
                Console.WriteLine($"[ConfigManager] Creating Video folder...");
                Directory.CreateDirectory(videoFolder);
                Console.WriteLine($"[ConfigManager] ✅ Video folder created: {videoFolder}");

                // Video.txt (README) を作成
                string readmePath = Path.Combine(videoFolder, "Video.txt");
                string readmeContent = @"# Sync Play Auto Timer Video Folder

Place your video files here.

Recommended structure:
- display0.mp4 (for Display 0)
- display1.mp4 (for Display 1)

Update config.ini in the Setting folder to configure video paths:
[Player]
Display0VideoPath=..\Video\display0.mp4
Display1VideoPath=..\Video\display1.mp4
";
                File.WriteAllText(readmePath, readmeContent, Encoding.UTF8);
                Console.WriteLine($"[ConfigManager] ✅ Video.txt created: {readmePath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigManager] Error creating Video folder: {ex.Message}");
        }
    }

    /// <summary>
    /// 相対パスを絶対パスに解決
    /// config.ini の親フォルダ（Setting/）を基準とする
    /// </summary>
    public string? ResolveVideoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // 絶対パスの場合はそのまま返す
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // 相対パスの場合は config.ini の親フォルダから解決
        try
        {
            string resolvedPath = Path.GetFullPath(Path.Combine(_configDirectory, path));
            return resolvedPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigManager] Error resolving path '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 読み込み済みの設定を表示
    /// </summary>
    private void PrintLoadedConfig()
    {
        Console.WriteLine("[ConfigManager] Loaded sections and keys:");
        foreach (var section in _config)
        {
            Console.WriteLine($"  [{section.Key}]");
            foreach (var kvp in section.Value)
            {
                // パスワードなどは マスク
                string displayValue = kvp.Key.Contains("password", StringComparison.OrdinalIgnoreCase)
                    ? "***"
                    : kvp.Value;

                Console.WriteLine($"    {kvp.Key} = {displayValue}");
            }
        }
    }

    /// <summary>
    /// config.ini テンプレートを生成
    /// </summary>
    public static void GenerateTemplate(string outputPath = "config.ini")
    {
        string template = @"# Sync Play Auto Timer v2.5.0 Configuration File
# Last updated: 2026-03-05

[Mode]
ServerMode=true
ScheduleMode=true
LoopMode=true
MinimizeStartMode=true
DebugMode=false

[Player]
MpvPath=C:\Program Files\mpv\mpv.exe
Display0VideoPath=..\Video\display0.mp4
Display1VideoPath=..\Video\display1.mp4
TopMostIntervalMs=100

[Network]
ClientIPList=192.168.1.1
HttpServerPort=8080
CommandTimeout=500
CommandRetry=2

[Keyboard]
KeyToggle=alt+ctrl+shift+p
KeyQuit=alt+ctrl+shift+q
KeyRestart=alt+ctrl+shift+r
KeyStatus=alt+ctrl+shift+s

[Timing]
TargetTimes=09:00,14:00,18:30
";

        try
        {
            File.WriteAllText(outputPath, template, Encoding.UTF8);
            Console.WriteLine($"[ConfigManager] Template generated: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigManager] Error generating template: {ex.Message}");
        }
    }
}
