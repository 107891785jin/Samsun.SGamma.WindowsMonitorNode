using Samsun.SGamma.Language;
using Samsun.SGamma.SDK;
using Samsun.SGamma.WindowsMonitorNode.Models;

namespace Samsun.SGamma.WindowsMonitorNode.Models
{
    /// <summary>
    /// Windows 状态输出节点定义。
    /// 读取后台监控线程的实时状态并输出到多个输出变量（CPU/内存/磁盘/Ping/警告/异常/汇总）。
    /// </summary>
    public class WindowsStatusNode : AElementNode
    {
        /// <summary>节点类型标识</summary>
        public const string StrNodeType = "WindowsStatusNode";

        public WindowsStatusNode()
        {
            NodeType = StrNodeType;
            DefaultName = NodeNames.WindowsStatus.ToLanguage();
            this.Name = DefaultName;

            this.OutputDefines = new VariableDefine[]
            {
                new VariableDefine("总状态".ToLanguage(), EnumVariableType.Boolean),
                new VariableDefine("是否正常".ToLanguage(), EnumVariableType.Boolean),
                new VariableDefine("CPU使用率".ToLanguage(), EnumVariableType.Number),
                new VariableDefine("内存使用率".ToLanguage(), EnumVariableType.Number),
                new VariableDefine("磁盘剩余".ToLanguage(), EnumVariableType.Text),
                new VariableDefine("Ping状态".ToLanguage(), EnumVariableType.Text),
                new VariableDefine("警告内容".ToLanguage(), EnumVariableType.Text),
                new VariableDefine("异常内容".ToLanguage(), EnumVariableType.Text),
                new VariableDefine("状态汇总".ToLanguage(), EnumVariableType.Text)
            };
        }
    }
}