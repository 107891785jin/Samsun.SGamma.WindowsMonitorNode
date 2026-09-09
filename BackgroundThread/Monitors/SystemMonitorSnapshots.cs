using System;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    public abstract class MonitorSnapshotBase
    {
        public bool IsValid { get; set; }
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public string Error { get; set; }
    }

    public enum CpuMetricSource
    {
        /// <summary>GetSystemTimes，CPU 忙碌时间占比（Processor Time，不含性能状态/睿频）。</summary>
        ProcessorTime,
        /// <summary>% Processor Utility，考虑处理器性能状态与 Turbo Boost。</summary>
        ProcessorUtility
    }

    public sealed class CpuSnapshot : MonitorSnapshotBase
    {
        public double UsagePercent { get; set; }
        /// <summary>系统总 CPU 的口径（Top10 进程始终为 Processor Time，不混用）。</summary>
        public CpuMetricSource MetricSource { get; set; } = CpuMetricSource.ProcessorTime;
        public List<ProcUsage> TopProcesses { get; set; } = new List<ProcUsage>();
    }

    public sealed class ProcUsage
    {
        public string Name { get; set; }
        public int ProcessId { get; set; }
        public double Percent { get; set; }
    }

    public sealed class MemorySnapshot : MonitorSnapshotBase
    {
        public double UsagePercent { get; set; }
        public double UsedGB { get; set; }
        public double TotalGB { get; set; }
        public double AvailableGB { get; set; }
        public List<ProcMemUsage> TopProcesses { get; set; } = new List<ProcMemUsage>();
    }

    public sealed class ProcMemUsage
    {
        public string Name { get; set; }
        public int ProcessId { get; set; }
        public double GB { get; set; }
    }

    public sealed class DiskSpaceSnapshot : MonitorSnapshotBase
    {
        public string Drive { get; set; }
        public double FreeGB { get; set; }
        public double TotalGB { get; set; }
    }

    public sealed class DirPermSnapshot : MonitorSnapshotBase
    {
        public string Path { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
    }

    public sealed class PingSnapshot : MonitorSnapshotBase
    {
        public string IP { get; set; }
        public int SuccessCount { get; set; }
        public int TotalCount { get; set; }
        public long AvgRttMs { get; set; }
        public long LastRttMs { get; set; }
        public double LossPercent { get; set; }
        public bool LossEvaluationReady { get; set; }
    }

    public sealed class DiskIoSnapshot : MonitorSnapshotBase
    {
        /// <summary>PhysicalDisk 实例名，例如 "0 C:" / "1 D: E:"。</summary>
        public string PhysicalDisk { get; set; }
        public double ReadMBps { get; set; }
        public double WriteMBps { get; set; }
        public double ThroughputMBps { get; set; }
        public double IOPS { get; set; }
        public double AvgResponseMs { get; set; }
        public double QueueDepth { get; set; }
    }

    public sealed class SgammaSnapshot : MonitorSnapshotBase
    {
        public double MemoryGB { get; set; }
        public int HandleCount { get; set; }
        public int ThreadCount { get; set; }
        public int GdiCount { get; set; }
    }
}