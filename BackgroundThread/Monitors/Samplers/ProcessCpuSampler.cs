using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers
{
    /// <summary>
    /// 进程 CPU Top10 采样（WMI Win32_PerfFormattedData_PerfProc_Process）。
    /// WMI 格式化类直接给 PercentProcessorTime（Processor Time 口径），
    /// 但底层 PerfMon 计数器在多处理器系统上不会自动除以核数，需手动除以逻辑核数（0-100）。
    /// 规避本地化计数器名，无需两次采样差值计算。
    /// </summary>
    internal sealed class ProcessCpuSampler
    {
        private const ushort ALL_PROCESSOR_GROUPS = 0xFFFF;

        [DllImport("kernel32.dll")]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);

        /// <summary>WMI 格式化性能类固定类名（不本地化）。</summary>
        private const string PerfProcClass = "Win32_PerfFormattedData_PerfProc_Process";

        /// <summary>逻辑处理器数，用于把 Per-Monitor CPU 转换为系统总 CPU 百分比。</summary>
        private readonly int _logicalProcessorCount;

        public ProcessCpuSampler()
        {
            uint count = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
            _logicalProcessorCount = count > 0 ? checked((int)count) : Environment.ProcessorCount;
        }

        /// <summary>采样一轮进程 CPU，返回全部进程占比列表（0-100，已除核数）。</summary>
        public List<ProcUsage> Capture()
        {
            var result = new List<ProcUsage>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, IDProcess, PercentProcessorTime FROM " + PerfProcClass);
                using var collection = searcher.Get();
                foreach (ManagementObject mo in collection)
                {
                    using (mo)
                    {
                        string rawName = Convert.ToString(mo["Name"] ?? string.Empty);
                        if (string.IsNullOrEmpty(rawName)) continue;
                        // 过滤系统项：_Total 合计、Idle 空闲进程
                        if (string.Equals(rawName, "_Total", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(rawName, "Idle", StringComparison.OrdinalIgnoreCase)) continue;

                        // Name 字段可能带 #N 后缀（同名多实例如 chrome#1），去掉后缀只取进程名
                        string displayName = StripInstanceSuffix(rawName);

                        int pid = 0;
                        try { pid = Convert.ToInt32(mo["IDProcess"]); }
                        catch { }
                        if (pid <= 0) continue;

                        double pct = 0;
                        try { pct = Convert.ToDouble(mo["PercentProcessorTime"]); }
                        catch { }

                        // 关键：PerfMon 的 \Process(*)\% Processor Time 在多处理器系统上不自动除以核数，
                        // 单进程满载 N 个核心会显示 N×100%。手动除以逻辑核数，与系统总 CPU 口径对齐。
                        if (_logicalProcessorCount > 1)
                            pct /= _logicalProcessorCount;

                        result.Add(new ProcUsage
                        {
                            ProcessId = pid,
                            Name = displayName,
                            Percent = Math.Round(Math.Max(0, ClampPercent(pct)), 1)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug("[SystemMonitor] 进程CPU采样失败: " + ex.Message);
            }
            return result;
        }

        /// <summary>
        /// WMI PerfProc Name 对同名多实例追加 #N 后缀（如 chrome#1），
        /// 去掉后缀得到干净的进程名（如 chrome）。
        /// </summary>
        private static string StripInstanceSuffix(string rawName)
        {
            int hashIdx = rawName.IndexOf('#');
            return hashIdx < 0 ? rawName : rawName.Substring(0, hashIdx);
        }

        private static double ClampPercent(double value) => value < 0 ? 0 : (value > 100 ? 100 : value);
    }
}