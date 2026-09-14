using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>CPU 调度循环：定秒采样系统总 CPU 与进程 Top10，并按各自周期评估警告/异常。</summary>
    internal sealed class CpuMonitorWorker : MonitorWorkerBase
    {
        private readonly CpuSampler _sampler;

        public CpuMonitorWorker(
            Func<SystemMonitorParam> getCfg,
            MonitorBehaviorOptions options,
            MonitorStateStore store,
            SystemMonitorRuleEngine rules,
            SystemMonitorEventLogger logger,
            MonitorSnapshotStore snapshots)
            : base(getCfg, options, store, rules, logger, snapshots)
        {
            _sampler = new CpuSampler();
        }

        /// <summary>释放 PerfLib 句柄等非托管资源。</summary>
        public void DisposeResources() => _sampler.Dispose();

        public override async Task RunAsync(CancellationToken token)
        {
            DateTime lastWarnEval = DateTime.MinValue;
            DateTime lastExEval = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var cfg = _getCfg();
                    if (!cfg.CpuWarnEnabled && !cfg.CpuExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    // 采样节拍固定 1 秒，保证 LatestCpu 是"秒级新鲜"的总利用率（与任务管理器口径对齐）。
                    // 规则评估（警告/异常）仍按各自 CpuWarnPeriodSec / CpuExPeriodSec 周期触发。
                    const int sampleCadenceSec = 1;
                    double detailThreshold = MonitorWorkerBase.MinUpperThreshold(cfg.CpuWarnEnabled, cfg.CpuWarnUpperPercent, cfg.CpuExEnabled, cfg.CpuExUpperPercent);
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshot = _sampler.Capture(detailThreshold);
                    _snapshots.PublishCpu(snapshot);
                    _store.MarkResourceSample("CPU", snapshot.CapturedAt, snapshot.IsValid, snapshot.Error);

                    DateTime now = DateTime.Now;
                    if (cfg.CpuWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.CpuWarnPeriodSec, now))
                    {
                        if (!(snapshot.Error == "CPU采样器预热中") && !CpuHitsException(snapshot, cfg))
                            Apply(_rules.EvaluateCpu(snapshot, true, cfg));
                        lastWarnEval = now;
                    }
                    if (cfg.CpuExEnabled && MonitorUtils.IsDue(lastExEval, cfg.CpuExPeriodSec, now))
                    {
                        if (!(snapshot.Error == "CPU采样器预热中")) Apply(_rules.EvaluateCpu(snapshot, false, cfg));
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, TimeSpan.FromSeconds(sampleCadenceSec), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] CPU统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        private bool CpuHitsException(CpuSnapshot s, SystemMonitorParam cfg)
            => cfg.CpuExEnabled && (_store.IsActive(MonitorRuleKeys.CpuException) || (s.IsValid && s.UsagePercent >= cfg.CpuExUpperPercent));
    }
}