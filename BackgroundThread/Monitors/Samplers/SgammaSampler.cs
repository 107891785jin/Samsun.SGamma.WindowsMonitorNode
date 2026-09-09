using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal sealed class SgammaSampler : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
        private const uint GR_GDIOBJECTS = 0;

        private readonly object _sync = new object();

        /// <summary>SGamma 工作集短窗口缓冲：保留最近 N 次采样，取最大值以消除瞬时内存裁剪造成的跳变。</summary>
        private readonly Queue<double> _memoryWindow = new Queue<double>();
        private const int MemorySampleWindow = 3;

        public SgammaSnapshot Capture()
        {
            lock (_sync)
            {
                var snapshot = new SgammaSnapshot { CapturedAt = DateTime.Now };
                try
                {
                    using var process = Process.GetCurrentProcess();
                    process.Refresh();

                    long memoryBytes = MonitorUtils.GetWorkingSetSizeBytes(process.Handle);
                    if (memoryBytes <= 0) memoryBytes = process.WorkingSet64;

                    // 短窗口最大值平滑：内存压力高而被临时裁剪（trim）时，读数不会瞬时暴跌。
                    double gb = memoryBytes / 1024d / 1024d / 1024d;
                    _memoryWindow.Enqueue(gb);
                    while (_memoryWindow.Count > MemorySampleWindow)
                        _memoryWindow.Dequeue();
                    gb = _memoryWindow.Max();

                    snapshot.MemoryGB = Math.Round(gb, 2);
                    snapshot.HandleCount = process.HandleCount;
                    snapshot.ThreadCount = process.Threads?.Count ?? 0;
                    snapshot.GdiCount = (int)GetGuiResources(process.Handle, GR_GDIOBJECTS);
                    snapshot.IsValid = true;
                }
                catch (Exception ex)
                {
                    snapshot.Error = ex.Message;
                    LogHelper.Debug("[SystemMonitor] SGamma资源采样失败: " + ex.Message);
                }
                return snapshot;
            }
        }

        public void Dispose()
        {
        }
    }
}