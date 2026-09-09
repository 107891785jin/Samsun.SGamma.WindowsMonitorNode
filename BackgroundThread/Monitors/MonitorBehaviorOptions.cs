using System;
using System.Collections.Generic;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 监控行为参数。与业务配置 SystemMonitorParam 分离，避免修改既有配置模型。
    /// </summary>
    public sealed class MonitorBehaviorOptions
    {
        /// <summary>警告连续命中次数，默认 3 次，防止瞬时抖动刷日志。</summary>
        public int WarningConsecutiveSamples { get; set; } = 3;

        /// <summary>异常连续命中次数。默认 1 次，设备启动保护优先安全。</summary>
        public int ExceptionConsecutiveSamples { get; set; } = 1;

        /// <summary>从告警/异常恢复到正常前连续健康次数。</summary>
        public int RecoveryConsecutiveSamples { get; set; } = 2;

        /// <summary>持续异常时重复提醒间隔；默认 30 秒，异常未恢复则每 30 秒补写一条。</summary>
        public TimeSpan ActiveReminderInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>CPU/内存恢复滞回，单位百分点。</summary>
        public double PercentRecoveryHysteresis { get; set; } = 5.0;

        /// <summary>磁盘空间恢复滞回，单位 GB。</summary>
        public double DiskSpaceRecoveryHysteresisGB { get; set; } = 2.0;

        /// <summary>Ping RTT 恢复阈值 = 告警阈值 × 此比例。</summary>
        public double PingRttRecoveryRatio { get; set; } = 0.85;

        /// <summary>Ping 丢包恢复滞回，单位百分点。</summary>
        public double PingLossRecoveryHysteresisPercent { get; set; } = 1.0;

        /// <summary>Ping 滚动窗口最大样本数。1% 丢包阈值建议至少 100。</summary>
        public int PingWindowSize { get; set; } = 100;

        /// <summary>Ping 采样周期。警告/异常的"周期"仍作为规则评估/记录周期。</summary>
        public TimeSpan PingProbeInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Ping 单次超时。</summary>
        public int PingTimeoutMs { get; set; } = 1000;

        /// <summary>异常 Check 允许的最大快照陈旧倍数。</summary>
        public int HealthSnapshotStaleMultiplier { get; set; } = 3;

        internal int RequiredSamples(MonitorSeverity severity)
        {
            return severity == MonitorSeverity.Warning
                ? Math.Max(1, WarningConsecutiveSamples)
                : Math.Max(1, ExceptionConsecutiveSamples);
        }
    }
}