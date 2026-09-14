using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Workers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 系统监控协调器（替代原 SystemMonitorManager）。
    /// 本身不含资源业务逻辑，只负责创建 Worker、启动/停止、等待、释放。
    /// </summary>
    internal sealed class SystemMonitorCoordinator : IDisposable
    {
        private readonly Func<SystemMonitorParam> _getCfg;
        private readonly MonitorBehaviorOptions _options;
        private readonly MonitorStateStore _store = new MonitorStateStore();
        private readonly MonitorSnapshotStore _snapshots = new MonitorSnapshotStore();
        private readonly SystemMonitorRuleEngine _rules;
        private readonly SystemMonitorEventLogger _logger = new SystemMonitorEventLogger();
        private readonly IReadOnlyList<IMonitorWorker> _workers;

        private readonly object _startSync = new object();
        private CancellationTokenSource _linkedCts;
        private Task _completion;
        private bool _disposed;

        public ISystemMonitorHealthGate HealthGate { get; }
        public MonitorStateStore StateStore => _store;
        public MonitorSnapshotStore Snapshots => _snapshots;

        internal SystemMonitorCoordinator(Func<SystemMonitorParam> getCfg, MonitorBehaviorOptions options)
        {
            _getCfg = getCfg ?? throw new ArgumentNullException(nameof(getCfg));
            _options = options ?? new MonitorBehaviorOptions();
            _rules = new SystemMonitorRuleEngine(_store, _options);
            if (!string.IsNullOrWhiteSpace(_options.LogRootDirectory))
                MonitorLogFileWriter.LogRootDirectory = _options.LogRootDirectory;

            MonitorSnapshotStore snap = _snapshots;
            _workers = new IMonitorWorker[]
            {
                new CpuMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new MemoryMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new DiskSpaceMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new DirectoryMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new PingMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new DiskIoMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
                new SgammaMonitorWorker(getCfg, _options, _store, _rules, _logger, snap),
            };
            HealthGate = new SystemMonitorHealthGate(getCfg, _store, _options);
        }

        public Task StartAsync(CancellationToken token)
        {
            lock (_startSync)
            {
                ThrowIfDisposed();
                if (_completion != null) return _completion;

                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _completion = Task.WhenAll(_workers.Select(w => w.RunAsync(_linkedCts.Token)));
                return _completion;
            }
        }

        public void Start(CancellationToken token) => _ = StartAsync(token);

        public async Task StopAsync()
        {
            Task completion;
            lock (_startSync)
            {
                if (_linkedCts == null) return;
                try { _linkedCts.Cancel(); } catch { }
                completion = _completion;
            }

            if (completion != null)
            {
                try { await completion.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>
        /// 配置变化后清理已失效的规则状态（幽灵异常修复）。
        /// </summary>
        public void ReconcileConfiguration(SystemMonitorParam config)
        {
            if (config == null) return;
            MonitorStateReconciler.Reconcile(_store, config);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SystemMonitorCoordinator));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _linkedCts?.Cancel(); } catch { }

            // 释放持有非托管资源的采样器
            foreach (var w in _workers)
            {
                try
                {
                    (w as CpuMonitorWorker)?.DisposeResources();
                    (w as MemoryMonitorWorker)?.DisposeResources();
                    (w as DiskIoMonitorWorker)?.DisposeResources();
                    (w as SgammaMonitorWorker)?.DisposeResources();
                }
                catch { }
            }
            _linkedCts?.Dispose();
        }
    }
}