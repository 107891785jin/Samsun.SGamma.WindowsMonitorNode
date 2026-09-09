using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 建议上层只持有此门面类。它替代原先分别创建 CpuMonitor/MemoryMonitor/... 的方式。
    /// </summary>
    public sealed class SystemMonitorService : IDisposable
    {
        private readonly SystemMonitorManager _manager;

        public ISystemMonitorHealthGate HealthGate => _manager.HealthGate;

        internal MonitorStateStore StateStore => _manager.StateStore;

        internal CpuSnapshot LatestCpu => _manager.LatestCpu;
        internal MemorySnapshot LatestMemory => _manager.LatestMemory;
        internal IReadOnlyList<DiskSpaceSnapshot> LatestDisks => _manager.LatestDisks;
        internal IReadOnlyList<PingSnapshot> LatestPings => _manager.LatestPings;
        internal SgammaSnapshot LatestSgamma => _manager.LatestSgamma;

        public SystemMonitorService(Func<SystemMonitorParam> getConfig, MonitorBehaviorOptions behavior = null)
        {
            _manager = new SystemMonitorManager(getConfig, behavior);
        }

        public Task StartAsync(CancellationToken token) => _manager.StartAsync(token);

        public void Start(CancellationToken token) => _manager.Start(token);

        public Task StopAsync() => _manager.StopAsync();

        /// <summary>兼容"标准流程开始前 Check(out msg)"调用习惯。</summary>
        public bool Check(out string errorMessage) => HealthGate.Check(out errorMessage);

        public SystemMonitorHealthResult Check() => HealthGate.Check();

        public void Dispose() => _manager.Dispose();
    }
}