using System;
using System.Collections.Generic;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal sealed class DirPermSampler
    {
        public List<DirPermSnapshot> Capture(IEnumerable<string> paths)
        {
            var list = new List<DirPermSnapshot>();
            if (paths == null) return list;

            foreach (var path in paths)
            {
                var result = MonitorUtils.CheckPathAccess(path);
                list.Add(new DirPermSnapshot
                {
                    CapturedAt = DateTime.Now,
                    Path = path,
                    IsValid = true,
                    CanRead = result.canRead,
                    CanWrite = result.canWrite,
                    Error = result.error
                });
            }
            return list;
        }
    }
}