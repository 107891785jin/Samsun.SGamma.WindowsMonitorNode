using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal static class MonitorRuleKeys
    {
        public const string CpuWarning = "CPU.Warning";
        public const string CpuException = "CPU.Exception";
        public const string MemoryWarning = "Memory.Warning";
        public const string MemoryException = "Memory.Exception";
        public const string SgammaWarning = "SGamma.Warning";
        public const string SgammaException = "SGamma.Exception";

        public static string DiskWarning(string drive) => "DiskSpace.Warning|" + (drive ?? "?");
        public static string DiskException(string drive) => "DiskSpace.Exception|" + (drive ?? "?");
        public static string DirWarning(string path) => "DirPerm.Warning|" + (path ?? "?");
        public static string DirException(string path) => "DirPerm.Exception|" + (path ?? "?");
        public static string PingWarning(string ip) => "Ping.Warning|" + (ip ?? "?");
        public static string PingException(string ip) => "Ping.Exception|" + (ip ?? "?");
    }

    internal sealed class SystemMonitorRuleEngine
    {
        private readonly MonitorStateStore _store;
        private readonly MonitorBehaviorOptions _options;

        public SystemMonitorRuleEngine(MonitorStateStore store, MonitorBehaviorOptions options)
        {
            _store = store;
            _options = options;
        }

        public MonitorRuleResult EvaluateCpu(CpuSnapshot s, bool warning, SystemMonitorParam cfg)
        {
            string key = warning ? MonitorRuleKeys.CpuWarning : MonitorRuleKeys.CpuException;
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "CPU", severity, s.Error);

            double threshold = warning ? cfg.CpuWarnUpperPercent : cfg.CpuExUpperPercent;
            double recovery = Math.Max(0, threshold - _options.PercentRecoveryHysteresis);
            bool active = _store.IsActive(key);
            bool violation = active ? s.UsagePercent > recovery : s.UsagePercent >= threshold;


            string level = warning ? "警告" : "异常";
            var sb = new StringBuilder($"\n【{MonitorUtils.FormatNow()}】{level}：电脑CPU超出！（{s.UsagePercent}%）");
            int rank = 1;
            foreach (var p in s.TopProcesses.Take(10))
                sb.AppendLine().Append($"\t\t 进程{rank++}：{p.Name}[PID={p.ProcessId}]（CPU：{p.Percent}%）");

            return Valid(key, "CPU", severity, violation, sb.ToString(),
                $"\n【{MonitorUtils.FormatNow()}】恢复：CPU已恢复（{s.UsagePercent}%）");
        }

        public MonitorRuleResult EvaluateMemory(MemorySnapshot s, bool warning, SystemMonitorParam cfg)
        {
            string key = warning ? MonitorRuleKeys.MemoryWarning : MonitorRuleKeys.MemoryException;
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "Memory", severity, s.Error);

            double threshold = warning ? cfg.MemoryWarnUpperPercent : cfg.MemoryExUpperPercent;
            double recovery = Math.Max(0, threshold - _options.PercentRecoveryHysteresis);
            bool active = _store.IsActive(key);
            bool violation = active ? s.UsagePercent > recovery : s.UsagePercent >= threshold;


            string level = warning ? "警告" : "异常";
            var sb = new StringBuilder($"\n【{MonitorUtils.FormatNow()}】{level}：电脑内存超出！（{s.UsagePercent}%）");
            int rank = 1;
            foreach (var p in s.TopProcesses.Take(10))
                sb.AppendLine().Append($"\t\t 进程{rank++}：{p.Name}[PID={p.ProcessId}]（内存：{p.GB} GB）");

            return Valid(key, "Memory", severity, violation, sb.ToString(),
                $"\n【{MonitorUtils.FormatNow()}】恢复：内存已恢复（{s.UsagePercent}%）");
        }

        public MonitorRuleResult EvaluateDisk(DiskSpaceSnapshot s, bool warning, SystemMonitorParam cfg)
        {
            string key = warning ? MonitorRuleKeys.DiskWarning(s.Drive) : MonitorRuleKeys.DiskException(s.Drive);
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "DiskSpace", severity, $"{s.Drive}：{s.Error}");

            double threshold = warning ? cfg.DiskSpaceWarnLowerGB : cfg.DiskSpaceExLowerGB;
            double recovery = threshold + Math.Max(0, _options.DiskSpaceRecoveryHysteresisGB);
            bool active = _store.IsActive(key);
            bool violation = active ? s.FreeGB < recovery : s.FreeGB <= threshold;


            string level = warning ? "警告" : "异常";
            return Valid(key, "DiskSpace", severity, violation,
                $"\n【{MonitorUtils.FormatNow()}】{level}：电脑{s.Drive}盘容量过低！（{s.FreeGB} GB）",
                $"\n【{MonitorUtils.FormatNow()}】恢复：电脑{s.Drive}盘容量已恢复（{s.FreeGB} GB）");
        }

        public MonitorRuleResult EvaluateDirectory(DirPermSnapshot s, bool warning)
        {
            string key = warning ? MonitorRuleKeys.DirWarning(s.Path) : MonitorRuleKeys.DirException(s.Path);
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "DirPerm", severity, $"{s.Path}：{s.Error}");

            bool violation = !s.CanRead || !s.CanWrite;
            string level = warning ? "警告" : "异常";
            return Valid(key, "DirPerm", severity, violation,
                $"\n【{MonitorUtils.FormatNow()}】{level}：目录权限访问失败！（{s.Path}）",
                $"\n【{MonitorUtils.FormatNow()}】恢复：目录读写权限已恢复（{s.Path}）");
        }

        public MonitorRuleResult EvaluatePing(PingSnapshot s, bool warning, SystemMonitorParam cfg)
        {
            string key = warning ? MonitorRuleKeys.PingWarning(s.IP) : MonitorRuleKeys.PingException(s.IP);
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "Ping", severity, $"{s.IP}：{s.Error}");

            double rttThreshold = warning ? cfg.PingWarnRttMs : cfg.PingExRttMs;
            double lossThreshold = warning ? cfg.PingWarnLossPercent : cfg.PingExLossPercent;
            bool active = _store.IsActive(key);

            double rttRecovery = Math.Max(0, rttThreshold * Math.Clamp(_options.PingRttRecoveryRatio, 0, 1));
            double lossRecovery = Math.Max(0, lossThreshold - Math.Max(0, _options.PingLossRecoveryHysteresisPercent));

            bool rttFail = s.AvgRttMs < 0 || (active ? s.AvgRttMs > rttRecovery : s.AvgRttMs >= rttThreshold);
            bool lossFail = s.LossEvaluationReady && (active ? s.LossPercent > lossRecovery : s.LossPercent >= lossThreshold);
            bool violation = rttFail || lossFail;


            string level = warning ? "警告" : "异常";
            string warm = s.LossEvaluationReady ? string.Empty : $"（丢包窗口预热 {s.TotalCount}/{Math.Max(1, _options.PingWindowSize)}）";
            return Valid(key, "Ping", severity, violation,
                $"\n【{MonitorUtils.FormatNow()}】{level}：Ping失败！（{s.IP}）（RTT往返时间：{Math.Max(0, s.AvgRttMs)} ms、丢包率：{s.LossPercent} %）{warm}",
                $"\n【{MonitorUtils.FormatNow()}】恢复：Ping已恢复（{s.IP}）（RTT往返时间：{Math.Max(0, s.AvgRttMs)} ms、丢包率：{s.LossPercent} %）");
        }

        public MonitorRuleResult EvaluateSgamma(SgammaSnapshot s, bool warning, SystemMonitorParam cfg)
        {
            string key = warning ? MonitorRuleKeys.SgammaWarning : MonitorRuleKeys.SgammaException;
            var severity = warning ? MonitorSeverity.Warning : MonitorSeverity.Exception;
            if (!s.IsValid) return Invalid(key, "SGamma", severity, s.Error);

            double mem = warning ? cfg.SgammaWarnMemoryGB : cfg.SgammaExMemoryGB;
            int handles = warning ? cfg.SgammaWarnHandleMax : cfg.SgammaExHandleMax;
            int threads = warning ? cfg.SgammaWarnThreadMax : cfg.SgammaExThreadMax;
            int gdi = warning ? cfg.SgammaWarnGdiMax : cfg.SgammaExGdiMax;

            bool violation = s.MemoryGB > mem || s.HandleCount > handles || s.ThreadCount > threads || s.GdiCount > gdi;

            string level = warning ? "警告" : "异常";
            return Valid(key, "SGamma", severity, violation,
                $"\n【{MonitorUtils.FormatNow()}】{level}：SGamma资源异常（内存：{s.MemoryGB}GB、句柄：{s.HandleCount}、线程：{s.ThreadCount}、GDI：{s.GdiCount}）",
                $"\n【{MonitorUtils.FormatNow()}】恢复：SGamma资源已恢复（内存：{s.MemoryGB}GB、句柄：{s.HandleCount}、线程：{s.ThreadCount}、GDI：{s.GdiCount}）");
        }

        private static MonitorRuleResult Invalid(string key, string resource, MonitorSeverity severity, string error)
        {
            return new MonitorRuleResult
            {
                RuleKey = key,
                ResourceKey = resource,
                Severity = severity,
                IsValid = false,
                Message = $"\n【{MonitorUtils.FormatNow()}】{(severity == MonitorSeverity.Exception ? "异常" : "警告")}：{resource}采样失败/状态未知！{(string.IsNullOrWhiteSpace(error) ? string.Empty : " 原因：" + error)}"
            };
        }

        private static MonitorRuleResult Valid(string key, string resource, MonitorSeverity severity, bool violation, string message, string recovery)
        {
            return new MonitorRuleResult
            {
                RuleKey = key,
                ResourceKey = resource,
                Severity = severity,
                IsValid = true,
                IsViolation = violation,
                Message = message,
                RecoveryMessage = recovery
            };
        }
    }
}