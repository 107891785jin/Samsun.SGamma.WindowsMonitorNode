using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>磁盘IO调度循环：按采样间隔聚集样本，到记录周期输出平均值日志。</summary>
    internal sealed class DiskIoMonitorWorker : MonitorWorkerBase
    {
        private readonly DiskIoSampler _sampler = new DiskIoSampler();

        public DiskIoMonitorWorker(
            Func<SystemMonitorParam> getCfg,
            MonitorBehaviorOptions options,
            MonitorStateStore store,
            SystemMonitorRuleEngine rules,
            SystemMonitorEventLogger logger,
            MonitorSnapshotStore snapshots)
            : base(getCfg, options, store, rules, logger, snapshots)
        {
        }

        public override async Task RunAsync(CancellationToken token)
        {
            DateTime lastLog = DateTime.MinValue;
            var aggregates = new Dictionary<string, DiskIoAggregate>(StringComparer.OrdinalIgnoreCase);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var cfg = _getCfg();
                    if (!cfg.DiskIoEnabled)
                    {
                        aggregates.Clear();
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    int sampleSec = Math.Max(1, cfg.DiskIoSampleIntervalSec);
                    int logSec = Math.Max(1, cfg.DiskIoPeriodSec);
                    int effectiveSampleSec = Math.Min(sampleSec, logSec);
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshots = _sampler.Capture();

                    foreach (var snapshot in snapshots.Where(x => x.IsValid))
                    {
                        if (!aggregates.TryGetValue(snapshot.PhysicalDisk, out var aggregate))
                        {
                            aggregate = new DiskIoAggregate(snapshot.PhysicalDisk);
                            aggregates[snapshot.PhysicalDisk] = aggregate;
                        }
                        aggregate.Add(snapshot);
                    }

                    DateTime now = DateTime.Now;
                    if (MonitorUtils.IsDue(lastLog, logSec, now) && aggregates.Count > 0)
                    {
                        foreach (var aggregate in aggregates.Values)
                            _logger.WriteDiskIo(aggregate.ToAverageSnapshot(now));
                        aggregates.Clear();
                        lastLog = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(effectiveSampleSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] 磁盘IO统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>磁盘IO采样器占用非托管资源，Worker 生命周期结束时释放。</summary>
        public void DisposeResources() => _sampler.Dispose();

        private sealed class DiskIoAggregate
        {
            private readonly string _physicalDisk;
            private int _count;
            private double _read;
            private double _write;
            private double _throughput;
            private double _iops;
            private double _response;
            private double _queue;

            public DiskIoAggregate(string physicalDisk) => _physicalDisk = physicalDisk;

            public void Add(DiskIoSnapshot s)
            {
                _count++;
                _read += s.ReadMBps;
                _write += s.WriteMBps;
                _throughput += s.ThroughputMBps;
                _iops += s.IOPS;
                _response += s.AvgResponseMs;
                _queue += s.QueueDepth;
            }

            public DiskIoSnapshot ToAverageSnapshot(DateTime now)
            {
                int n = Math.Max(1, _count);
                return new DiskIoSnapshot
                {
                    CapturedAt = now,
                    IsValid = true,
                    PhysicalDisk = _physicalDisk,
                    ReadMBps = Math.Round(_read / n, 2),
                    WriteMBps = Math.Round(_write / n, 2),
                    ThroughputMBps = Math.Round(_throughput / n, 2),
                    IOPS = Math.Round(_iops / n, 1),
                    AvgResponseMs = Math.Round(_response / n, 2),
                    QueueDepth = Math.Round(_queue / n, 2)
                };
            }
        }
    }
}