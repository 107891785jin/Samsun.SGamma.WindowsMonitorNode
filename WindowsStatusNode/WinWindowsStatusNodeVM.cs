using CommunityToolkit.Mvvm.Input;
using Samsun.SGamma.Common.UI.Extensions;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread;
using Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Models;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using Samsun.SGamma.WindowsMonitorNode.Views;
using Samsun.SGamma.FlowTools.Base;
using Samsun.SGamma.Language;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace Samsun.SGamma.WindowsMonitorNode.ViewModels
{
    /// <summary>
    /// Windows 状态输出节点视图模型。
    /// 编辑后台监控参数（SystemMonitorParam），通过 configManager(IConfig) 读写。
    /// </summary>
    public class WinWindowsStatusNodeVM : DataModelParaBase
    {
        /// <summary>关闭窗口动作</summary>
        public Action CloseAction { get; set; }

        public IRelayCommand UpdateCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand ConfirmCommand { get; }
        public IRelayCommand CloseCommand { get; }

        private string toolName = string.Empty;
        public string ToolName { get => toolName; set => Set(ref toolName, value); }

        private string label = string.Empty;
        public string Label { get => label; set => Set(ref label, value); }

        /// <summary>
        /// 正在编辑的监控参数副本（来自全局模型克隆，确认时回写）。
        /// </summary>
        public SystemMonitorParam Param { get; }

        public WinWindowsStatusNodeVM()
        {
            Param = WindowsMonitorBackground.Config.Clone();

            UpdateCommand = new RelayCommand(Update);
            SaveCommand = new RelayCommand(Save);
            ConfirmCommand = new RelayCommand(Confirm);
            CloseCommand = new RelayCommand(() => CloseAction?.Invoke());
        }

        /// <summary>更新：应用校验通过的参数立即生效并写入配置。</summary>
        private void Update()
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                TipExtension.Error(string.Join("；", errors));
                return;
            }

            WindowsMonitorBackground.ApplyConfig(Param);
            WindowsMonitorBackground.SaveTo(SdkServices.Config);
            TipExtension.Success("监控参数已更新".ToLanguage());
        }

        /// <summary>保存：仅持久化到配置，不立即触发监控应用。</summary>
        private void Save()
        {
            WindowsMonitorBackground.SaveTo(SdkServices.Config);
            TipExtension.Success("监控参数已保存".ToLanguage());
        }

        /// <summary>确认：应用参数、写入配置并关闭窗口。</summary>
        private void Confirm()
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                TipExtension.Error(string.Join("；", errors));
                return;
            }

            ApplyNodeInfo();
            WindowsMonitorBackground.ApplyConfig(Param);
            WindowsMonitorBackground.SaveTo(SdkServices.Config);
            CloseAction?.Invoke();
        }

        private List<string> Validate()
        {
            return WindowsMonitorBackground.ValidateConfig(Param).ToList();
        }

        private void ApplyNodeInfo()
        {
            if (Application.Current.Windows.Count > 0)
            {
                foreach (var window in Application.Current.Windows)
                {
                    if (window is WinWindowsStatusNode win && win.DataContext == this)
                    {
                        win.SaveSnapshot(this);
                        break;
                    }
                }
            }
        }
    }
}