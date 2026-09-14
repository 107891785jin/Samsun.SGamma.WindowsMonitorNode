using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers
{
    /// <summary>系统总 CPU 采样（GetSystemTimes，Processor Time 口径，不除逻辑核数）。</summary>
    internal sealed class ProcessorTimeCpuSampler
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

        private readonly object _sync = new object();
        private bool _hasPrevious;
        private ulong _prevIdle;
        private ulong _prevKernel;
        private ulong _prevUser;
        private long _prevTimestamp;

        public CpuSnapshot Capture()
        {
            lock (_sync)
            {
                var snapshot = new CpuSnapshot { CapturedAt = DateTime.Now, MetricSource = CpuMetricSource.ProcessorTime };
                long timestamp = Stopwatch.GetTimestamp();

                if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
                {
                    snapshot.Error = "GetSystemTimes 调用失败，Win32Error=" + Marshal.GetLastWin32Error();
                    return snapshot;
                }

                ulong idle = FileTimeToUInt64(idleFt);
                ulong kernel = FileTimeToUInt64(kernelFt);
                ulong user = FileTimeToUInt64(userFt);

                if (!_hasPrevious)
                {
                    _hasPrevious = true;
                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;
                    _prevTimestamp = timestamp;
                    snapshot.Error = "CPU采样器预热中";
                    return snapshot;
                }

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

                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _prevTimestamp = timestamp;
                return snapshot;
            }
        }

        private static ulong FileTimeToUInt64(FILETIME time) => ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;
        private static ulong SafeDelta(ulong current, ulong previous) => current >= previous ? current - previous : 0;
        private static double ClampPercent(double value) => value < 0 ? 0 : (value > 100 ? 100 : value);
    }
}