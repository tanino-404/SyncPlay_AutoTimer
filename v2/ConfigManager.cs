using System.Text;
using System.Text.RegularExpressions;

namespace v2;

/// <summary>
/// config.ini ファイルの読み込みと管理
/// </summary>
public class ConfigManager
{
    private readonly string _configPath;
    private Dictionary<string, Dictionary<string, string>> _config = new();

    public ConfigManager(string configPath = "config.ini")
    {
        _configPath = configPath;
    }

    /// <summary>
    /// config.ini ファイルを読み込む
    /// </summary>
    public bool LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Console.WriteLine($"[ConfigManager] Config file not found: {_configPath}");
                Console.WriteLine($"[ConfigManager] Using hardcoded defaults instead.");
                return false;
            }

            _config.Clear();
            string currentSection = "";

            foreach (string line in File.ReadLines(_configPath, Encoding.UTF8))
            {
                string trimmedLine = line.Trim();

                // コメント行スキップ
                if (trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
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

            Console.WriteLine($"[ConfigManager] Config loaded: {_configPath}");
            PrintLoadedConfig();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigManager] Error loading config: {ex.Message}");
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
        string template = @"# SyncPlay_AutoTimer v2.0 Configuration File
# Last updated: 2025-12-01

[Syncplay]
MpvPath=C:\Program Files\mpv\mpv.exe
SyncplayServerPath=C:\Program Files (x86)\Syncplay\syncplayServer.exe
SyncplayClientPath=C:\Program Files (x86)\Syncplay\SyncplayConsole.exe
ServerIP=192.168.100.13
ServerPort=8999
UserName=Server
RoomName=Test_Run
RoomPassword=

[Player]
VideoFilePath=..\Video\Terminal0_JP_Video_R_250915_v1.mp4

[Keyboard]
KeyToggle=alt+ctrl+shift+p
KeyQuit=alt+ctrl+shift+q
KeyRestart=alt+ctrl+shift+r
KeyStatus=alt+ctrl+shift+s

[Network]
ClientIPList=192.168.100.54
HttpServerPort=8080
CommandTimeout=500
CommandRetry=2

[Timing]
TargetTimes=13:20,00:00,00:00,00:00
AutoStopMinutes=1
AutoPlayDelay=3

[Mode]
ServerMode=true
AutoStopMode=true
MinimizeStartMode=true
DebugMode=false
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
