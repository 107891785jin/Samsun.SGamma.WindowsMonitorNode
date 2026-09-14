using System;
using System.Collections.Generic;
using System.IO;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Infrastructure
{
    /// <summary>目录/文件读写权限探测。</summary>
    internal static class PathAccessChecker
    {
        public static (bool canRead, bool canWrite, string error) CheckPathAccess(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return (false, false, "路径为空");

            bool canRead = false;
            bool canWrite = false;
            string readError = null;
            string writeError = null;

            if (File.Exists(path))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    canRead = true;
                }
                catch (Exception ex) { readError = ex.Message; }

                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    canWrite = true;
                }
                catch (Exception ex) { writeError = ex.Message; }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                    _ = enumerator.MoveNext();
                    canRead = true;
                }
                catch (Exception ex) { readError = ex.Message; }

                string tempPath = null;
                try
                {
                    tempPath = Path.Combine(path, ".__sgamma_perm_" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    File.Delete(tempPath);
                    tempPath = null;
                    canWrite = true;
                }
                catch (Exception ex) { writeError = ex.Message; }
                finally
                {
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
            else
            {
                return (false, false, "路径不存在");
            }

            var errors = new List<string>();
            if (!canRead) errors.Add("读取失败" + (string.IsNullOrWhiteSpace(readError) ? string.Empty : ": " + readError));
            if (!canWrite) errors.Add("写入失败" + (string.IsNullOrWhiteSpace(writeError) ? string.Empty : ": " + writeError));
            return (canRead, canWrite, errors.Count == 0 ? null : string.Join("；", errors));
        }
    }
}