using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers
{
    /// <summary>
    /// Worker 公共基类：持有配置/状态/规则/日志/快照等共享依赖，并提供通用的调度辅助方法。
    /// 具体资源循环由子类实现 RunAsync。
    /// </summary>
    internal abstract class MonitorWorkerBase : IMonitorWorker
    {
        protected readonly Func<SystemMonitorParam> _getCfg;
        protected readonly MonitorBehaviorOptions _options;
        protected readonly MonitorStateStore _store;
        protected readonly SystemMonitorRuleEngine _rules;
        protected readonly SystemMonitorEventLogger _logger;
        protected readonly MonitorSnapshotStore _snapshots;

        protected MonitorWorkerBase(
            Func<SystemMonitorParam> getCfg,
            MonitorBehaviorOptions options,
            MonitorStateStore store,
            SystemMonitorRuleEngine rules,
            SystemMonitorEventLogger logger,
            MonitorSnapshotStore snapshots)
        {
            _getCfg = getCfg;
            _options = options;
            _store = store;
            _rules = rules;
            _logger = logger;
            _snapshots = snapshots;
        }

        public abstract Task RunAsync(CancellationToken token);

        protected void Apply(MonitorRuleResult result, string station = MonitorUtils.WinStation)
        {
            var change = _store.Apply(result, _options);
            _logger.Write(change, station);
        }

        protected static double MinUpperThreshold(bool warnEnabled, double warn, bool exEnabled, double ex)
        {
            double threshold = double.MaxValue;
            if (warnEnabled) threshold = Math.Min(threshold, warn);
            if (exEnabled) threshold = Math.Min(threshold, ex);
            return threshold == double.MaxValue ? 101 : threshold;
        }

        protected static DirPermSnapshot GetPathSnapshot(Dictionary<string, DirPermSnapshot> map, string path)
        {
            if (map.TryGetValue(path, out var snapshot)) return snapshot;
            return new DirPermSnapshot { CapturedAt = DateTime.Now, Path = path, Error = "未获得路径采样结果" };
        }

        protected static PingSnapshot GetPingSnapshot(Dictionary<string, PingSnapshot> map, string ip)
        {
            if (map.TryGetValue(ip, out var snapshot)) return snapshot;
            return new PingSnapshot { CapturedAt = DateTime.Now, IP = ip, Error = "尚无Ping采样结果", AvgRttMs = -1 };
        }

        protected static async Task DelayAfterFailure(CancellationToken token)
        {
            try { await Task.Delay(1000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }
}