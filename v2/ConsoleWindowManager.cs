using System.Runtime.InteropServices;

namespace v2;

/// <summary>
/// コンソールウィンドウの管理クラス
/// ウィンドウの最小化・非表示を提供
/// </summary>
public class ConsoleWindowManager
{
    // Win32 API 定義
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    // 定数
    private const int SW_MINIMIZE = 6;
    private const int SW_HIDE = 0;

    private IntPtr _consoleHandle;

    /// <summary>
    /// コンソールウィンドウハンドルを取得
    /// </summary>
    public ConsoleWindowManager()
    {
        _consoleHandle = GetConsoleWindow();
    }

    /// <summary>
    /// コンソールウィンドウを最小化
    /// </summary>
    /// <returns>成功した場合 true</returns>
    public bool MinimizeWindow()
    {
        if (_consoleHandle == IntPtr.Zero)
        {
            Console.Error.WriteLine("[ConsoleWindowManager] Failed to get console window handle.");
            return false;
        }

        try
        {
            bool result = ShowWindow(_consoleHandle, SW_MINIMIZE);
            if (result)
            {
                Console.WriteLine("[ConsoleWindowManager] Console window minimized successfully.");
            }
            else
            {
                Console.Error.WriteLine("[ConsoleWindowManager] Failed to minimize console window.");
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConsoleWindowManager] Error minimizing window: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// コンソールウィンドウを完全に非表示（Phase 4 以降用）
    /// </summary>
    /// <returns>成功した場合 true</returns>
    public bool HideWindow()
    {
        if (_consoleHandle == IntPtr.Zero)
        {
            Console.Error.WriteLine("[ConsoleWindowManager] Failed to get console window handle.");
            return false;
        }

        try
        {
            bool result = ShowWindow(_consoleHandle, SW_HIDE);
            if (result)
            {
                Console.WriteLine("[ConsoleWindowManager] Console window hidden successfully.");
            }
            else
            {
                Console.Error.WriteLine("[ConsoleWindowManager] Failed to hide console window.");
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConsoleWindowManager] Error hiding window: {ex.Message}");
            return false;
        }
    }

}
