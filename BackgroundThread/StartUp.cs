using Samsun.SGamma.SDK;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using Samsun.SGamma.WindowsMonitorNode.Models;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread
{
    /// <summary>
    /// Windows 后台监控线程启动入口，实现 IStartup 接口。
    /// 软件启动时创建/加载监控参数并启动后台监控线程，工程保存时把参数写回 configManager(IConfig)，关闭时停止线程。
    /// </summary>
    public class StartUp : IStartup
    {
        public string Name => "WindowsMonitorBackgroundThread";

        public StartUp()
        {
            // 从 configManager 读取后台监控参数
            WindowsMonitorBackground.LoadFrom(SdkServices.Config);

            IWorkflowDesigner workflowDesigner = SdkServices.WorkflowDesigner;
            if (workflowDesigner != null)
            {
                workflowDesigner.ProjectSaving += WorkflowDesigner_ProjectSaving;
            }
        }

        private void WorkflowDesigner_ProjectSaving()
        {
            WindowsMonitorBackground.SaveTo(SdkServices.Config);
        }

        public void Start()
        {
            WindowsMonitorBackground.Start();
        }

        public void Stop()
        {
            WindowsMonitorBackground.Stop();
        }
    }
}