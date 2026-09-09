using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// Ping 单一采样器：每个 Probe 周期每个IP只发1包，并在内存中维护滚动窗口。
    /// 默认100个样本后，丢包率分辨率达到1%。
    /// </summary>
    internal sealed class PingRollingSampler
    {
        private readonly MonitorBehaviorOptions _options;
        private readonly ConcurrentDictionary<string, PingHistory> _histories = new(StringComparer.OrdinalIgnoreCase);

        private sealed class PingPoint
        {
            public bool Success { get; set; }
            public long RttMs { get; set; }
            public string Error { get; set; }
        }

        private sealed class PingHistory
        {
            public readonly object Sync = new object();
            public readonly Queue<PingPoint> Points = new Queue<PingPoint>();
        }

        public PingRollingSampler(MonitorBehaviorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<List<PingSnapshot>> CaptureAsync(IEnumerable<string> ips, CancellationToken token)
        {
            var targets = (ips ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tasks = targets.Select(ip => ProbeAsync(ip, token)).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            return targets.Select(BuildSnapshot).ToList();
        }

        public List<PingSnapshot> GetCurrentSnapshots(IEnumerable<string> ips)
        {
            return (ips ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildSnapshot)
                .ToList();
        }

        private async Task ProbeAsync(string ip, CancellationToken token)
        {
            var point = new PingPoint();
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, Math.Max(100, _options.PingTimeoutMs))
                    .WaitAsync(token)
                    .ConfigureAwait(false);

                point.Success = reply != null && reply.Status == IPStatus.Success;
                point.RttMs = point.Success ? reply.RoundtripTime : -1;
                if (!point.Success) point.Error = reply?.Status.ToString() ?? "NoReply";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                point.Success = false;
                point.RttMs = -1;
                point.Error = ex.Message;
            }

            var history = _histories.GetOrAdd(ip, _ => new PingHistory());
            lock (history.Sync)
            {
                history.Points.Enqueue(point);
                int max = Math.Max(1, _options.PingWindowSize);
                while (history.Points.Count > max)
                    history.Points.Dequeue();
            }
        }

        private PingSnapshot BuildSnapshot(string ip)
        {
            var snapshot = new PingSnapshot
            {
                CapturedAt = DateTime.Now,
                IP = ip,
                IsValid = true,
                AvgRttMs = -1,
                LastRttMs = -1
            };

            if (!_histories.TryGetValue(ip, out var history))
            {
                snapshot.IsValid = false;
                snapshot.Error = "尚无Ping采样";
                return snapshot;
            }

            lock (history.Sync)
            {
                var points = history.Points.ToArray();
                snapshot.TotalCount = points.Length;
                snapshot.SuccessCount = points.Count(x => x.Success);
                snapshot.LossPercent = points.Length == 0
                    ? 100
                    : Math.Round((points.Length - snapshot.SuccessCount) * 100d / points.Length, 2);

                var success = points.Where(x => x.Success).ToArray();
                if (success.Length > 0)
                    snapshot.AvgRttMs = (long)Math.Round(success.Average(x => x.RttMs));

                if (points.Length > 0)
                {
                    var last = points[points.Length - 1];
                    snapshot.LastRttMs = last.Success ? last.RttMs : -1;
                    snapshot.Error = last.Error;
                }

                snapshot.LossEvaluationReady = points.Length >= Math.Max(1, _options.PingWindowSize);
            }

            return snapshot;
        }
    }
}