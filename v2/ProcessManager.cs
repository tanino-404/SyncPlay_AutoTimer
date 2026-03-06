using System.Diagnostics;
using System.Runtime.InteropServices;

namespace v2;

/// <summary>
/// Windows Job Objects を使用したプロセス管理
/// 子プロセスをジョブに割り当てることで、親プロセス終了時に自動的に子プロセスも終了される
/// </summary>
public class ProcessManager : IDisposable
{
    // Windows API 定数
    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    // P/Invoke デリゲート・構造体
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    // P/Invoke 宣言
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JOBOBJECTINFOCLASS JobObjectInformationClass,
        IntPtr lpJobObjectInformation,
        uint cbJobObjectInformationLength
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectBasicAccountingInformation = 1,
        JobObjectBasicLimitInformation = 2,
        JobObjectBasicProcessIdList = 3,
        JobObjectBasicUIRestrictions = 4,
        JobObjectSecurityLimitInformation = 5,
        JobObjectEndOfJobTimeInformation = 6,
        JobObjectAssociateCompletionPortInformation = 7,
        JobObjectBasicAndIoAccountingInformation = 8,
        JobObjectExtendedLimitInformation = 9,
        JobObjectJobSetInformation = 10,
        JobObjectGroupInformation = 11,
        JobObjectNotificationLimitInformation = 12,
        JobObjectLimitViolationInformation = 13,
        JobObjectGroupInformationEx = 14,
        JobObjectCpuRateControlInformation = 15,
        JobObjectCompletionFilter = 16,
        JobObjectCompletionCounter = 17,
        JobObjectFreezeInformation = 18,
        JobObjectExtendedAccountingInformation = 19,
        JobObjectWakeFilter = 20,
        JobObjectContainerId = 21
    }

    private IntPtr _jobHandle = IntPtr.Zero;
    private Dictionary<int, Process> _managedProcesses = new();

    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// ジョブオブジェクトを初期化
    /// </summary>
    public void Initialize(string jobName = "SyncPlayAutoTimerJob")
    {
        if (IsInitialized)
        {
            Console.WriteLine("[ProcessManager] Already initialized.");
            return;
        }

        try
        {
            // ジョブオブジェクト作成
            _jobHandle = CreateJobObject(IntPtr.Zero, jobName);

            if (_jobHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"CreateJobObject failed with error: {error}");
            }

            // ジョブの設定：親プロセス終了時に子プロセスを自動終了
            SetJobLimits(_jobHandle);

            IsInitialized = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProcessManager] Initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ジョブの制限設定：親プロセス終了時に子プロセスを終了
    /// </summary>
    private void SetJobLimits(IntPtr jobHandle)
    {
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            if (!SetInformationJobObject(
                jobHandle,
                JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                extendedInfoPtr,
                (uint)length))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"SetInformationJobObject failed with error: {error}");
            }

        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    /// <summary>
    /// 外部プロセスを起動して、ジョブに割り当て
    /// </summary>
    public Process StartManagedProcess(string exePath, string? arguments = null, string? workingDirectory = null)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("ProcessManager is not initialized.");
        }

        try
        {
            // プロセス起動情報設定
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            // プロセス起動
            var process = Process.Start(startInfo)
                ?? throw new Exception($"Failed to start process: {exePath}");

            // ジョブに割り当て
            if (!AssignProcessToJobObject(_jobHandle, process.Handle))
            {
                int error = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"[ProcessManager] Warning: Failed to assign process to job (Error: {error})");
                // ここではエラーをスローしない（プロセスは起動している）
            }

            // 管理リストに追加
            _managedProcesses[process.Id] = process;

            return process;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProcessManager] Failed to start managed process: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 管理下のプロセスを終了
    /// </summary>
    public void TerminateProcess(int processId, int timeoutMs = 5000)
    {
        if (!_managedProcesses.TryGetValue(processId, out var process))
        {
            Console.WriteLine($"[ProcessManager] Process ID {processId} not found in managed list.");
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                Console.WriteLine($"[ProcessManager] Terminating process: PID {processId}");
                process.Kill();

                if (!process.WaitForExit(timeoutMs))
                {
                    Console.Error.WriteLine($"[ProcessManager] Warning: Process {processId} did not exit within timeout.");
                }
            }

            _managedProcesses.Remove(processId);
            Console.WriteLine($"[ProcessManager] Process terminated: PID {processId}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProcessManager] Error terminating process {processId}: {ex.Message}");
        }
    }

    /// <summary>
    /// すべての管理下プロセスを終了
    /// </summary>
    public void TerminateAllProcesses(int timeoutMs = 5000)
    {
        var processIds = _managedProcesses.Keys.ToList();

        foreach (var processId in processIds)
        {
            TerminateProcess(processId, timeoutMs);
        }

        Console.WriteLine($"[ProcessManager] All managed processes terminated ({processIds.Count} processes)");
    }

    /// <summary>
    /// 管理下プロセスの一覧を取得
    /// </summary>
    public IReadOnlyDictionary<int, Process> GetManagedProcesses()
    {
        return _managedProcesses.AsReadOnly();
    }

    /// <summary>
    /// ジョブを終了し、すべてのリソースを解放
    /// </summary>
    public void Shutdown()
    {
        try
        {
            // すべてのプロセスを終了
            TerminateAllProcesses();

            // ジョブハンドルをクローズ
            if (_jobHandle != IntPtr.Zero)
            {
                if (CloseHandle(_jobHandle))
                {
                    Console.WriteLine("[ProcessManager] Job closed successfully.");
                    _jobHandle = IntPtr.Zero;
                    IsInitialized = false;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.Error.WriteLine($"[ProcessManager] Warning: Failed to close job handle (Error: {error})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ProcessManager] Shutdown error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    ~ProcessManager()
    {
        Shutdown();
    }
}
