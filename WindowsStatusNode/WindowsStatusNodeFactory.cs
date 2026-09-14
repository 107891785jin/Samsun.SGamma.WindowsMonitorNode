using HandyControl.Tools;
using Samsun.SGamma.WindowsMonitorNode.Helpers;
using Samsun.SGamma.WindowsMonitorNode.Models;
using Samsun.SGamma.WindowsMonitorNode.Views;
using Samsun.SGamma.ImageControl;
using Samsun.SGamma.Language;
using Samsun.SGamma.SDK;
using System.Windows;
using System.Windows.Media;

namespace Samsun.SGamma.WindowsMonitorNode.Core
{
    /// <summary>
    /// Windows 状态输出节点工厂，负责创建节点、执行语句和属性面板
    /// </summary>
    public class WindowsStatusNodeFactory : IElementFactoryInterface
    {
        public bool CanShowToToolBar()
        {
            return true;
        }

        public IImageDraw CreateElementDraw(AElementNode elementNode)
        {
            return null;
        }

        public AElementNode CreateElementNode()
        {
            return new WindowsStatusNode();
        }

        public IElementPropertyPanel CreateElementPropertyPanel()
        {
            return new WinWindowsStatusNode();
        }

        public IStatement CreateStatement(AElementNode elementNode, Dictionary<string, ElementOutputInfo> elementOutputInfos)
        {
            if (IsElementNode(elementNode))
            {
                return new WindowsStatusNodeStatement((WindowsStatusNode)elementNode);
            }
            return null;
        }

        public string GetDisplayName()
        {
            return NodeNames.WindowsStatus.ToLanguage() + " " + NodeVersionInfo.NodeVersion;
        }

        public object GetIcon()
        {
            IconResourceHelper.EnsureLoaded();
            return ResourceHelper.GetResource<DrawingImage>("Image_WindowsStatusNode");
        }

        public string GetGroupName()
        {
            return "应用工具".ToLanguage();
        }

        public object GetGroupIcon()
        {
            return null;
        }

        public int GetIndex()
        {
            return 1;
        }

        public Type GetNodeType()
        {
            return typeof(WindowsStatusNode);
        }

        public string GetTypeName()
        {
            return WindowsStatusNode.StrNodeType;
        }

        public bool IsElementNode(AElementNode node)
        {
            return node is WindowsStatusNode;
        }

        public void ShowPropertyPanel(AElementNode elementNode)
        {
            var panel = CreateElementPropertyPanel();
            panel.ElementNode = elementNode;
            panel.ShowProperty();
        }
    }
}