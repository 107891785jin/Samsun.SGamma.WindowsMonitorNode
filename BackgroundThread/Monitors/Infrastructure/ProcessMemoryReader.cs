using System;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure
{
    /// <summary>进程物理工作集读取（PSAPI，直达 Win32 API，避免本地化性能计数器）。</summary>
    internal static class ProcessMemoryReader
    {
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
        /// 与任务管理器"内存"列口径一致；不要用 PrivateUsage（会虚高）。
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
    }
}