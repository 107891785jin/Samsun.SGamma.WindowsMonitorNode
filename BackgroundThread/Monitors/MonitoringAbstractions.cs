using System;
using System.Collections.Generic;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal enum MonitorSeverity
    {
        Warning = 1,
        Exception = 2,
        Information = 3
    }

    internal enum MonitorRuleStatus
    {
        Unknown = 0,
        Normal = 1,
        Active = 2
    }

    internal sealed class MonitorRuleResult
    {
        public string RuleKey { get; set; }
        public string ResourceKey { get; set; }
        public MonitorSeverity Severity { get; set; }
        public bool IsValid { get; set; }
        public bool IsViolation { get; set; }
        public DateTime EvaluatedAt { get; set; } = DateTime.Now;
        public string Message { get; set; }
        public string RecoveryMessage { get; set; }
    }

    internal sealed class MonitorRuleState
    {
        public string RuleKey { get; set; }
        public string ResourceKey { get; set; }
        public MonitorSeverity Severity { get; set; }
        public MonitorRuleStatus Status { get; set; } = MonitorRuleStatus.Unknown;
        public int ConsecutiveViolations { get; set; }
        public int ConsecutiveHealthy { get; set; }
        public DateTime LastEvaluatedAt { get; set; } = DateTime.MinValue;
        public DateTime LastChangedAt { get; set; } = DateTime.MinValue;
        public DateTime LastLoggedAt { get; set; } = DateTime.MinValue;
        public string LastMessage { get; set; }
    }

    internal sealed class MonitorStateChange
    {
        public bool ShouldLog { get; set; }
        public bool IsRecovery { get; set; }
        public MonitorSeverity Severity { get; set; }
        public string RuleKey { get; set; }
        public string Message { get; set; }
    }

    public sealed class SystemMonitorHealthResult
    {
        public bool CanStart { get; internal set; }
        public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();

        public override string ToString()
        {
            return CanStart ? "OK" : string.Join("；", Errors);
        }
    }
}