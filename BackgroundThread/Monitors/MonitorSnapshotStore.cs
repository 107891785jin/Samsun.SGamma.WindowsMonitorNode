using System.Threading;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 线程安全的最新快照存储。后台 Worker 写入、前台节点线程读取。
    /// 使用 Volatile 读写保证可见性；列表以不可变数组发布，发布后不再修改。
    /// </summary>
    internal sealed class MonitorSnapshotStore
    {
        private CpuSnapshot _cpu;
        private MemorySnapshot _memory;
        private DiskSpaceSnapshot[] _disks = System.Array.Empty<DiskSpaceSnapshot>();
        private PingSnapshot[] _pings = System.Array.Empty<PingSnapshot>();
        private SgammaSnapshot _sgamma;

        public CpuSnapshot Cpu => Volatile.Read(ref _cpu);
        public MemorySnapshot Memory => Volatile.Read(ref _memory);
        public DiskSpaceSnapshot[] Disks => Volatile.Read(ref _disks);
        public PingSnapshot[] Pings => Volatile.Read(ref _pings);
        public SgammaSnapshot Sgamma => Volatile.Read(ref _sgamma);

        public void PublishCpu(CpuSnapshot value) => Volatile.Write(ref _cpu, value);
        public void PublishMemory(MemorySnapshot value) => Volatile.Write(ref _memory, value);
        public void PublishDisks(DiskSpaceSnapshot[] value) => Volatile.Write(ref _disks, value);
        public void PublishPings(PingSnapshot[] value) => Volatile.Write(ref _pings, value);
        public void PublishSgamma(SgammaSnapshot value) => Volatile.Write(ref _sgamma, value);
    }
}