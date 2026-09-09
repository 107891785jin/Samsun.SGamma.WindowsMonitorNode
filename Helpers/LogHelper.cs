using Samsun.SGamma.Log;
using System;
using System.Runtime.CompilerServices;

namespace Samsun.SGamma.WindowsMonitorNode.Helpers
{
    public static class LogHelper
    {
        // 日志模型名称
        private const string logModelName = "WindowsMonitorNode";

        public static void Info(string message, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Info(logModelName, message, callerMembername);
        }

        public static void Error(string message, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Error(logModelName, message, callerMembername);
        }

        public static void Error(string message, Exception ex, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Error(logModelName, message, ex, callerMembername);
        }

        public static void Warning(string message, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Warning(logModelName, message, callerMembername);
        }

        public static void Debug(string message, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Debug(logModelName, message, callerMembername);
        }

        public static void Fatal(string message, [CallerMemberName] string callerMembername = "")
        {
            SGammaLog.Fatal(logModelName, message, callerMembername);
        }
    }
}