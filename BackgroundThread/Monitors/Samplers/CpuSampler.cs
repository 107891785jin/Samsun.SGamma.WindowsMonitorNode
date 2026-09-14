using System;
using System.Linq;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors;
using Samsun.SGamma.WindowsMonitorNode.Helpers;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers
{
    /// <summary>
    /// CPU 采样组合器：探测 Utility 可用性后选择总 CPU 口径（ProcessorUtility 优先，否则 ProcessorTime 兜底），
    /// 并组合进程 Top10（始终 Processor Time）。对 Worker 只暴露 ICpuSampler.Capture。
    /// </summary>
    internal sealed class CpuSampler : ICpuSampler, IDisposable
    {
        private readonly object _sync = new object();
        private readonly ProcessorTimeCpuSampler _time = new ProcessorTimeCpuSampler();
        private readonly ProcessorUtilitySampler _utility = new ProcessorUtilitySampler();
        private readonly ProcessCpuSampler _process = new ProcessCpuSampler();
        private bool _probed;
        private bool _useUtility;
        private bool _utilityDiagLogged;

        public CpuSnapshot Capture(double detailThresholdPercent)
        {
            lock (_sync)
            {
                // 首次探测：Utility 可用则采用，否则永久回退 Processor Time
                if (!_probed)
                {
                    _probed = true;
                    _useUtility = _utility.AcquireIfAvailable();
                    if (!_useUtility && !_utilityDiagLogged)
                    {
                        _utilityDiagLogged = true;
                        LogHelper.Warning("[SystemMonitor] " + _utility.Diagnose());
                    }
                }

                CpuSnapshot total = _useUtility ? _utility.Capture() : null;
                if (_useUtility && total == null)
                {
                    // Utility 运行中永久失败，切回 Processor Time
                    _useUtility = false;
                    if (!_utilityDiagLogged)
                    {
                        _utilityDiagLogged = true;
                        LogHelper.Warning("[SystemMonitor] " + _utility.Diagnose());
                    }
                    total = null;
                }
                if (total == null)
                    total = _time.Capture();

                var snapshot = new CpuSnapshot { CapturedAt = DateTime.Now };
                snapshot.UsagePercent = total.UsagePercent;
                snapshot.IsValid = total.IsValid;
                snapshot.Error = total.Error;
                snapshot.MetricSource = total.MetricSource;

                // 每轮都推进进程基线；仅在需要记录 Top10 时取值
                var all = _process.Capture();
                if (total.IsValid && total.UsagePercent >= detailThresholdPercent)
                {
                    snapshot.TopProcesses = all
                        .OrderByDescending(x => x.Percent)
                        .ThenBy(x => x.ProcessId)
                        .Take(10)
                        .ToList();
                }

                return snapshot;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _utility.Dispose();
            }
        }
    }
}