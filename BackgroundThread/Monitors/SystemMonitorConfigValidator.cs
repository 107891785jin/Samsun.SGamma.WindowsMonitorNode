using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using System.Collections.Generic;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal static class SystemMonitorConfigValidator
    {
        public static List<string> Validate(SystemMonitorParam cfg)
        {
            var errors = new List<string>();
            if (cfg == null)
            {
                errors.Add("SystemMonitorParam为空");
                return errors;
            }

            CheckPeriod(errors, "CPU警告", cfg.CpuWarnEnabled, cfg.CpuWarnPeriodSec);
            CheckPeriod(errors, "CPU异常", cfg.CpuExEnabled, cfg.CpuExPeriodSec);
            CheckPeriod(errors, "内存警告", cfg.MemoryWarnEnabled, cfg.MemoryWarnPeriodSec);
            CheckPeriod(errors, "内存异常", cfg.MemoryExEnabled, cfg.MemoryExPeriodSec);
            CheckPeriod(errors, "磁盘容量警告", cfg.DiskSpaceWarnEnabled, cfg.DiskSpaceWarnPeriodSec);
            CheckPeriod(errors, "磁盘容量异常", cfg.DiskSpaceExEnabled, cfg.DiskSpaceExPeriodSec);
            CheckPeriod(errors, "目录权限警告", cfg.DirPermWarnEnabled, cfg.DirPermWarnPeriodSec);
            CheckPeriod(errors, "目录权限异常", cfg.DirPermExEnabled, cfg.DirPermExPeriodSec);
            CheckPeriod(errors, "Ping警告", cfg.PingWarnEnabled, cfg.PingWarnPeriodSec);
            CheckPeriod(errors, "Ping异常", cfg.PingExEnabled, cfg.PingExPeriodSec);
            CheckPeriod(errors, "SGamma警告", cfg.SgammaWarnEnabled, cfg.SgammaWarnPeriodSec);
            CheckPeriod(errors, "SGamma异常", cfg.SgammaExEnabled, cfg.SgammaExPeriodSec);

            if (cfg.CpuWarnEnabled && cfg.CpuExEnabled && cfg.CpuWarnUpperPercent >= cfg.CpuExUpperPercent)
                errors.Add("CPU阈值配置错误：警告上限必须小于异常上限");
            if (cfg.MemoryWarnEnabled && cfg.MemoryExEnabled && cfg.MemoryWarnUpperPercent >= cfg.MemoryExUpperPercent)
                errors.Add("内存阈值配置错误：警告上限必须小于异常上限");
            if (cfg.DiskSpaceWarnEnabled && cfg.DiskSpaceExEnabled && cfg.DiskSpaceWarnLowerGB <= cfg.DiskSpaceExLowerGB)
                errors.Add("磁盘容量阈值配置错误：警告下限必须大于异常下限");
            if (cfg.PingWarnEnabled && cfg.PingExEnabled && cfg.PingWarnRttMs >= cfg.PingExRttMs)
                errors.Add("Ping RTT阈值配置错误：警告阈值必须小于异常阈值");
            if (cfg.PingWarnEnabled && cfg.PingExEnabled && cfg.PingWarnLossPercent >= cfg.PingExLossPercent)
                errors.Add("Ping丢包阈值配置错误：警告阈值必须小于异常阈值");

            if (cfg.DiskIoEnabled && cfg.DiskIoPeriodSec <= 0)
                errors.Add("磁盘IO记录周期必须大于0秒");
            if (cfg.DiskIoEnabled && cfg.DiskIoSampleIntervalSec <= 0)
                errors.Add("磁盘IO采样频率必须大于0秒");

            if (cfg.SgammaWarnEnabled && cfg.SgammaExEnabled)
            {
                if (cfg.SgammaWarnMemoryGB >= cfg.SgammaExMemoryGB) errors.Add("SGamma内存警告阈值必须小于异常阈值");
                if (cfg.SgammaWarnHandleMax >= cfg.SgammaExHandleMax) errors.Add("SGamma句柄警告阈值必须小于异常阈值");
                if (cfg.SgammaWarnThreadMax >= cfg.SgammaExThreadMax) errors.Add("SGamma线程警告阈值必须小于异常阈值");
                if (cfg.SgammaWarnGdiMax >= cfg.SgammaExGdiMax) errors.Add("SGamma GDI警告阈值必须小于异常阈值");
            }

            return errors;
        }

        private static void CheckPeriod(List<string> errors, string name, bool enabled, int periodSec)
        {
            if (enabled && periodSec <= 0) errors.Add(name + "周期必须大于0秒");
        }
    }
}