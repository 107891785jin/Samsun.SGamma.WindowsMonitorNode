using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 系统监控总调度器：每种资源只有一个采样循环；警告/异常在同一快照上按各自周期评估。
    /// </summary>
    internal sealed class SystemMonitorManager : IDisposable
    {
        private readonly Func<SystemMonitorParam> _getCfg;
        private readonly MonitorBehaviorOptions _options;
        private readonly MonitorStateStore _store;
        private readonly SystemMonitorRuleEngine _rules;
        private readonly SystemMonitorEventLogger _logger;

        private readonly PerfLibUtilitySampler _cpu = new PerfLibUtilitySampler();
        private readonly MemorySampler _memory = new MemorySampler();
        private readonly DiskSpaceSampler _diskSpace = new DiskSpaceSampler();
        private readonly DirPermSampler _dirPerm = new DirPermSampler();
        private readonly PingRollingSampler _ping;
        private readonly DiskIoSampler _diskIo = new DiskIoSampler();
        private readonly SgammaSampler _sgamma = new SgammaSampler();

        private readonly object _startSync = new object();
        private CancellationTokenSource _linkedCts;
        private Task _completion;
        private bool _disposed;
        private bool _cpuDiagnosticLogged;

        // 最新一次采样快照（引用赋值，供节点读取实时数值，非精确一致即可）
        private CpuSnapshot _latestCpu;
        private MemorySnapshot _latestMemory;
        private List<DiskSpaceSnapshot> _latestDisks = new List<DiskSpaceSnapshot>();
        private List<PingSnapshot> _latestPings = new List<PingSnapshot>();
        private SgammaSnapshot _latestSgamma;

        public ISystemMonitorHealthGate HealthGate { get; }

        public MonitorStateStore StateStore => _store;

        internal CpuSnapshot LatestCpu => _latestCpu;
        internal MemorySnapshot LatestMemory => _latestMemory;
        internal IReadOnlyList<DiskSpaceSnapshot> LatestDisks => _latestDisks;
        internal IReadOnlyList<PingSnapshot> LatestPings => _latestPings;
        internal SgammaSnapshot LatestSgamma => _latestSgamma;

        public SystemMonitorManager(Func<SystemMonitorParam> getCfg, MonitorBehaviorOptions options = null)
        {
            _getCfg = getCfg ?? throw new ArgumentNullException(nameof(getCfg));
            _options = options ?? new MonitorBehaviorOptions();
            _store = new MonitorStateStore();
            _rules = new SystemMonitorRuleEngine(_store, _options);
            _logger = new SystemMonitorEventLogger();
            _ping = new PingRollingSampler(_options);
            HealthGate = new SystemMonitorHealthGate(_getCfg, _store, _options);
        }

        public Task StartAsync(CancellationToken token)
        {
            lock (_startSync)
            {
                ThrowIfDisposed();
                if (!_cpuDiagnosticLogged)
                {
                    _cpuDiagnosticLogged = true;
                    LogHelper.Warning("[SystemMonitor] " + _cpu.Diagnose());
                }
                if (_completion != null) return _completion;

                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var ct = _linkedCts.Token;
                _completion = Task.WhenAll(
                    RunCpuAsync(ct),
                    RunMemoryAsync(ct),
                    RunDiskSpaceAsync(ct),
                    RunDirectoryAsync(ct),
                    RunPingAsync(ct),
                    RunDiskIoAsync(ct),
                    RunSgammaAsync(ct));
                return _completion;
            }
        }

        public void Start(CancellationToken token)
        {
            _ = StartAsync(token);
        }

        public async Task StopAsync()
        {
            Task completion;
            lock (_startSync)
            {
                if (_linkedCts == null) return;
                try { _linkedCts.Cancel(); } catch { }
                completion = _completion;
            }

            if (completion != null)
            {
                try { await completion.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        private async Task RunCpuAsync(CancellationToken token)
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

                    // 采样节拍固定 1 秒，保证 LatestCpu 是"秒级新鲜"的总利用率（与任务管理器口径一致）。
                    // 规则评估（警告/异常）仍按各自 CpuWarnPeriodSec / CpuExPeriodSec 周期触发。
                    const int sampleCadenceSec = 1;
                    double detailThreshold = MinUpperThreshold(cfg.CpuWarnEnabled, cfg.CpuWarnUpperPercent, cfg.CpuExEnabled, cfg.CpuExUpperPercent);
                    DateTime cycleStartedUtc = DateTime.UtcNow;
                    var snapshot = _cpu.Capture(detailThreshold);
                    _latestCpu = snapshot;
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

        private async Task RunMemoryAsync(CancellationToken token)
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
                    var snapshot = _memory.Capture(detailThreshold);
                    _latestMemory = snapshot;
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

        private async Task RunDiskSpaceAsync(CancellationToken token)
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
                    var snapshots = _diskSpace.Capture();
                    _latestDisks = snapshots;
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

        private async Task RunDirectoryAsync(CancellationToken token)
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
                    var snapshots = _dirPerm.Capture(allPaths);
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

        private async Task RunPingAsync(CancellationToken token)
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
                    var snapshots = await _ping.CaptureAsync(allIps, token).ConfigureAwait(false);
                    _latestPings = snapshots;
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

        private async Task RunDiskIoAsync(CancellationToken token)
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
                    var snapshots = _diskIo.Capture();

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

        private async Task RunSgammaAsync(CancellationToken token)
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
                    var snapshot = _sgamma.Capture();
                    _latestSgamma = snapshot;
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

            public DiskIoAggregate(string physicalDisk)
            {
                _physicalDisk = physicalDisk;
            }

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

        private void Apply(MonitorRuleResult result, string station = MonitorUtils.WinStation)
        {
            var change = _store.Apply(result, _options);
            _logger.Write(change, station);
        }


        private bool CpuHitsException(CpuSnapshot s, SystemMonitorParam cfg)
            => cfg.CpuExEnabled && (_store.IsActive(MonitorRuleKeys.CpuException) ||
                                   (s.IsValid && s.UsagePercent >= cfg.CpuExUpperPercent));

        private bool MemoryHitsException(MemorySnapshot s, SystemMonitorParam cfg)
            => cfg.MemoryExEnabled && (_store.IsActive(MonitorRuleKeys.MemoryException) ||
                                      (s.IsValid && s.UsagePercent >= cfg.MemoryExUpperPercent));

        private bool DiskHitsException(DiskSpaceSnapshot s, SystemMonitorParam cfg)
            => cfg.DiskSpaceExEnabled && (_store.IsActive(MonitorRuleKeys.DiskException(s.Drive)) ||
                                         (s.IsValid && s.FreeGB <= cfg.DiskSpaceExLowerGB));

        private bool PingHitsException(PingSnapshot s, SystemMonitorParam cfg)
        {
            if (!cfg.PingExEnabled) return false;
            if (_store.IsActive(MonitorRuleKeys.PingException(s.IP))) return true;
            if (!s.IsValid) return false;
            bool rtt = s.AvgRttMs < 0 || s.AvgRttMs >= cfg.PingExRttMs;
            bool loss = s.LossEvaluationReady && s.LossPercent >= cfg.PingExLossPercent;
            return rtt || loss;
        }

        private bool SgammaHitsException(SgammaSnapshot s, SystemMonitorParam cfg)
            => cfg.SgammaExEnabled && (_store.IsActive(MonitorRuleKeys.SgammaException) ||
               (s.IsValid && (s.MemoryGB > cfg.SgammaExMemoryGB || s.HandleCount > cfg.SgammaExHandleMax ||
                s.ThreadCount > cfg.SgammaExThreadMax || s.GdiCount > cfg.SgammaExGdiMax)));

        private static double MinUpperThreshold(bool warnEnabled, double warn, bool exEnabled, double ex)
        {
            double threshold = double.MaxValue;
            if (warnEnabled) threshold = Math.Min(threshold, warn);
            if (exEnabled) threshold = Math.Min(threshold, ex);
            return threshold == double.MaxValue ? 101 : threshold;
        }

        private static DirPermSnapshot GetPathSnapshot(Dictionary<string, DirPermSnapshot> map, string path)
        {
            if (map.TryGetValue(path, out var snapshot)) return snapshot;
            return new DirPermSnapshot { CapturedAt = DateTime.Now, Path = path, Error = "未获得路径采样结果" };
        }

        private static PingSnapshot GetPingSnapshot(Dictionary<string, PingSnapshot> map, string ip)
        {
            if (map.TryGetValue(ip, out var snapshot)) return snapshot;
            return new PingSnapshot { CapturedAt = DateTime.Now, IP = ip, Error = "尚无Ping采样结果", AvgRttMs = -1 };
        }

        private static async Task DelayAfterFailure(CancellationToken token)
        {
            try { await Task.Delay(1000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SystemMonitorManager));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _linkedCts?.Cancel(); } catch { }
            _memory.Dispose();
            _diskIo.Dispose();
            _sgamma.Dispose();
            _linkedCts?.Dispose();
        }
    }
}