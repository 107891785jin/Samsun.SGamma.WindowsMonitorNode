using Samsun.SGamma.WindowsMonitorNode.Helpers;
using System;
using System.Collections.Generic;
using System.IO;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    internal sealed class DiskSpaceSampler
    {
        public List<DiskSpaceSnapshot> Capture()
        {
            var list = new List<DiskSpaceSnapshot>();
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.DriveType != DriveType.Fixed) continue;

                        var snapshot = new DiskSpaceSnapshot
                        {
                            CapturedAt = DateTime.Now,
                            Drive = (drive.Name ?? "?").TrimEnd('\\').ToUpperInvariant()
                        };

                        if (!drive.IsReady)
                        {
                            snapshot.Error = "固定磁盘未就绪";
                            list.Add(snapshot);
                            continue;
                        }

                        snapshot.FreeGB = Math.Round(drive.AvailableFreeSpace / 1024d / 1024d / 1024d, 2);
                        snapshot.TotalGB = Math.Round(drive.TotalSize / 1024d / 1024d / 1024d, 2);
                        snapshot.IsValid = true;
                        list.Add(snapshot);
                    }
                    catch (Exception ex)
                    {
                        list.Add(new DiskSpaceSnapshot
                        {
                            CapturedAt = DateTime.Now,
                            Drive = drive.Name,
                            Error = ex.Message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug("[SystemMonitor] 磁盘容量采样失败: " + ex.Message);
            }
            return list;
        }
    }
}