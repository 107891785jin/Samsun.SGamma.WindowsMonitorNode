using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal static class MonitorUtils
    {
        public const string WinStation = "Win监控";
        public const string SgammaStation = "SGamma监控";

        /// <summary>监控日志根目录。</summary>
        public const string LogRootDirectory = @"D:\SAMSUN-Log\SGamma标准日志";

        private static readonly object _logFileSync = new object();

        /// <summary>
        /// 写出监控日志，直接写入文本文件：LogRootDirectory\日期\小时\ {站点} 日志类型 HH时.txt。
        /// 命名参考：{Win监控} 异常日志 17时.txt、{SGamma监控} 异常日志 17时.txt、{Win监控} 磁盘IO日志 17时.txt。
        /// 使用全局锁串行化同文件追加写，避免后台各采样循环并发写导致的行交错。
        /// </summary>
        public static void WriteLog(string logType, string content, string station = WinStation)
        {
            try
            {
                DateTime now = DateTime.Now;
                // 目录：根\yyyy年MM月dd日\yyyy年MM月dd日HH时\（例：SGamma标准日志\2026年09月09日\2026年09月09日09时\）
                string dateDir = now.ToString("yyyy年MM月dd日");
                string hourDir = now.ToString("yyyy年MM月dd日HH时");
                string hour = now.ToString("HH");
                string dir = Path.Combine(LogRootDirectory, dateDir, hourDir);
                Directory.CreateDirectory(dir);

                string fileName = $"{{{station}}} {logType} {hour}时.txt";
                string filePath = Path.Combine(dir, fileName);

                string line = (content ?? string.Empty).TrimStart('\r', '\n');

                lock (_logFileSync)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug("[SystemMonitor] 写入日志失败: " + ex.Message);
            }
        }

        public static string FormatNow() => DateTime.Now.ToString("HH时mm分ss秒fff毫秒");

        public static List<string> SplitMultiValue(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return input
                .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static int MinEnabledPeriod(params (bool enabled, int seconds)[] values)
        {
            var periods = values.Where(x => x.enabled).Select(x => Math.Max(1, x.seconds)).ToArray();
            return periods.Length == 0 ? 1 : periods.Min();
        }

        public static bool IsDue(DateTime lastRun, int periodSec, DateTime now)
        {
            if (lastRun == DateTime.MinValue) return true;
            return now - lastRun >= TimeSpan.FromSeconds(Math.Max(1, periodSec));
        }

        public static async System.Threading.Tasks.Task DelaySafeAsync(TimeSpan delay, System.Threading.CancellationToken token)
        {
            if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
            await System.Threading.Tasks.Task.Delay(delay, token).ConfigureAwait(false);
        }

        public static async System.Threading.Tasks.Task DelayRemainingAsync(DateTime cycleStartedUtc, TimeSpan targetPeriod, System.Threading.CancellationToken token)
        {
            var remaining = targetPeriod - (DateTime.UtcNow - cycleStartedUtc);
            if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromMilliseconds(10);
            await System.Threading.Tasks.Task.Delay(remaining, token).ConfigureAwait(false);
        }

        public static (bool canRead, bool canWrite, string error) CheckPathAccess(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return (false, false, "路径为空");

            bool canRead = false;
            bool canWrite = false;
            string readError = null;
            string writeError = null;

            if (File.Exists(path))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    canRead = true;
                }
                catch (Exception ex) { readError = ex.Message; }

                try
                {
                    // 只申请写句柄，不改变文件内容。
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    canWrite = true;
                }
                catch (Exception ex) { writeError = ex.Message; }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    // EnumerateFileSystemEntries 是惰性枚举，必须 MoveNext 才真正触发目录读取。
                    using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                    _ = enumerator.MoveNext();
                    canRead = true;
                }
                catch (Exception ex) { readError = ex.Message; }

                string tempPath = null;
                try
                {
                    tempPath = Path.Combine(path, ".__sgamma_perm_" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    File.Delete(tempPath);
                    tempPath = null;
                    canWrite = true;
                }
                catch (Exception ex) { writeError = ex.Message; }
                finally
                {
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
            else
            {
                return (false, false, "路径不存在");
            }

            var errors = new List<string>();
            if (!canRead) errors.Add("读取失败" + (string.IsNullOrWhiteSpace(readError) ? string.Empty : ": " + readError));
            if (!canWrite) errors.Add("写入失败" + (string.IsNullOrWhiteSpace(writeError) ? string.Empty : ": " + writeError));
            return (canRead, canWrite, errors.Count == 0 ? null : string.Join("；", errors));
        }

        // ------ 进程工作集（PSAPI，直达 Win32 API，避免本地化性能计数器）------
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out PROCESS_MEMORY_COUNTERS ppsmemCounters, uint cb);

        /// <summary>
        /// 读取进程的物理工作集（Working Set，物理驻留内存，字节数）。失败返回 0。
        /// 与任务管理器"内存"列口径一致；不要用 PrivateUsage（Private Bytes，已提交但可能未驻留），否则会虚高（如 SQL Server/ToDesk）。
        /// </summary>
        public static long GetWorkingSetSizeBytes(IntPtr processHandle)
        {
            if (processHandle == IntPtr.Zero) return 0;
            try
            {
                var counters = new PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS)) };
                if (!GetProcessMemoryInfo(processHandle, out counters, counters.cb))
                    return 0;
                return (long)counters.WorkingSetSize.ToUInt64();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 从 PhysicalDisk 实例名中提取盘符标签，例如 "0 C:" → "C盘"、"1 D: E:" → "D/E盘"。
        /// </summary>
        public static string ExtractDriveLabel(string physicalDisk)
        {
            if (string.IsNullOrWhiteSpace(physicalDisk)) return physicalDisk ?? "?";
            var letters = Regex.Matches(physicalDisk, @"([A-Za-z]):")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return letters.Count == 0 ? physicalDisk : string.Join("/", letters) + "盘";
        }
    }
}