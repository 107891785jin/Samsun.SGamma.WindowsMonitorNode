namespace Samsun.SGamma.WindowsMonitorNode.Models
{
    /// <summary>
    /// 后台监控参数在 IConfig（configManager）中持久化使用的段名与键名常量。
    /// </summary>
    public static class MonitorParamKeys
    {
        public const string Section = "WindowsMonitor";

        public const string CpuWarnEnabled = "CpuWarnEnabled";
        public const string CpuWarnPeriodSec = "CpuWarnPeriodSec";
        public const string CpuWarnUpperPercent = "CpuWarnUpperPercent";
        public const string CpuExEnabled = "CpuExEnabled";
        public const string CpuExPeriodSec = "CpuExPeriodSec";
        public const string CpuExUpperPercent = "CpuExUpperPercent";

        public const string MemoryWarnEnabled = "MemoryWarnEnabled";
        public const string MemoryWarnPeriodSec = "MemoryWarnPeriodSec";
        public const string MemoryWarnUpperPercent = "MemoryWarnUpperPercent";
        public const string MemoryExEnabled = "MemoryExEnabled";
        public const string MemoryExPeriodSec = "MemoryExPeriodSec";
        public const string MemoryExUpperPercent = "MemoryExUpperPercent";

        public const string DiskSpaceWarnEnabled = "DiskSpaceWarnEnabled";
        public const string DiskSpaceWarnPeriodSec = "DiskSpaceWarnPeriodSec";
        public const string DiskSpaceWarnLowerGB = "DiskSpaceWarnLowerGB";
        public const string DiskSpaceExEnabled = "DiskSpaceExEnabled";
        public const string DiskSpaceExPeriodSec = "DiskSpaceExPeriodSec";
        public const string DiskSpaceExLowerGB = "DiskSpaceExLowerGB";

        public const string DirPermWarnEnabled = "DirPermWarnEnabled";
        public const string DirPermWarnPeriodSec = "DirPermWarnPeriodSec";
        public const string DirPermWarnPaths = "DirPermWarnPaths";
        public const string DirPermExEnabled = "DirPermExEnabled";
        public const string DirPermExPeriodSec = "DirPermExPeriodSec";
        public const string DirPermExPaths = "DirPermExPaths";

        public const string PingWarnEnabled = "PingWarnEnabled";
        public const string PingWarnPeriodSec = "PingWarnPeriodSec";
        public const string PingWarnIps = "PingWarnIps";
        public const string PingWarnRttMs = "PingWarnRttMs";
        public const string PingWarnLossPercent = "PingWarnLossPercent";
        public const string PingExEnabled = "PingExEnabled";
        public const string PingExPeriodSec = "PingExPeriodSec";
        public const string PingExIps = "PingExIps";
        public const string PingExRttMs = "PingExRttMs";
        public const string PingExLossPercent = "PingExLossPercent";

        public const string DiskIoEnabled = "DiskIoEnabled";
        public const string DiskIoPeriodSec = "DiskIoPeriodSec";
        public const string DiskIoSampleIntervalSec = "DiskIoSampleIntervalSec";

        public const string SgammaWarnEnabled = "SgammaWarnEnabled";
        public const string SgammaWarnPeriodSec = "SgammaWarnPeriodSec";
        public const string SgammaWarnMemoryGB = "SgammaWarnMemoryGB";
        public const string SgammaWarnHandleMax = "SgammaWarnHandleMax";
        public const string SgammaWarnThreadMax = "SgammaWarnThreadMax";
        public const string SgammaWarnGdiMax = "SgammaWarnGdiMax";
        public const string SgammaExEnabled = "SgammaExEnabled";
        public const string SgammaExPeriodSec = "SgammaExPeriodSec";
        public const string SgammaExMemoryGB = "SgammaExMemoryGB";
        public const string SgammaExHandleMax = "SgammaExHandleMax";
        public const string SgammaExThreadMax = "SgammaExThreadMax";
        public const string SgammaExGdiMax = "SgammaExGdiMax";
    }
}