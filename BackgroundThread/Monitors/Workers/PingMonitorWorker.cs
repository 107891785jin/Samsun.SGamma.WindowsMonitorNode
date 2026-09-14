using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>Ping 调度循环。</summary>
    internal sealed class PingMonitorWorker : MonitorWorkerBase
    {
        private readonly PingRollingSampler _sampler;

        public PingMonitorWorker(
            Func<SystemMonitorParam> getCfg,
            MonitorBehaviorOptions options,
            MonitorStateStore store,
            SystemMonitorRuleEngine rules,
            SystemMonitorEventLogger logger,
            MonitorSnapshotStore snapshots)
            : base(getCfg, options, store, rules, logger, snapshots)
        {
            _sampler = new PingRollingSampler(options);
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
                    if (!cfg.PingWarnEnabled && !cfg.PingExEnabled)
                    {
                        await MonitorUtils.DelaySafeAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                        continue;
                    }

                    var warnIps = MonitorUtils.SplitMultiValue(cfg.PingWarnIps);
                    var exIps = MonitorUtils.SplitMultiValue(cfg.PingExIps);
                    var allIps = warnIps.Concat(exIps).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshots = await _sampler.CaptureAsync(allIps, token).ConfigureAwait(false);
                    _snapshots.PublishPings(snapshots.ToArray());
                    bool resourceValid = allIps.Count == 0 || snapshots.Count == allIps.Count;
                    _store.MarkResourceSample("Ping", DateTime.Now, resourceValid, resourceValid ? null : "Ping采样数量不完整");
                    var map = snapshots.ToDictionary(x => x.IP, StringComparer.OrdinalIgnoreCase);

                    DateTime now = DateTime.Now;
                    if (cfg.PingWarnEnabled && MonitorUtils.IsDue(lastWarnEval, cfg.PingWarnPeriodSec, now))
                    {
                        foreach (var ip in warnIps)
                        {
                            var pingSnapshot = GetPingSnapshot(map, ip);
                            if (!PingHitsException(pingSnapshot, cfg)) Apply(_rules.EvaluatePing(pingSnapshot, true, cfg));
                        }
                        lastWarnEval = now;
                    }
                    if (cfg.PingExEnabled && MonitorUtils.IsDue(lastExEval, cfg.PingExPeriodSec, now))
                    {
                        foreach (var ip in exIps) Apply(_rules.EvaluatePing(GetPingSnapshot(map, ip), false, cfg));
                        lastExEval = now;
                    }

                    await MonitorUtils.DelayRemainingAsync(cycleStartedUtc, _options.PingProbeInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    LogHelper.Error("[SystemMonitor] Ping统一采样循环异常", ex);
                    await DelayAfterFailure(token).ConfigureAwait(false);
                }
            }
        }

        private bool PingHitsException(PingSnapshot s, SystemMonitorParam cfg)
        {
            if (!cfg.PingExEnabled) return false;
            if (_store.IsActive(MonitorRuleKeys.PingException(s.IP))) return true;
            if (!s.IsValid) return false;
            bool rtt = s.AvgRttMs < 0 || s.AvgRttMs >= cfg.PingExRttMs;
            bool loss = s.LossEvaluationReady && s.LossPercent >= cfg.PingExLossPercent;
            return rtt || loss;
        }
    }
}