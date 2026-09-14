using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 兼容门面：内部实现已下沉到 Infrastructure 下职责单一的各工具类。
    /// 保留本类以便既有调用点零回归迁移；后续可在确认无回归后逐步将调用点改为直接引用具体类并删除本层。
    /// </summary>
    internal static class MonitorUtils
    {
        public const string WinStation = "Win监控";
        public const string SgammaStation = "SGamma监控";

        public static string LogRootDirectory
        {
            get => MonitorLogFileWriter.LogRootDirectory;
            set => MonitorLogFileWriter.LogRootDirectory = value;
        }

        public static void WriteLog(string logType, string content, string station = WinStation)
            => MonitorLogFileWriter.WriteLog(logType, content, station ?? WinStation);

        public static string FormatNow() => MonitorTiming.FormatNow();

        public static List<string> SplitMultiValue(string input) => MonitorText.SplitMultiValue(input);

        public static int MinEnabledPeriod(params (bool enabled, int seconds)[] values) => MonitorTiming.MinEnabledPeriod(values);

        public static bool IsDue(System.DateTime lastRun, int periodSec, System.DateTime now) => MonitorTiming.IsDue(lastRun, periodSec, now);

        public static Task DelaySafeAsync(System.TimeSpan delay, CancellationToken token) => MonitorTiming.DelaySafeAsync(delay, token);

        public static Task DelayRemainingAsync(System.DateTime cycleStartedUtc, System.TimeSpan targetPeriod, CancellationToken token)
            => MonitorTiming.DelayRemainingAsync(cycleStartedUtc, targetPeriod, token);

        public static (bool canRead, bool canWrite, string error) CheckPathAccess(string path) => PathAccessChecker.CheckPathAccess(path);

        public static long GetWorkingSetSizeBytes(System.IntPtr processHandle) => ProcessMemoryReader.GetWorkingSetSizeBytes(processHandle);

        public static string ExtractDriveLabel(string physicalDisk) => MonitorText.ExtractDriveLabel(physicalDisk);
    }
}