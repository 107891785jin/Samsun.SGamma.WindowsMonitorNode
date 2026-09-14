using System.Threading;
using System.Threading.Tasks;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>单一资源的调度循环。Sampler 取事实，Worker 做调度。</summary>
    internal interface IMonitorWorker
    {
        Task RunAsync(CancellationToken token);
    }
}