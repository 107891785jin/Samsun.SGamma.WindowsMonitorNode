using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Management;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// PhysicalDisk IO 采样器（WMI Win32_PerfFormattedData_PerfDisk_PhysicalDisk）。
    /// WMI 类名、属性名、实例名均为固定英文，规避本地化性能计数器名（"PhysicalDisk"、"Disk Read Bytes/sec" 等）。
    /// 读取即得格式化速率，无需相邻样本差值计算。
    /// 不把 "0 C: D: E: F:" 拆成多个假独立 IO 结果。
    /// </summary>
    internal sealed class DiskIoSampler : IDisposable
    {
        private readonly object _sync = new object();

        /// <summary>WMI 格式化性能类固定类名（不本地化）。</summary>
        private const string PerfDiskClass = "Win32_PerfFormattedData_PerfDisk_PhysicalDisk";

        public List<DiskIoSnapshot> Capture()
        {
            lock (_sync)
            {
                var result = new List<DiskIoSnapshot>();
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec, " +
                        "DiskTransfersPersec, AvgDiskSecPerTransfer, CurrentDiskQueueLength FROM " + PerfDiskClass);
                    using var collection = searcher.Get();
                    {
                        foreach (ManagementObject mo in collection)
                        {
                            using (mo)
                            {
                                string name = Convert.ToString(mo["Name"] ?? string.Empty);
                                if (string.IsNullOrEmpty(name) || string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                double readBps = ToDouble(mo["DiskReadBytesPersec"]);
                                double writeBps = ToDouble(mo["DiskWriteBytesPersec"]);
                                double transfers = ToDouble(mo["DiskTransfersPersec"]);
                                double avgSec = ToDouble(mo["AvgDiskSecPerTransfer"]);
                                double queue = ToDouble(mo["CurrentDiskQueueLength"]);

                                result.Add(new DiskIoSnapshot
                                {
                                    CapturedAt = DateTime.Now,
                                    IsValid = true,
                                    PhysicalDisk = name,
                                    ReadMBps = Math.Round(Math.Max(0, readBps) / 1024d / 1024d, 2),
                                    WriteMBps = Math.Round(Math.Max(0, writeBps) / 1024d / 1024d, 2),
                                    ThroughputMBps = Math.Round(Math.Max(0, readBps + writeBps) / 1024d / 1024d, 2),
                                    IOPS = Math.Round(Math.Max(0, transfers), 1),
                                    AvgResponseMs = Math.Round(Math.Max(0, avgSec) * 1000d, 2),
                                    QueueDepth = Math.Round(Math.Max(0, queue), 2)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Debug("[SystemMonitor] 磁盘IO采样失败: " + ex.Message);
                }
                return result;
            }
        }

        private static double ToDouble(object value)
        {
            try { return Convert.ToDouble(value); }
            catch { return 0; }
        }

        public void Dispose()
        {
        }
    }
}