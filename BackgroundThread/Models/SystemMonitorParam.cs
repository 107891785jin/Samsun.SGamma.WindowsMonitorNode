using Samsun.SGamma.SDK;
using Samsun.SGamma.WindowsMonitorNode.Models;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models
{
    /// <summary>
    /// 后台监控参数模型。字段与 SystemMonitorConfigValidator / SystemMonitorManager / SystemMonitorRuleEngine 的使用点一一对应。
    /// 提供基于 IConfig（configManager）的 ReadFrom/WriteTo 持久化。
    /// </summary>
    public sealed class SystemMonitorParam
    {
        // ===== CPU =====
        public bool CpuWarnEnabled { get; set; } = true;
        public int CpuWarnPeriodSec { get; set; } = 1;
        public double CpuWarnUpperPercent { get; set; } = 80;
        public bool CpuExEnabled { get; set; } = true;
        public int CpuExPeriodSec { get; set; } = 1;
        public double CpuExUpperPercent { get; set; } = 90;

        // ===== 内存 =====
        public bool MemoryWarnEnabled { get; set; } = true;
        public int MemoryWarnPeriodSec { get; set; } = 10;
        public double MemoryWarnUpperPercent { get; set; } = 85;
        public bool MemoryExEnabled { get; set; } = true;
        public int MemoryExPeriodSec { get; set; } = 10;
        public double MemoryExUpperPercent { get; set; } = 95;

        // ===== 磁盘容量 =====
        public bool DiskSpaceWarnEnabled { get; set; } = true;
        public int DiskSpaceWarnPeriodSec { get; set; } = 30;
        public double DiskSpaceWarnLowerGB { get; set; } = 10;
        public bool DiskSpaceExEnabled { get; set; } = true;
        public int DiskSpaceExPeriodSec { get; set; } = 30;
        public double DiskSpaceExLowerGB { get; set; } = 5;

        // ===== 目录权限 =====
        public bool DirPermWarnEnabled { get; set; }
        public int DirPermWarnPeriodSec { get; set; } = 30;
        public string DirPermWarnPaths { get; set; } = string.Empty;
        public bool DirPermExEnabled { get; set; }
        public int DirPermExPeriodSec { get; set; } = 30;
        public string DirPermExPaths { get; set; } = string.Empty;

        // ===== Ping =====
        public bool PingWarnEnabled { get; set; }
        public int PingWarnPeriodSec { get; set; } = 30;
        public string PingWarnIps { get; set; } = string.Empty;
        public double PingWarnRttMs { get; set; } = 200;
        public double PingWarnLossPercent { get; set; } = 5;
        public bool PingExEnabled { get; set; }
        public int PingExPeriodSec { get; set; } = 30;
        public string PingExIps { get; set; } = string.Empty;
        public double PingExRttMs { get; set; } = 500;
        public double PingExLossPercent { get; set; } = 20;

        // ===== 磁盘IO =====
        public bool DiskIoEnabled { get; set; }
        public int DiskIoPeriodSec { get; set; } = 60;
        public int DiskIoSampleIntervalSec { get; set; } = 5;

        // ===== SGamma 本进程资源 =====
        public bool SgammaWarnEnabled { get; set; }
        public int SgammaWarnPeriodSec { get; set; } = 30;
        public double SgammaWarnMemoryGB { get; set; } = 4;
        public int SgammaWarnHandleMax { get; set; } = 10000;
        public int SgammaWarnThreadMax { get; set; } = 1000;
        public int SgammaWarnGdiMax { get; set; } = 10000;
        public bool SgammaExEnabled { get; set; }
        public int SgammaExPeriodSec { get; set; } = 30;
        public double SgammaExMemoryGB { get; set; } = 8;
        public int SgammaExHandleMax { get; set; } = 20000;
        public int SgammaExThreadMax { get; set; } = 2000;
        public int SgammaExGdiMax { get; set; } = 20000;

        /// <summary>深拷贝当前配置，供界面编辑/回滚使用。</summary>
        public SystemMonitorParam Clone()
        {
            return (SystemMonitorParam)MemberwiseClone();
        }

        /// <summary>从项目配置读取并覆盖当前值（缺失的键保留现有默认值）。</summary>
        public void ReadFrom(IConfig config)
        {
            if (config == null) return;

            CpuWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnEnabled, CpuWarnEnabled);
            CpuWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnPeriodSec, CpuWarnPeriodSec);
            CpuWarnUpperPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnUpperPercent, CpuWarnUpperPercent);
            CpuExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExEnabled, CpuExEnabled);
            CpuExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExPeriodSec, CpuExPeriodSec);
            CpuExUpperPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExUpperPercent, CpuExUpperPercent);

            MemoryWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnEnabled, MemoryWarnEnabled);
            MemoryWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnPeriodSec, MemoryWarnPeriodSec);
            MemoryWarnUpperPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnUpperPercent, MemoryWarnUpperPercent);
            MemoryExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExEnabled, MemoryExEnabled);
            MemoryExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExPeriodSec, MemoryExPeriodSec);
            MemoryExUpperPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExUpperPercent, MemoryExUpperPercent);

            DiskSpaceWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnEnabled, DiskSpaceWarnEnabled);
            DiskSpaceWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnPeriodSec, DiskSpaceWarnPeriodSec);
            DiskSpaceWarnLowerGB = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnLowerGB, DiskSpaceWarnLowerGB);
            DiskSpaceExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExEnabled, DiskSpaceExEnabled);
            DiskSpaceExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExPeriodSec, DiskSpaceExPeriodSec);
            DiskSpaceExLowerGB = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExLowerGB, DiskSpaceExLowerGB);

            DirPermWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnEnabled, DirPermWarnEnabled);
            DirPermWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnPeriodSec, DirPermWarnPeriodSec);
            DirPermWarnPaths = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnPaths, DirPermWarnPaths);
            DirPermExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExEnabled, DirPermExEnabled);
            DirPermExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExPeriodSec, DirPermExPeriodSec);
            DirPermExPaths = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExPaths, DirPermExPaths);

            PingWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnEnabled, PingWarnEnabled);
            PingWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnPeriodSec, PingWarnPeriodSec);
            PingWarnIps = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnIps, PingWarnIps);
            PingWarnRttMs = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnRttMs, PingWarnRttMs);
            PingWarnLossPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnLossPercent, PingWarnLossPercent);
            PingExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExEnabled, PingExEnabled);
            PingExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExPeriodSec, PingExPeriodSec);
            PingExIps = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExIps, PingExIps);
            PingExRttMs = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExRttMs, PingExRttMs);
            PingExLossPercent = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExLossPercent, PingExLossPercent);

            DiskIoEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoEnabled, DiskIoEnabled);
            DiskIoPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoPeriodSec, DiskIoPeriodSec);
            DiskIoSampleIntervalSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoSampleIntervalSec, DiskIoSampleIntervalSec);

            SgammaWarnEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnEnabled, SgammaWarnEnabled);
            SgammaWarnPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnPeriodSec, SgammaWarnPeriodSec);
            SgammaWarnMemoryGB = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnMemoryGB, SgammaWarnMemoryGB);
            SgammaWarnHandleMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnHandleMax, SgammaWarnHandleMax);
            SgammaWarnThreadMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnThreadMax, SgammaWarnThreadMax);
            SgammaWarnGdiMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnGdiMax, SgammaWarnGdiMax);
            SgammaExEnabled = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExEnabled, SgammaExEnabled);
            SgammaExPeriodSec = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExPeriodSec, SgammaExPeriodSec);
            SgammaExMemoryGB = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExMemoryGB, SgammaExMemoryGB);
            SgammaExHandleMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExHandleMax, SgammaExHandleMax);
            SgammaExThreadMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExThreadMax, SgammaExThreadMax);
            SgammaExGdiMax = config.ReadConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExGdiMax, SgammaExGdiMax);
        }

        /// <summary>将当前参数写入项目配置。</summary>
        public void WriteTo(IConfig config)
        {
            if (config == null) return;

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnEnabled, CpuWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnPeriodSec, CpuWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuWarnUpperPercent, CpuWarnUpperPercent);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExEnabled, CpuExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExPeriodSec, CpuExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.CpuExUpperPercent, CpuExUpperPercent);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnEnabled, MemoryWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnPeriodSec, MemoryWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryWarnUpperPercent, MemoryWarnUpperPercent);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExEnabled, MemoryExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExPeriodSec, MemoryExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.MemoryExUpperPercent, MemoryExUpperPercent);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnEnabled, DiskSpaceWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnPeriodSec, DiskSpaceWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceWarnLowerGB, DiskSpaceWarnLowerGB);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExEnabled, DiskSpaceExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExPeriodSec, DiskSpaceExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskSpaceExLowerGB, DiskSpaceExLowerGB);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnEnabled, DirPermWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnPeriodSec, DirPermWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermWarnPaths, DirPermWarnPaths);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExEnabled, DirPermExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExPeriodSec, DirPermExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DirPermExPaths, DirPermExPaths);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnEnabled, PingWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnPeriodSec, PingWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnIps, PingWarnIps);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnRttMs, PingWarnRttMs);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingWarnLossPercent, PingWarnLossPercent);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExEnabled, PingExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExPeriodSec, PingExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExIps, PingExIps);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExRttMs, PingExRttMs);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.PingExLossPercent, PingExLossPercent);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoEnabled, DiskIoEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoPeriodSec, DiskIoPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.DiskIoSampleIntervalSec, DiskIoSampleIntervalSec);

            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnEnabled, SgammaWarnEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnPeriodSec, SgammaWarnPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnMemoryGB, SgammaWarnMemoryGB);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnHandleMax, SgammaWarnHandleMax);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnThreadMax, SgammaWarnThreadMax);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaWarnGdiMax, SgammaWarnGdiMax);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExEnabled, SgammaExEnabled);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExPeriodSec, SgammaExPeriodSec);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExMemoryGB, SgammaExMemoryGB);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExHandleMax, SgammaExHandleMax);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExThreadMax, SgammaExThreadMax);
            config.WriteConfig(MonitorParamKeys.Section, MonitorParamKeys.SgammaExGdiMax, SgammaExGdiMax);
        }
    }
}