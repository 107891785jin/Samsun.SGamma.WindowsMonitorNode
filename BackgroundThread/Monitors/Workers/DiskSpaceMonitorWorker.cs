using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>磁盘容量调度循环。</summary>
    internal sealed class DiskSpaceMonitorWorker : MonitorWorkerBase
    {
        private readonly DiskSpaceSampler _sampler = new DiskSpaceSampler();

        public DiskSpaceMonitorWorker(
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
                    if (!cfg.DiskSpaceWarnEnabled && !cfg.DiskSpaceExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    int periodSec = MonitorUtils.MinEnabledPeriod((cfg.DiskSpaceWarnEnabled, cfg.DiskSpaceWarnPeriodSec), (cfg.DiskSpaceExEnabled, cfg.DiskSpaceExPeriodSec));
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshots = _sampler.Capture();
                    _snapshots.PublishDisks(snapshots.ToArray());
                    bool resourceValid = snapshots.Count > 0 && snapshots.All(x => x.IsValid);
                    string resourceError = resourceValid ? null : string.Join("；", snapshots.Where(x => !x.IsValid).Select(x => x.Drive + ":" + x.Error));
                    _store.MarkResourceSample("DiskSpace", DateTime.Now, resourceValid, resourceError);

                    DateTime now = DateTime.Now;
                    if (cfg.DiskSpaceWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.DiskSpaceWarnPeriodSec, now))
                    {
                        foreach (var snapshot in snapshots)
                            if (!DiskHitsException(snapshot, cfg)) Apply(_rules.EvaluateDisk(snapshot, true, cfg));
                        lastWarnEval = now;
                    }
                    if (cfg.DiskSpaceExEnabled && MonitorUtils.IsDue(lastExEval, cfg.DiskSpaceExPeriodSec, now))
                    {
                        foreach (var snapshot in snapshots) Apply(_rules.EvaluateDisk(snapshot, false, cfg));
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(periodSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] 磁盘容量统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        private bool DiskHitsException(DiskSpaceSnapshot s, SystemMonitorParam cfg)
            => cfg.DiskSpaceExEnabled && (_store.IsActive(MonitorRuleKeys.DiskException(s.Drive)) ||
                                         (s.IsValid && s.FreeGB <= cfg.DiskSpaceExLowerGB));
    }
}