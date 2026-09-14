using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure
{
    /// <summary>采样/评估周期与延迟相关的公共工具。</summary>
    internal static class MonitorTiming
    {
        public static string FormatNow() => DateTime.Now.ToString("HH时mm分ss秒fff毫秒");

        public static int MinEnabledPeriod(params (bool enabled, int seconds)[] values)
        {
            var periods = values.Where(x => x.enabled).Select(x => Math.Max(1, x.seconds)).ToArray();
            return periods.Length == 0 ? 1 : periods.Min();
        }

        public static bool IsDue(DateTime lastRun, int periodSec, DateTime now)
        {
            if (lastRun == DateTime.MinValue) return true;
            return now - lastRun >= TimeSpan.FromSeconds(Math.Max(1, periodSec));
        }

        public static async Task DelaySafeAsync(TimeSpan delay, CancellationToken token)
        {
            if (delay < TimeSpan.FromMilliseconds(50)) delay = TimeSpan.FromMilliseconds(50);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        public static async Task DelayRemainingAsync(DateTime cycleStartedUtc, TimeSpan targetPeriod, CancellationToken token)
        {
            var remaining = targetPeriod - (DateTime.UtcNow - cycleStartedUtc);
            if (remaining <= TimeSpan.Zero) remaining = TimeSpan.FromMilliseconds(10);
            await Task.Delay(remaining, token).ConfigureAwait(false);
        }
    }
}