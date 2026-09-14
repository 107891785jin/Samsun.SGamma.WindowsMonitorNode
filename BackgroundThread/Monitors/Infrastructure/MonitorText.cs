using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure
{
    /// <summary>字符串解析：多值拆分与磁盘实例盘符提取。</summary>
    internal static class MonitorText
    {
        public static List<string> SplitMultiValue(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return input
                .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>从 PhysicalDisk 实例名中提取盘符标签，例如 "0 C:" → "C盘"、"1 D: E:" → "D/E盘"。</summary>
        public static string ExtractDriveLabel(string physicalDisk)
        {
            if (string.IsNullOrWhiteSpace(physicalDisk)) return physicalDisk ?? "?";
            var letters = Regex.Matches(physicalDisk, @"([A-Za-z]):")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return letters.Count == 0 ? physicalDisk : string.Join("/", letters) + "盘";
        }
    }
}