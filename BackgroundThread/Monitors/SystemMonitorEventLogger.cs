using System;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal sealed class SystemMonitorEventLogger
    {
        public void Write(MonitorStateChange change, string station = MonitorUtils.WinStation)
        {
            if (change == null || !change.ShouldLog || string.IsNullOrWhiteSpace(change.Message)) return;

            string logType;
            if (change.IsRecovery)
                logType = change.Severity == MonitorSeverity.Exception ? "异常恢复日志" : "警告恢复日志";
            else
                logType = change.Severity == MonitorSeverity.Exception ? "异常日志" : "警告日志";

            MonitorUtils.WriteLog(logType, change.Message, station);
        }

        public void WriteDiskIo(DiskIoSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsValid) return;
            string label = MonitorUtils.ExtractDriveLabel(snapshot.PhysicalDisk);
            string content = $"\n【{MonitorUtils.FormatNow()}】{label}（吞吐量：{snapshot.ThroughputMBps} MB/S、IOPS：{snapshot.IOPS} 次、平均响应时间：{snapshot.AvgResponseMs} ms、队列深度：{snapshot.QueueDepth}）";
            MonitorUtils.WriteLog("磁盘IO日志", content);
        }
    }
}