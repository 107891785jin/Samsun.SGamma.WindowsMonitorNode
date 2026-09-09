using Samsun.SGamma.SDK;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread
{
    /// <summary>
    /// 前台/节点读取的实时监控状态汇总。
    /// </summary>
    public sealed class WindowsMonitorStatus
    {
        public bool IsRunning { get; internal set; }
        public bool IsNormal { get; internal set; }
        public DateTime CapturedAt { get; internal set; }
        public double CpuPercent { get; internal set; }
        public double MemoryPercent { get; internal set; }
        public string DiskSummary { get; internal set; } = string.Empty;
        public string PingSummary { get; internal set; } = string.Empty;
        public string WarningText { get; internal set; } = string.Empty;
        public string ExceptionText { get; internal set; } = string.Empty;
        public string SummaryText { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// 后台监控线程宿主（静态单例）。
    /// 负责持有全局监控参数 SystemMonitorParam、创建/启停 SystemMonitorService，并在 configManager(IConfig) 与节点之间读写参数。
    /// </summary>
    public static class WindowsMonitorBackground
    {
        private static readonly object _lock = new object();
        private static SystemMonitorParam _configModel;
        private static SystemMonitorService _service;
        private static CancellationTokenSource _cts;
        private static volatile bool _running;

        /// <summary>全局监控参数模型（懒加载默认值）。</summary>
        public static SystemMonitorParam Config
        {
            get
            {
                lock (_lock)
                {
                    return _configModel ??= new SystemMonitorParam();
                }
            }
        }

        public static bool IsRunning => _running;

        /// <summary>
        /// 校验监控参数是否合法，返回错误列表（空表示合法）。
        /// </summary>
        public static IReadOnlyList<string> ValidateConfig(SystemMonitorParam param)
        {
            return param == null
                ? new List<string> { "SystemMonitorParam为空" }
                : SystemMonitorConfigValidator.Validate(param);
        }

        /// <summary>
        /// 用界面编辑的副本覆盖全局监控参数（监控线程下次采样立即生效）。
        /// </summary>
        public static void ApplyConfig(SystemMonitorParam param)
        {
            if (param == null) return;
            lock (_lock)
            {
                _configModel = param.Clone();
            }
        }

        /// <summary>
        /// 启动后台监控线程（幂等，多开/多节点重复调用不会重复起线程）。
        /// </summary>
        public static void Start()
        {
            lock (_lock)
            {
                if (_running && _service != null) return;

                var service = _service ?? new SystemMonitorService(() => Config);
                _cts = new CancellationTokenSource();
                try
                {
                    // 直接以内部 token 运行，线程生命周期与宿主进程一致（Stop 时取消）。
                    service.Start(_cts.Token);
                    _service = service;
                    _running = true;
                    LogHelper.Info("Windows后台监控线程已启动");
                }
                catch (Exception ex)
                {
                    LogHelper.Error("Windows后台监控线程启动失败", ex);
                    try { service.Dispose(); } catch { }
                    _service = null;
                    _running = false;
                }
            }
        }

        /// <summary>
        /// 停止后台监控线程并释放资源（幂等）。
        /// </summary>
        public static void Stop()
        {
            Task drain;
            SystemMonitorService service;
            lock (_lock)
            {
                if (!_running && _service == null) return;
                try { _cts?.Cancel(); } catch { }
                _running = false;
                service = _service;
                drain = service?.StopAsync();
                _cts?.Dispose();
                _cts = null;
                _service = null;
            }

            try { drain?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            catch (Exception ex) { LogHelper.Debug("[WindowsMonitor] 停止时异常: " + ex.Message); }
            try { service?.Dispose(); } catch { }
            LogHelper.Info("Windows后台监控线程已停止");
        }

        /// <summary>
        /// 从 configManager(IConfig) 读取监控参数并应用到全局模型。
        /// </summary>
        public static void LoadFrom(IConfig config)
        {
            if (config == null) return;
            try { Config.ReadFrom(config); }
            catch (Exception ex) { LogHelper.Error("WindowsMonitor LoadFrom 配置失败", ex); }
        }

        /// <summary>
        /// 将当前监控参数写入 configManager(IConfig)。
        /// </summary>
        public static void SaveTo(IConfig config)
        {
            if (config == null) return;
            try { Config.WriteTo(config); }
            catch (Exception ex) { LogHelper.Error("WindowsMonitor SaveTo 配置失败", ex); }
        }

        /// <summary>
        /// 汇总当前实时监控状态，供节点执行时输出。
        /// </summary>
        public static WindowsMonitorStatus GetCurrentStatus()
        {
            var status = new WindowsMonitorStatus { IsRunning = _running, CapturedAt = DateTime.Now };

            lock (_lock)
            {
                var service = _service;
                if (service == null)
                {
                    status.IsNormal = true;
                    status.SummaryText = "后台监控未启动";
                    return status;
                }

                // 数值快照
                var cpu = service.LatestCpu;
                status.CpuPercent = cpu != null && cpu.IsValid ? cpu.UsagePercent : -1;

                var mem = service.LatestMemory;
                if (mem != null && mem.IsValid)
                {
                    status.MemoryPercent = mem.UsagePercent;
                }
                status.DiskSummary = BuildDiskSummary(service);
                status.PingSummary = BuildPingSummary(service);

                // 规则状态（警告/异常）
                var states = service.StateStore.SnapshotStates();
                var warnings = states
                    .Where(s => s.Severity == MonitorSeverity.Warning && s.Status == MonitorRuleStatus.Active)
                    .Select(s => Clean(s.LastMessage))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var exceptions = states
                    .Where(s => s.Severity == MonitorSeverity.Exception && s.Status == MonitorRuleStatus.Active)
                    .Select(s => Clean(s.LastMessage))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var warnList = warnings.ToList();
                var exList = exceptions.ToList();

                status.WarningText = warnList.Count > 0 ? string.Join(Environment.NewLine, warnList) : string.Empty;
                status.ExceptionText = exList.Count > 0 ? string.Join(Environment.NewLine, exList) : string.Empty;
                status.IsNormal = warnList.Count == 0 && exList.Count == 0;
                status.SummaryText = BuildSummaryText(status);
                return status;
            }
        }

        private static string BuildDiskSummary(SystemMonitorService service)
        {
            var lines = service.LatestDisks
                .Where(d => d != null)
                .Select(d => d.IsValid
                    ? $"{d.Drive}: 剩余{d.FreeGB}GB/{d.TotalGB}GB"
                    : $"{d.Drive}: 采样失败({d.Error})");
            return string.Join("；", lines);
        }

        private static string BuildPingSummary(SystemMonitorService service)
        {
            var lines = service.LatestPings
                .Where(p => p != null)
                .Select(p => p.IsValid
                    ? $"{p.IP}: RTT={Math.Max(0, p.AvgRttMs)}ms 丢包={p.LossPercent}%"
                    : $"{p.IP}: {p.Error}");
            return string.Join("；", lines);
        }

        private static string BuildSummaryText(WindowsMonitorStatus status)
        {
            var parts = new List<string>
            {
                $"状态:{(status.IsNormal ? "正常" : "异常")}",
                $"CPU:{FormatPercent(status.CpuPercent)}",
                $"内存:{FormatPercent(status.MemoryPercent)}"
            };
            if (!string.IsNullOrWhiteSpace(status.DiskSummary)) parts.Add($"磁盘:{status.DiskSummary}");
            if (!string.IsNullOrWhiteSpace(status.PingSummary)) parts.Add($"Ping:{status.PingSummary}");
            if (!string.IsNullOrWhiteSpace(status.WarningText)) parts.Add($"警告:{status.WarningText}");
            if (!string.IsNullOrWhiteSpace(status.ExceptionText)) parts.Add($"异常:{status.ExceptionText}");
            return string.Join(Environment.NewLine, parts);
        }

        private static string FormatPercent(double value)
        {
            return value < 0 ? "未知" : value.ToString("0.0") + "%";
        }

        private static string Clean(string message)
        {
            return (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}