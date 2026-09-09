using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    public interface ISystemMonitorHealthGate
    {
        SystemMonitorHealthResult Check();
        bool Check(out string errorMessage);
    }

    /// <summary>
    /// 标准流程启动前只依赖此接口，不直接依赖各个采样器。
    /// Exception 开启时采用 fail-closed：无状态、采样失败、采样陈旧、异常激活均阻止启动。
    /// </summary>
    internal sealed class SystemMonitorHealthGate : ISystemMonitorHealthGate
    {
        private readonly Func<SystemMonitorParam> _getCfg;
        private readonly MonitorStateStore _store;
        private readonly MonitorBehaviorOptions _options;

        public SystemMonitorHealthGate(Func<SystemMonitorParam> getCfg, MonitorStateStore store, MonitorBehaviorOptions options)
        {
            _getCfg = getCfg;
            _store = store;
            _options = options;
        }

        public bool Check(out string errorMessage)
        {
            var result = Check();
            errorMessage = result.CanStart ? null : string.Join("；", result.Errors);
            return result.CanStart;
        }

        public SystemMonitorHealthResult Check()
        {
            var cfg = _getCfg();
            var errors = new List<string>();
            errors.AddRange(SystemMonitorConfigValidator.Validate(cfg));
            if (cfg == null)
                return new SystemMonitorHealthResult { CanStart = false, Errors = errors };

            var now = DateTime.Now;

            CheckResourceFreshness(errors, "CPU", cfg.CpuExEnabled, cfg.CpuExPeriodSec, now);
            CheckResourceFreshness(errors, "Memory", cfg.MemoryExEnabled, cfg.MemoryExPeriodSec, now);
            CheckResourceFreshness(errors, "DiskSpace", cfg.DiskSpaceExEnabled, cfg.DiskSpaceExPeriodSec, now);
            CheckResourceFreshness(errors, "DirPerm", cfg.DirPermExEnabled && MonitorUtils.SplitMultiValue(cfg.DirPermExPaths).Count > 0, cfg.DirPermExPeriodSec, now);
            CheckResourceFreshness(errors, "Ping", cfg.PingExEnabled && MonitorUtils.SplitMultiValue(cfg.PingExIps).Count > 0, Math.Max(1, cfg.PingExPeriodSec), now);
            CheckResourceFreshness(errors, "SGamma", cfg.SgammaExEnabled, cfg.SgammaExPeriodSec, now);

            var states = _store.SnapshotStates();
            foreach (var state in states)
            {
                if (state.Severity != MonitorSeverity.Exception) continue;
                if (!IsRelevantExceptionRule(state.RuleKey, cfg)) continue;

                if (state.Status == MonitorRuleStatus.Active)
                    errors.Add(string.IsNullOrWhiteSpace(state.LastMessage) ? state.RuleKey + "异常" : CleanMessage(state.LastMessage));
                else if (state.Status == MonitorRuleStatus.Unknown)
                    errors.Add(state.RuleKey + "状态未知");
            }

            // 固定规则在刚启动、尚未产生 RuleState 时也必须 fail-closed。
            RequireFixedRuleState(errors, cfg.CpuExEnabled, MonitorRuleKeys.CpuException);
            RequireFixedRuleState(errors, cfg.MemoryExEnabled, MonitorRuleKeys.MemoryException);
            RequireFixedRuleState(errors, cfg.SgammaExEnabled, MonitorRuleKeys.SgammaException);

            foreach (string path in MonitorUtils.SplitMultiValue(cfg.DirPermExPaths))
                RequireFixedRuleState(errors, cfg.DirPermExEnabled, MonitorRuleKeys.DirException(path));

            foreach (string ip in MonitorUtils.SplitMultiValue(cfg.PingExIps))
                RequireFixedRuleState(errors, cfg.PingExEnabled, MonitorRuleKeys.PingException(ip));

            return new SystemMonitorHealthResult
            {
                CanStart = errors.Count == 0,
                Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private void CheckResourceFreshness(List<string> errors, string resourceKey, bool enabled, int periodSec, DateTime now)
        {
            if (!enabled) return;
            if (!_store.TryGetResourceSample(resourceKey, out var capturedAt, out var valid, out var error))
            {
                errors.Add(resourceKey + "尚无监控数据");
                return;
            }

            if (!valid)
            {
                errors.Add(resourceKey + "采样失败" + (string.IsNullOrWhiteSpace(error) ? string.Empty : "：" + error));
                return;
            }

            double maxAgeSec = Math.Max(5, Math.Max(1, periodSec) * Math.Max(1, _options.HealthSnapshotStaleMultiplier));
            if ((now - capturedAt).TotalSeconds > maxAgeSec)
                errors.Add($"{resourceKey}监控数据已过期（{Math.Round((now - capturedAt).TotalSeconds, 1)}s）");
        }

        private void RequireFixedRuleState(List<string> errors, bool enabled, string key)
        {
            if (!enabled) return;
            if (_store.GetRuleState(key) == null)
                errors.Add(key + "尚未完成首次判断");
        }

        private static bool IsRelevantExceptionRule(string key, SystemMonitorParam cfg)
        {
            if (string.Equals(key, MonitorRuleKeys.CpuException, StringComparison.OrdinalIgnoreCase)) return cfg.CpuExEnabled;
            if (string.Equals(key, MonitorRuleKeys.MemoryException, StringComparison.OrdinalIgnoreCase)) return cfg.MemoryExEnabled;
            if (string.Equals(key, MonitorRuleKeys.SgammaException, StringComparison.OrdinalIgnoreCase)) return cfg.SgammaExEnabled;

            if (key.StartsWith("DiskSpace.Exception|", StringComparison.OrdinalIgnoreCase)) return cfg.DiskSpaceExEnabled;

            if (key.StartsWith("DirPerm.Exception|", StringComparison.OrdinalIgnoreCase))
            {
                if (!cfg.DirPermExEnabled) return false;
                string path = key.Substring("DirPerm.Exception|".Length);
                return MonitorUtils.SplitMultiValue(cfg.DirPermExPaths).Contains(path, StringComparer.OrdinalIgnoreCase);
            }

            if (key.StartsWith("Ping.Exception|", StringComparison.OrdinalIgnoreCase))
            {
                if (!cfg.PingExEnabled) return false;
                string ip = key.Substring("Ping.Exception|".Length);
                return MonitorUtils.SplitMultiValue(cfg.PingExIps).Contains(ip, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string CleanMessage(string message)
        {
            return (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}