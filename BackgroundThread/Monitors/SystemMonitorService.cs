using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 建议上层只持有此门面类。它替代原先分别创建 CpuMonitor/MemoryMonitor/... 的方式。
    /// </summary>
    public sealed class SystemMonitorService : IDisposable
    {
        private readonly SystemMonitorCoordinator _coordinator;

        public ISystemMonitorHealthGate HealthGate => _coordinator.HealthGate;

        internal MonitorStateStore StateStore => _coordinator.StateStore;

        internal CpuSnapshot LatestCpu => _coordinator.Snapshots.Cpu;
        internal MemorySnapshot LatestMemory => _coordinator.Snapshots.Memory;
        internal IReadOnlyList<DiskSpaceSnapshot> LatestDisks => _coordinator.Snapshots.Disks;
        internal IReadOnlyList<PingSnapshot> LatestPings => _coordinator.Snapshots.Pings;
        internal SgammaSnapshot LatestSgamma => _coordinator.Snapshots.Sgamma;

        public SystemMonitorService(Func<SystemMonitorParam> getConfig, MonitorBehaviorOptions behavior = null)
        {
            _coordinator = new SystemMonitorCoordinator(getConfig, behavior);
        }

        public Task StartAsync(CancellationToken token) => _coordinator.StartAsync(token);

        public void Start(CancellationToken token) => _coordinator.Start(token);

        public Task StopAsync() => _coordinator.StopAsync();

        /// <summary>配置变化后清理 StateStore 中已失效的规则状态。</summary>
        public void ReconcileConfiguration(SystemMonitorParam config) => _coordinator.ReconcileConfiguration(config);

        /// <summary>兼容"标准流程开始前 Check(out msg)"调用习惯。</summary>
        public bool Check(out string errorMessage) => HealthGate.Check(out errorMessage);

        public SystemMonitorHealthResult Check() => HealthGate.Check();

        public void Dispose() => _coordinator.Dispose();
    }
}