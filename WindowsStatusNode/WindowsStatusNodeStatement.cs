using Samsun.SGamma.Language;
using Samsun.SGamma.SDK;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using Samsun.SGamma.WindowsMonitorNode.Models;
using System;

namespace Samsun.SGamma.WindowsMonitorNode.Core
{
    /// <summary>
    /// Windows 状态输出节点执行语句。
    /// 执行时从后台监控线程读取实时状态并分配到各输出变量。
    /// </summary>
    internal sealed class WindowsStatusNodeStatement : BaseStatement
    {
        private WindowsStatusNode WindowsNode { get; set; }

        public WindowsStatusNodeStatement(WindowsStatusNode elementNode) : base(elementNode)
        {
            WindowsNode = elementNode;
        }

        public override EnumValidationStatus Validate()
        {
            ValidationScope?.Define(OutputDefines, TaskId, Id);
            return EnumValidationStatus.Success;
        }

        public override ExecutionResult Run()
        {
            try
            {
                var status = WindowsMonitorBackground.GetCurrentStatus();

                Assign(0, new SwBooleanVariableValue(status.IsNormal));
                Assign(1, new SwBooleanVariableValue(status.IsNormal));
                Assign(2, new SwNumberVariableValue(status.CpuPercent));
                Assign(3, new SwNumberVariableValue(status.MemoryPercent));
                Assign(4, new SwTextVariableValue(status.DiskSummary));
                Assign(5, new SwTextVariableValue(status.PingSummary));
                Assign(6, new SwTextVariableValue(status.WarningText));
                Assign(7, new SwTextVariableValue(status.ExceptionText));
                Assign(8, new SwTextVariableValue(status.SummaryText));

                SetResultOK();
                return ExecutionResult.Success;
            }
            catch (Exception ex)
            {
                SetResultNG("执行异常:".ToLanguage() + ex.Message);
                LogHelper.Error($"节点[{WindowsNode?.Name}]执行失败", ex);
                return ExecutionResult.Error;
            }
        }
    }
}