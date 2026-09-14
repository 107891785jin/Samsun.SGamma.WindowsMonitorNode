namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers
{
    /// <summary>CPU 采样对外只暴露统一入口，Worker 不关心内部口径选择。</summary>
    internal interface ICpuSampler
    {
        CpuSnapshot Capture(double detailThresholdPercent);
    }
}