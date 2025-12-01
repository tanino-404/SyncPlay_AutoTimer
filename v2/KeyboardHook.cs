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

    // キー入力状態の追跡（Alt, Ctrl, Shift の複合判定）
    private bool _isAltPressed = false;
    private bool _isCtrlPressed = false;
    private bool _isShiftPressed = false;

    // キーマッピング（Alt+Ctrl+Shift + キーコード → アクション）
    private readonly Dictionary<int, string> _keyMap = new()
    {
        { 0x50, "toggle" },    // Alt+Ctrl+Shift+P
        { 0x51, "quit" },      // Alt+Ctrl+Shift+Q
        { 0x52, "restart" },   // Alt+Ctrl+Shift+R
        { 0x53, "status" }     // Alt+Ctrl+Shift+S
    };

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

    // VK_* キーコード（修飾キー判定用）
    private const int VK_LALT = 0xA4;
    private const int VK_RALT = 0xA5;
    private const int VK_LCTRL = 0xA2;
    private const int VK_RCTRL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

    public bool IsInitialized { get; private set; } = false;

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
            Console.WriteLine("[KeyboardHook] Initialized successfully.");
            Console.WriteLine("Keyboard hook is now monitoring for Alt+Ctrl+Shift+P/Q/R/S input.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[KeyboardHook] Initialization failed: {ex.Message}");
            throw;
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
                // 修飾キー状態を更新
                _isAltPressed = IsKeyPressed(VK_LALT) || IsKeyPressed(VK_RALT);
                _isCtrlPressed = IsKeyPressed(VK_LCTRL) || IsKeyPressed(VK_RCTRL);
                _isShiftPressed = IsKeyPressed(VK_LSHIFT) || IsKeyPressed(VK_RSHIFT);

                // Alt+Ctrl+Shift が同時押しされている場合、キーマップを確認
                if (_isAltPressed && _isCtrlPressed && _isShiftPressed)
                {
                    if (_keyMap.TryGetValue(vkCode, out var action))
                    {
                        var keyEvent = new KeyEvent
                        {
                            Timestamp = DateTime.UtcNow,
                            Action = action,
                            Details = $"VK={vkCode:X2}"
                        };

                        EventQueue.Enqueue(keyEvent);
                        Console.WriteLine($"[KeyboardHook] Detected: {keyEvent}");
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
