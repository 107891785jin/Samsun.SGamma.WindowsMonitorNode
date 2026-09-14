using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 配置生效后清理 StateStore 中已失效的规则状态（修复"配置删除后幽灵异常"）。
    /// 例如：Ping IP 从配置删除后，对应 RuleState 应被移除，避免节点持续显示异常。
    /// </summary>
    internal static class MonitorStateReconciler
    {
        public static void Reconcile(MonitorStateStore store, SystemMonitorParam config)
        {
            if (store == null || config == null) return;

            var valid = BuildValidKeys(config);
            foreach (var rule in store.SnapshotStates())
            {
                // 磁盘盘符在运行时探测、不在配置里，无法预测：仅当其警告/异常整体关闭时才清理
                if (rule.RuleKey.StartsWith("DiskSpace.", StringComparison.OrdinalIgnoreCase))
                {
                    bool keep = (rule.Severity == MonitorSeverity.Warning && config.DiskSpaceWarnEnabled)
                             || (rule.Severity == MonitorSeverity.Exception && config.DiskSpaceExEnabled);
                    if (!keep) store.Remove(rule.RuleKey);
                    continue;
                }

                if (valid.Contains(rule.RuleKey)) continue;
                store.Remove(rule.RuleKey);
            }
        }

        private static IReadOnlyCollection<string> BuildValidKeys(SystemMonitorParam cfg)
        {
            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 固定键的资源：未启用则视为全部失效
            if (cfg.CpuWarnEnabled) valid.Add(MonitorRuleKeys.CpuWarning);
            if (cfg.CpuExEnabled) valid.Add(MonitorRuleKeys.CpuException);
            if (cfg.MemoryWarnEnabled) valid.Add(MonitorRuleKeys.MemoryWarning);
            if (cfg.MemoryExEnabled) valid.Add(MonitorRuleKeys.MemoryException);
            if (cfg.SgammaWarnEnabled) valid.Add(MonitorRuleKeys.SgammaWarning);
            if (cfg.SgammaExEnabled) valid.Add(MonitorRuleKeys.SgammaException);

            // 目录权限：按启用的路径列表
            if (cfg.DirPermWarnEnabled)
                foreach (var p in MonitorUtils.SplitMultiValue(cfg.DirPermWarnPaths))
                    valid.Add(MonitorRuleKeys.DirWarning(p));
            if (cfg.DirPermExEnabled)
                foreach (var p in MonitorUtils.SplitMultiValue(cfg.DirPermExPaths))
                    valid.Add(MonitorRuleKeys.DirException(p));

            // Ping：按启用的 IP 列表
            if (cfg.PingWarnEnabled)
                foreach (var ip in MonitorUtils.SplitMultiValue(cfg.PingWarnIps))
                    valid.Add(MonitorRuleKeys.PingWarning(ip));
            if (cfg.PingExEnabled)
                foreach (var ip in MonitorUtils.SplitMultiValue(cfg.PingExIps))
                    valid.Add(MonitorRuleKeys.PingException(ip));

            return valid;
        }
    }
}