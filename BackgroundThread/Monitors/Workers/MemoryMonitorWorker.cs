using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>内存调度循环。</summary>
    internal sealed class MemoryMonitorWorker : MonitorWorkerBase
    {
        private readonly MemorySampler _sampler = new MemorySampler();

        public MemoryMonitorWorker(
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
            DateTime lastWarnEval = DateTime.MinValue;
            DateTime lastExEval = DateTime.MinValue;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var cfg = _getCfg();
                    if (!cfg.MemoryWarnEnabled && !cfg.MemoryExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    int periodSec = MonitorUtils.MinEnabledPeriod((cfg.MemoryWarnEnabled, cfg.MemoryWarnPeriodSec), (cfg.MemoryExEnabled, cfg.MemoryExPeriodSec));
                    double detailThreshold = MinUpperThreshold(cfg.MemoryWarnEnabled, cfg.MemoryWarnUpperPercent, cfg.MemoryExEnabled, cfg.MemoryExUpperPercent);
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshot = _sampler.Capture(detailThreshold);
                    _snapshots.PublishMemory(snapshot);
                    _store.MarkResourceSample("Memory", snapshot.CapturedAt, snapshot.IsValid, snapshot.Error);

                    DateTime now = DateTime.Now;
                    if (cfg.MemoryWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.MemoryWarnPeriodSec, now))
                    {
                        if (!MemoryHitsException(snapshot, cfg))
                            Apply(_rules.EvaluateMemory(snapshot, true, cfg));
                        lastWarnEval = now;
                    }
                    if (cfg.MemoryExEnabled && MonitorUtils.IsDue(lastExEval, cfg.MemoryExPeriodSec, now))
                    {
                        Apply(_rules.EvaluateMemory(snapshot, false, cfg));
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(periodSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] 内存统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        private bool MemoryHitsException(MemorySnapshot s, SystemMonitorParam cfg)
            => cfg.MemoryExEnabled && (_store.IsActive(MonitorRuleKeys.MemoryException) ||
                                      (s.IsValid && s.UsagePercent >= cfg.MemoryExUpperPercent));

        /// <summary>内存采样器占用非托管资源，Worker 生命周期结束时释放。</summary>
        public void DisposeResources() => _sampler.Dispose();
    }
}