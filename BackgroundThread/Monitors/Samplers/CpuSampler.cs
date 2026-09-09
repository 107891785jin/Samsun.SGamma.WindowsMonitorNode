using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// CPU 单一采样器（生产级实现）。
    /// 总CPU使用 GetSystemTimes 的 Idle/Kernel/User 时间差；进程CPU使用 GetProcessTimes 的 Kernel+User 时间差，
    /// 按“时间复杂度差 / (墙钟时间差 × 逻辑处理器数)”计算，与新版任务管理器口径一致（多核并行时不超过 100%×核数）。
    /// 用 OpenProcess + CreationTime 双重校验防止 PID 重用；逻辑核数用 GetActiveProcessorCount，避免依赖本地化性能计数器。
    /// Capture 不主动 Delay，采样窗口由上层统一调度周期决定（本项目默认每 1 秒一次，供警告/异常/UI/日志共用同一份数据）。
    /// </summary>
    internal sealed class CpuSampler
    {
        private const ushort ALL_PROCESSOR_GROUPS = 0xFFFF;
        private const ulong FILETIME_TICKS_PER_SEC = 10_000_000UL;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryLimitedInformation = 0x1000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME creationTime, out FILETIME exitTime, out FILETIME kernelTime, out FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);

        private readonly int _logicalProcessorCount;

        private readonly object _sync = new object();
        private bool _hasPrevious;
        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private long _prevTimestamp;
        private Dictionary<int, ProcessCpuPoint> _prevProcesses = new Dictionary<int, ProcessCpuPoint>();

        private sealed class ProcessCpuPoint
        {
            public string Name { get; set; }
            public ulong CreationTime { get; set; }
            public ulong KernelTime { get; set; }
            public ulong UserTime { get; set; }
        }

        public CpuSampler()
        {
            uint count = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
            _logicalProcessorCount = count > 0 ? checked((int)count) : Environment.ProcessorCount;
        }

        public CpuSnapshot Capture(double detailThresholdPercent)
        {
            lock (_sync)
            {
                var snapshot = new CpuSnapshot { CapturedAt = DateTime.Now };
                long timestamp = Stopwatch.GetTimestamp();

                try
                {
                    if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                    {
                        snapshot.Error = "GetSystemTimes 调用失败，Win32Error=" + Marshal.GetLastWin32Error();
                        return snapshot;
                    }

                    ulong idle = FileTimeToUInt64(idleFt);
                    ulong kernel = FileTimeToUInt64(kernelFt);
                    ulong user = FileTimeToUInt64(userFt);
                    var currentProcesses = CaptureProcesses();

                    if (!_hasPrevious)
                    {
                        _hasPrevious = true;
                        _prevIdle = idle;
                        _prevKernel = kernel;
                        _prevUser = user;
                        _prevTimestamp = timestamp;
                        _prevProcesses = currentProcesses;
                        snapshot.Error = "CPU采样器预热中";
                        return snapshot;
                    }

                    // ---------- 总 CPU（GetSystemTimes 的 Kernel 已包含 Idle）----------
                    ulong idleDelta = SafeDelta(idle, _prevIdle);
                    ulong kernelDelta = SafeDelta(kernel, _prevKernel);
                    ulong userDelta = SafeDelta(user, _prevUser);
                    ulong totalDelta = kernelDelta + userDelta;

                    if (totalDelta == 0)
                    {
                        snapshot.Error = "CPU采样时间差为0";
                    }
                    else
                    {
                        ulong busyDelta = totalDelta >= idleDelta ? totalDelta - idleDelta : 0;
                        snapshot.UsagePercent = Math.Round(ClampPercent(busyDelta * 100.0 / totalDelta), 1);
                        snapshot.IsValid = true;
                    }

                    // ---------- 墙钟采样时间 ----------
                    double elapsedSeconds = (timestamp - _prevTimestamp) / (double)Stopwatch.Frequency;

                    // ---------- 进程 CPU（GetProcessTimes 时间差 / 核数）----------
                    var usages = new List<ProcUsage>();
                    if (elapsedSeconds > 0)
                    {
                        foreach (var kv in currentProcesses)
                        {
                            if (!_prevProcesses.TryGetValue(kv.Key, out var prev))
                                continue; // 新启动进程，无基线，暂不能计算

                            // PID 重用防护：CreationTime 不一致说明不是同一个进程
                            if (prev.CreationTime != kv.Value.CreationTime)
                                continue;

                            ulong prevCpu = prev.KernelTime + prev.UserTime;
                            ulong currentCpu = kv.Value.KernelTime + kv.Value.UserTime;
                            if (currentCpu < prevCpu)
                                continue;

                            double processCpuSeconds = (currentCpu - prevCpu) / (double)FILETIME_TICKS_PER_SEC;
                            double pct = processCpuSeconds / elapsedSeconds / _logicalProcessorCount * 100.0;
                            pct = ClampPercent(pct);

                            usages.Add(new ProcUsage
                            {
                                ProcessId = kv.Key,
                                Name = kv.Value.Name,
                                Percent = Math.Round(Math.Max(0, pct), 1)
                            });
                        }
                    }

                    // 只有达到可能需要写日志的阈值时才构造 Top 列表；进程原始时间点仍每次采集，以便下轮算差值。
                    if (snapshot.IsValid && snapshot.UsagePercent >= detailThresholdPercent)
                    {
                        snapshot.TopProcesses = usages
                            .OrderByDescending(x => x.Percent)
                            .ThenBy(x => x.ProcessId)
                            .Take(10)
                            .ToList();
                    }

                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;
                    _prevTimestamp = timestamp;
                    _prevProcesses = currentProcesses;
                }
                catch (Exception ex)
                {
                    snapshot.IsValid = false;
                    snapshot.Error = ex.Message;
                    LogHelper.Debug("[SystemMonitor] CPU采样失败: " + ex.Message);
                }

                return snapshot;
            }
        }

        private static Dictionary<int, ProcessCpuPoint> CaptureProcesses()
        {
            var result = new Dictionary<int, ProcessCpuPoint>();
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    try
                    {
                        int pid = process.Id;
                        if (pid == 0)
                            continue; // PID 0 = System Idle Process，非实际用户进程

                        string name;
                        try { name = process.ProcessName; }
                        catch { name = $"PID {pid}"; }

                        IntPtr handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, pid);
                        if (handle == IntPtr.Zero)
                            continue;

                        try
                        {
                            if (!GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
                                continue;

                            result[pid] = new ProcessCpuPoint
                            {
                                Name = name,
                                CreationTime = FileTimeToUInt64(creation),
                                KernelTime = FileTimeToUInt64(kernel),
                                UserTime = FileTimeToUInt64(user)
                            };
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                    catch
                    {
                        // 进程可能刚好退出或属于受保护进程，单个失败不影响整体采样
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                foreach (var p in processes) { try { p.Dispose(); } catch { } }
            }
            return result;
        }

        private static ulong FileTimeToUInt64(FILETIME time) => ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;
        private static ulong SafeDelta(ulong current, ulong previous) => current >= previous ? current - previous : 0;
        private static double ClampPercent(double value) => value < 0 ? 0 : (value > 100 ? 100 : value);
    }
}