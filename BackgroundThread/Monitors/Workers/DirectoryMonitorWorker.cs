using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>目录权限调度循环。</summary>
    internal sealed class DirectoryMonitorWorker : MonitorWorkerBase
    {
        private readonly DirPermSampler _sampler = new DirPermSampler();

        public DirectoryMonitorWorker(
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
                    if (!cfg.DirPermWarnEnabled && !cfg.DirPermExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    var warnPaths = MonitorUtils.SplitMultiValue(cfg.DirPermWarnPaths);
                    var exPaths = MonitorUtils.SplitMultiValue(cfg.DirPermExPaths);
                    var allPaths = warnPaths.Concat(exPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    int periodSec = MonitorUtils.MinEnabledPeriod((cfg.DirPermWarnEnabled, cfg.DirPermWarnPeriodSec), (cfg.DirPermExEnabled, cfg.DirPermExPeriodSec));
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshots = _sampler.Capture(allPaths);
                    _store.MarkResourceSample("DirPerm", DateTime.Now, true, null);
                    var map = snapshots.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);

                    DateTime now = DateTime.Now;
                    if (cfg.DirPermWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.DirPermWarnPeriodSec, now))
                    {
                        foreach (var path in warnPaths)
                            Apply(_rules.EvaluateDirectory(GetPathSnapshot(map, path), true));
                        lastWarnEval = now;
                    }
                    if (cfg.DirPermExEnabled && MonitorUtils.IsDue(lastExEval, cfg.DirPermExPeriodSec, now))
                    {
                        foreach (var path in exPaths)
                            Apply(_rules.EvaluateDirectory(GetPathSnapshot(map, path), false));
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(periodSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] 目录权限统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }
    }
}