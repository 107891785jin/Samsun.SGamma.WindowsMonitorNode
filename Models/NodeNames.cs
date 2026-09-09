namespace Samsun.SGamma.WindowsMonitorNode.Models
{
    /// <summary>
    /// Windows 状态输出节点默认名称常量，避免硬编码字符串。
    /// 赋值时仍需要 .ToLanguage() 进行本地化。
    /// </summary>
    public static class NodeNames
    {
        /// <summary>界面显示名（窗口标题、工厂工具名）</summary>
        public const string WindowsStatus = "Windows状态输出";
    }
}