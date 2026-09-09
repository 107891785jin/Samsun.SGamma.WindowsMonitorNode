using System;
using System.Windows;

namespace Samsun.SGamma.WindowsMonitorNode.Helpers
{
    /// <summary>
    /// 图标资源加载帮助类，统一管理 Icons.xaml 资源字典的加载，避免各工厂类重复加载
    /// </summary>
    internal static class IconResourceHelper
    {
        private static readonly object _lock = new();
        private static bool _loaded;

        /// <summary>
        /// 确保 Icons.xaml 资源字典已加载到应用资源中（幂等，线程安全）
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            lock (_lock)
            {
                if (_loaded)
                    return;

                try
                {
                    var resourceDictionary = new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Samsun.SGamma.WindowsMonitorNode;component/Resources/Icons.xaml", UriKind.Absolute)
                    };
                    Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);
                    _loaded = true;
                }
                catch
                {
                    // 资源加载失败时不抛出，避免影响节点创建
                }
            }
        }
    }
}