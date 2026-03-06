using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using v2.Models;

namespace v2;

/// <summary>
/// Windows API SetWindowsHookEx を使用して OS レベルキー入力を監視
/// </summary>
public class KeyboardHook : IDisposable
{
    // Windows API 定数
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    // キーマッピング（キーコード → (アクション, 必要な修飾キー)）
    private readonly Dictionary<int, (string Action, bool Alt, bool Ctrl, bool Shift)> _keyMap = new();
    private readonly ConfigManager? _configManager;

    // イベントキュー（スレッドセーフ）
    public ConcurrentQueue<KeyEvent> EventQueue { get; } = new();

    // フックハンドル
    private IntPtr _hookId = IntPtr.Zero;
    private IntPtr _moduleHandle = IntPtr.Zero;

    // デリゲート参照（ガベージコレクション防止用）
    private LowLevelKeyboardProc? _keyboardProc;

    // P/Invoke デリゲート定義
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // P/Invoke 宣言
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    // VK_* キーコード（修飾キー判定用）
    private const int VK_LALT = 0xA4;
    private const int VK_RALT = 0xA5;
    private const int VK_LCTRL = 0xA2;
    private const int VK_RCTRL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

    public bool IsInitialized { get; private set; } = false;

    public KeyboardHook(ConfigManager? configManager = null)
    {
        _configManager = configManager;
    }

    /// <summary>
    /// キーボードフックを初期化・開始
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            Console.WriteLine("[KeyboardHook] Already initialized.");
            return;
        }

        try
        {
            // デリゲート作成（ガベージコレクション防止）
            _keyboardProc = KeyboardProc;

            // カレントプロセスのモジュールハンドル取得
            _moduleHandle = GetModuleHandle(null);

            if (_moduleHandle == IntPtr.Zero)
            {
                throw new Exception("Failed to get module handle.");
            }

            // フック設定
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, _moduleHandle, 0);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"SetWindowsHookEx failed with error: {error}");
            }

            IsInitialized = true;

            // キーマップを config.ini から動的生成
            LoadKeyBindingsFromConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[KeyboardHook] Initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// config.ini からキーバインドを読み込む
    /// </summary>
    private void LoadKeyBindingsFromConfig()
    {
        if (_configManager != null)
        {
            var bindings = new Dictionary<string, string>
            {
                { "toggle", _configManager.GetValue("Keyboard", "KeyToggle", "alt+ctrl+shift+p")! },
                { "quit", _configManager.GetValue("Keyboard", "KeyQuit", "alt+ctrl+shift+q")! },
                { "restart", _configManager.GetValue("Keyboard", "KeyRestart", "alt+ctrl+shift+r")! },
                { "status", _configManager.GetValue("Keyboard", "KeyStatus", "alt+ctrl+shift+s")! }
            };

            foreach (var (action, keyBinding) in bindings)
            {
                var (alt, ctrl, shift, keyCode) = _configManager.ParseKeyBinding(keyBinding);
                if (keyCode > 0)
                {
                    _keyMap[keyCode] = (action, alt, ctrl, shift);
                }
                else
                {
                    Console.WriteLine($"[KeyboardHook] ⚠️  Invalid key binding: {keyBinding} (action: {action})");
                }
            }
        }
        else
        {
            // フォールバック: デフォルトのハードコードマップ
            Console.WriteLine("[KeyboardHook] ConfigManager not available, using default key bindings.");
            _keyMap[0x50] = ("toggle", true, true, true);   // Alt+Ctrl+Shift+P
            _keyMap[0x51] = ("quit", true, true, true);     // Alt+Ctrl+Shift+Q
            _keyMap[0x52] = ("restart", true, true, true);  // Alt+Ctrl+Shift+R
            _keyMap[0x53] = ("status", true, true, true);   // Alt+Ctrl+Shift+S
        }
    }

    /// <summary>
    /// キーボードフック処理
    /// </summary>
    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);

            // キーダウンイベントのみ処理
            if (wParam == (IntPtr)WM_KEYDOWN)
            {
                // 修飾キー状態を取得
                bool altPressed = IsKeyPressed(VK_LALT) || IsKeyPressed(VK_RALT);
                bool ctrlPressed = IsKeyPressed(VK_LCTRL) || IsKeyPressed(VK_RCTRL);
                bool shiftPressed = IsKeyPressed(VK_LSHIFT) || IsKeyPressed(VK_RSHIFT);

                // キーマップから該当するアクションを検索
                if (_keyMap.TryGetValue(vkCode, out var mapping))
                {
                    // 修飾キーが一致するか確認
                    if (mapping.Alt == altPressed &&
                        mapping.Ctrl == ctrlPressed &&
                        mapping.Shift == shiftPressed)
                    {
                        var keyEvent = new KeyEvent
                        {
                            Timestamp = DateTime.Now,
                            Action = mapping.Action,
                            Details = $"VK={vkCode:X2}"
                        };

                        EventQueue.Enqueue(keyEvent);
                        Console.WriteLine($"\n[KeyboardHook] Detected: {keyEvent}");
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// 指定されたキーが現在押下されているかを判定
    /// </summary>
    private bool IsKeyPressed(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    /// <summary>
    /// Windowsメッセージループを実行
    /// キーボードフックを動作させるために必要
    /// </summary>
    public void RunMessageLoop()
    {
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    /// <summary>
    /// キーボードフック をアンインストール
    /// </summary>
    public void Shutdown()
    {
        if (_hookId != IntPtr.Zero)
        {
            if (UnhookWindowsHookEx(_hookId))
            {
                Console.WriteLine("[KeyboardHook] Unhooked successfully.");
                _hookId = IntPtr.Zero;
                IsInitialized = false;
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"[KeyboardHook] Unhook failed with error: {error}");
            }
        }
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    ~KeyboardHook()
    {
        Shutdown();
    }
}
