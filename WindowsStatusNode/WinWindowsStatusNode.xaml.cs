using Samsun.SGamma.Common.UI.Extensions;
using Samsun.SGamma.Common.UI.Helpers;
using Samsun.SGamma.WindowsMonitorNode.Models;
using Samsun.SGamma.WindowsMonitorNode.ViewModels;
using Samsun.SGamma.SDK;
using System.Windows;
using System.Windows.Input;
using Samsun.SGamma.Language;

namespace Samsun.SGamma.WindowsMonitorNode.Views
{
    /// <summary>
    /// Windows 状态输出节点属性面板交互逻辑
    /// </summary>
    public partial class WinWindowsStatusNode : Window, IElementPropertyPanel
    {
        private WinWindowsStatusNodeVM VM { get; set; }

        private bool IsConfirmed { get; set; }

        private string OriginalNodeName { get; set; }

        private string OriginalLabel { get; set; }

        /// <summary>
        /// 当前关联的节点对象
        /// </summary>
        public AElementNode ElementNode { get; set; }

        /// <summary>
        /// 显示属性面板
        /// </summary>
        public bool ShowProperty()
        {
            Owner = ElementExtension.MainWin;
            return this.ShowDialogWithMask();
        }

        public WinWindowsStatusNode()
        {
            InitializeComponent();

            Title = NodeNames.WindowsStatus.ToLanguage() + " " + NodeVersionInfo.NodeVersion;
            WindowTitleText.Text = Title;

            VM = DataContext as WinWindowsStatusNodeVM;
            Loaded += OnLoaded;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            RestoreNodeStateIfCancelled();

            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;

            if (VM != null)
            {
                VM.CloseAction = null;
            }

            DataContext = null;
            Owner = null;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (VM == null)
            {
                return;
            }

            IsConfirmed = false;
            VM.CloseAction = Close;

            if (ElementNode is WindowsStatusNode node)
            {
                OriginalNodeName = node.Name;
                OriginalLabel = node.Label ?? string.Empty;

                VM.ToolName = node.Name;
                VM.Label = node.Label ?? string.Empty;
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void RestoreNodeStateIfCancelled()
        {
            if (IsConfirmed || ElementNode is not WindowsStatusNode node)
            {
                return;
            }

            node.Name = OriginalNodeName;
            node.Label = OriginalLabel;
        }

        /// <summary>
        /// 保存界面快照到节点
        /// </summary>
        public void SaveSnapshot(WinWindowsStatusNodeVM snapshot)
        {
            if (ElementNode is WindowsStatusNode node)
            {
                IsConfirmed = true;
                node.Name = snapshot.ToolName;
                node.Label = snapshot.Label;
            }
        }
    }
}