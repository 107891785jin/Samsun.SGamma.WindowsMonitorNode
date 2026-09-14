using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>SGamma 本进程调度循环。</summary>
    internal sealed class SgammaMonitorWorker : MonitorWorkerBase
    {
        private readonly SgammaSampler _sampler = new SgammaSampler();

        public SgammaMonitorWorker(
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
                    if (!cfg.SgammaWarnEnabled && !cfg.SgammaExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    int periodSec = MonitorUtils.MinEnabledPeriod((cfg.SgammaWarnEnabled, cfg.SgammaWarnPeriodSec), (cfg.SgammaExEnabled, cfg.SgammaExPeriodSec));
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshot = _sampler.Capture();
                    _snapshots.PublishSgamma(snapshot);
                    _store.MarkResourceSample("SGamma", snapshot.CapturedAt, snapshot.IsValid, snapshot.Error);

                    DateTime now = DateTime.Now;
                    if (cfg.SgammaWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.SgammaWarnPeriodSec, now))
                    {
                        if (!SgammaHitsException(snapshot, cfg))
                            Apply(_rules.EvaluateSgamma(snapshot, true, cfg), MonitorUtils.SgammaStation);
                        lastWarnEval = now;
                    }
                    if (cfg.SgammaExEnabled && MonitorUtils.IsDue(lastExEval, cfg.SgammaExPeriodSec, now))
                    {
                        Apply(_rules.EvaluateSgamma(snapshot, false, cfg), MonitorUtils.SgammaStation);
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(periodSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] SGamma统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        private bool SgammaHitsException(SgammaSnapshot s, SystemMonitorParam cfg)
            => cfg.SgammaExEnabled && (_store.IsActive(MonitorRuleKeys.SgammaException) ||
               (s.IsValid && (s.MemoryGB > cfg.SgammaExMemoryGB || s.HandleCount > cfg.SgammaExHandleMax ||
                s.ThreadCount > cfg.SgammaExThreadMax || s.GdiCount > cfg.SgammaExGdiMax)));

        /// <summary>SGamma采样器占用非托管资源，Worker 生命周期结束时释放。</summary>
        public void DisposeResources() => _sampler.Dispose();
    }
}