using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// PhysicalDisk IO 采样器。每次Capture只读取一次计数器；速率由相邻 CounterSample 差值计算。
    /// 不把 "1 D: E:" 拆成两个假的独立IO结果。
    /// </summary>
    internal sealed class DiskIoSampler : IDisposable
    {
        private sealed class CounterSet : IDisposable
        {
            public PerformanceCounter Read { get; set; }
            public PerformanceCounter Write { get; set; }
            public PerformanceCounter Transfers { get; set; }
            public PerformanceCounter AvgSecTransfer { get; set; }
            public PerformanceCounter Queue { get; set; }
            public bool HasBaseline { get; set; }
            public CounterSample ReadPrev { get; set; }
            public CounterSample WritePrev { get; set; }
            public CounterSample TransfersPrev { get; set; }
            public CounterSample AvgSecTransferPrev { get; set; }

            public void Dispose()
            {
                try { Read?.Dispose(); } catch { }
                try { Write?.Dispose(); } catch { }
                try { Transfers?.Dispose(); } catch { }
                try { AvgSecTransfer?.Dispose(); } catch { }
                try { Queue?.Dispose(); } catch { }
            }
        }

        private readonly Dictionary<string, CounterSet> _sets = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new object();

        public List<DiskIoSnapshot> Capture()
        {
            lock (_sync)
            {
                var result = new List<DiskIoSnapshot>();
                try
                {
                    var category = new PerformanceCounterCategory("PhysicalDisk");
                    var instances = category.GetInstanceNames()
                        .Where(x => !string.Equals(x, "_Total", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var valid = new HashSet<string>(instances, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in _sets.Keys.Where(x => !valid.Contains(x)).ToList())
                    {
                        _sets[stale].Dispose();
                        _sets.Remove(stale);
                    }

                    foreach (string instance in instances)
                    {
                        try
                        {
                            var set = GetOrCreate(instance);
                            CounterSample readNow = set.Read.NextSample();
                            CounterSample writeNow = set.Write.NextSample();
                            CounterSample transfersNow = set.Transfers.NextSample();
                            CounterSample avgNow = set.AvgSecTransfer.NextSample();
                            double queueNow = Math.Max(0, set.Queue.NextValue());

                            if (set.HasBaseline)
                            {
                                double readBps = SafeCalculate(set.ReadPrev, readNow);
                                double writeBps = SafeCalculate(set.WritePrev, writeNow);
                                double iops = SafeCalculate(set.TransfersPrev, transfersNow);
                                double avgSec = SafeCalculate(set.AvgSecTransferPrev, avgNow);

                                result.Add(new DiskIoSnapshot
                                {
                                    CapturedAt = DateTime.Now,
                                    IsValid = true,
                                    PhysicalDisk = instance,
                                    ReadMBps = Math.Round(Math.Max(0, readBps) / 1024d / 1024d, 2),
                                    WriteMBps = Math.Round(Math.Max(0, writeBps) / 1024d / 1024d, 2),
                                    ThroughputMBps = Math.Round(Math.Max(0, readBps + writeBps) / 1024d / 1024d, 2),
                                    IOPS = Math.Round(Math.Max(0, iops), 1),
                                    AvgResponseMs = Math.Round(Math.Max(0, avgSec) * 1000d, 2),
                                    QueueDepth = Math.Round(queueNow, 2)
                                });
                            }

                            set.ReadPrev = readNow;
                            set.WritePrev = writeNow;
                            set.TransfersPrev = transfersNow;
                            set.AvgSecTransferPrev = avgNow;
                            set.HasBaseline = true;
                        }
                        catch (Exception ex)
                        {
                            result.Add(new DiskIoSnapshot
                            {
                                CapturedAt = DateTime.Now,
                                PhysicalDisk = instance,
                                Error = ex.Message
                            });
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

        private CounterSet GetOrCreate(string instance)
        {
            if (_sets.TryGetValue(instance, out var set)) return set;
            set = new CounterSet
            {
                Read = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instance, true),
                Write = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instance, true),
                Transfers = new PerformanceCounter("PhysicalDisk", "Disk Transfers/sec", instance, true),
                AvgSecTransfer = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", instance, true),
                Queue = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", instance, true)
            };
            _sets[instance] = set;
            return set;
        }

        private static double SafeCalculate(CounterSample previous, CounterSample current)
        {
            try
            {
                double value = CounterSample.Calculate(previous, current);
                return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var set in _sets.Values) set.Dispose();
                _sets.Clear();
            }
        }
    }
}