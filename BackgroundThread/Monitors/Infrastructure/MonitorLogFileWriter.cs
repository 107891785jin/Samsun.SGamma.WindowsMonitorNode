using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.IO;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 监控日志文件写出：LogRootDirectory\日期\小时\ {站点} 日志类型 HH时.txt。
    /// 全局锁串行化同文件追加写，避免并发写导致的行交错。
    /// </summary>
    internal static class MonitorLogFileWriter
    {
        /// <summary>监控日志根目录（可注入，默认兼容原写死路径）。</summary>
        public static string LogRootDirectory { get; set; } = @"D:\SAMSUN-Log\SGamma标准日志";

        private static readonly object _logFileSync = new object();

        public static void WriteLog(string logType, string content, string station = MonitorUtils.WinStation)
        {
            try
            {
                DateTime now = DateTime.Now;
                string dateDir = now.ToString("yyyy年MM月dd日");
                string hourDir = now.ToString("yyyy年MM月dd日HH时");
                string hour = now.ToString("HH");
                string dir = Path.Combine(LogRootDirectory, dateDir, hourDir);
                Directory.CreateDirectory(dir);

                string fileName = $"{{{station}}} {logType} {hour}时.txt";
                string filePath = Path.Combine(dir, fileName);

                string line = (content ?? string.Empty).TrimStart('\r', '\n');

                lock (_logFileSync)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug("[SystemMonitor] 写入日志失败: " + ex.Message);
            }
        }
    }
}