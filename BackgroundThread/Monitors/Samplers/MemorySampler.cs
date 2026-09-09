using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal sealed class MemorySampler : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [Flags]
        private enum ProcessAccessFlags : uint
        {
            QueryLimitedInformation = 0x1000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private readonly object _sync = new object();

        public MemorySnapshot Capture(double detailThresholdPercent)
        {
            lock (_sync)
            {
                var snapshot = new MemorySnapshot { CapturedAt = DateTime.Now };
                try
                {
                    var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                    if (!GlobalMemoryStatusEx(ref mem))
                    {
                        snapshot.Error = "GlobalMemoryStatusEx 调用失败，Win32Error=" + Marshal.GetLastWin32Error();
                        return snapshot;
                    }

                    double totalBytes = mem.ullTotalPhys;
                    double availableBytes = mem.ullAvailPhys;
                    double usedBytes = Math.Max(0, totalBytes - availableBytes);

                    snapshot.TotalGB = Math.Round(totalBytes / 1024d / 1024d / 1024d, 2);
                    snapshot.AvailableGB = Math.Round(availableBytes / 1024d / 1024d / 1024d, 2);
                    snapshot.UsedGB = Math.Round(usedBytes / 1024d / 1024d / 1024d, 2);
                    snapshot.UsagePercent = totalBytes <= 0 ? 0 : Math.Round(Math.Clamp(usedBytes * 100d / totalBytes, 0, 100), 1);
                    snapshot.IsValid = true;

                    if (snapshot.UsagePercent >= detailThresholdPercent)
                        snapshot.TopProcesses = CaptureTopPrivateWorkingSet();
                }
                catch (Exception ex)
                {
                    snapshot.IsValid = false;
                    snapshot.Error = ex.Message;
                    LogHelper.Debug("[SystemMonitor] 内存采样失败: " + ex.Message);
                }
                return snapshot;
            }
        }

        private List<ProcMemUsage> CaptureTopPrivateWorkingSet()
        {
            var list = new List<ProcMemUsage>();
            Process[] processes = Array.Empty<Process>();
            try
            {
                processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    int pid = process.Id;
                    if (pid == 0) continue; // 系统空闲进程不计入

                    string name;
                    try { name = process.ProcessName; }
                    catch { name = $"PID {pid}"; }

                    IntPtr handle = OpenProcess(ProcessAccessFlags.QueryLimitedInformation, false, pid);
                    if (handle == IntPtr.Zero) continue;

                    long bytes;
                    try { bytes = MonitorUtils.GetWorkingSetSizeBytes(handle); }
                    finally { CloseHandle(handle); }

                    if (bytes <= 0) continue;
                    list.Add(new ProcMemUsage
                    {
                        ProcessId = pid,
                        Name = name,
                        GB = Math.Round(bytes / 1024d / 1024d / 1024d, 2)
                    });
                }
            }
            catch
            {
            }
            finally
            {
                foreach (var p in processes) { try { p.Dispose(); } catch { } }
            }

            return list.OrderByDescending(x => x.GB).Take(10).ToList();
        }

        public void Dispose()
        {
        }
    }
}