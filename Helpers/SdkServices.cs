using Samsun.SGamma.Correspondence;
using Samsun.SGamma.SDK;

namespace Samsun.SGamma.WindowsMonitorNode.Helpers
{
    /// <summary>
    /// SDK 服务定位器：统一"首次访问时 Resolve 一次、之后复用"的缓存逻辑。
    /// </summary>
    internal static class SdkServices
    {
        private static IWorkflowDesigner _workflowDesigner;
        private static IConfig _config;
        private static ISGammaMainWin _sGammaMainWin;

        /// <summary>缓存的工作流设计器（首次访问时 Resolve，之后复用）</summary>
        public static IWorkflowDesigner WorkflowDesigner =>
            _workflowDesigner ??= ContainerBuilderHelper.Resolve<IWorkflowDesigner>();

        /// <summary>缓存的配置对象（configManager）</summary>
        public static IConfig Config =>
            _config ??= ContainerBuilderHelper.Resolve<IConfig>();

        /// <summary>缓存的主窗口</summary>
        public static ISGammaMainWin SGammaMainWin =>
            _sGammaMainWin ??= ContainerBuilderHelper.Resolve<ISGammaMainWin>();
    }
}