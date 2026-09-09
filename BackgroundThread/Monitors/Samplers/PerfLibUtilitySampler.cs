using System;
using System.Runtime.InteropServices;
using Samsun.SGamma.WindowsMonitorNode.Helpers;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// CPU 总利用率采样器，优先通过 PerfLib V2 Consumer API 读取 % Processor Utility，
    /// 与新版任务管理器"性能→CPU"口径一致；失败则自动回退到 GetSystemTimes(Processor Time)。
    ///
    /// 注意：PerfLib V2 Consumer API 位于 advapi32.dll（perflib.h 仅是头文件名，不是 DLL 名）。
    ///
    /// 目标 Counter Set GUID: {B4FC721A-0378-476F-89BA-A5A79F810B36} (Processor Information V2)
    /// Counter ID: 26 = % Processor Utility (PERF_AVERAGE_BULK), 27 = % Utility Base (其 base)
    /// 计算（PERF_AVERAGE_BULK 标准）: Utility = (Δ26 / Δ27)，provider 已按百分比缩放，不要额外 ×100。
    /// </summary>
    internal sealed class PerfLibUtilitySampler
    {
        private const string PerfApiDll = "advapi32.dll";
        private const int ERROR_SUCCESS = 0;

        internal const int CounterId_ProcessorUtility = 26;
        internal const int CounterId_UtilityBase = 27;
        internal static readonly Guid ProcessorInformationGuid = new Guid("B4FC721A-0378-476F-89BA-A5A79F810B36");

        private enum InitState
        {
            NotAttempted,
            ApiFailed,     // advapi32 加载/PInvoke 调用本身失败
            GuidNotFound,  // PerfLib V2 枚举正常，但未注册 Processor Information V2 Counter Set
            CounterUnavailable, // Counter Set 存在，但 % Processor Utility/Base 不可用
            Ready          // 可用，系统总CPU采用 % Processor Utility
        }

        #region P/Invoke（PerfLib V2 Consumer API，advapi32.dll）

        [StructLayout(LayoutKind.Sequential)]
        private struct PERF_COUNTERSET_GUID
        {
            public Guid CounterSet;
            public uint Provider;
            public Guid ProviderGuid;
            public ushort NameOffset;
            public ushort NameSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERF_COUNTER_IDENTIFIER
        {
            public IntPtr CounterSetGuid;   // GUID*
            public uint Size;
            public uint CounterId;
            public uint InstanceId;         // 0 或 UInt32.MaxValue = _Total
            public uint Index;
            public ulong Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERF_DATA_HEADER
        {
            public uint TotalByteLength;
            public uint DefinitionsVersion;
            public uint HeaderVersion;
            public uint NumberOfTypes;
            public uint Flags;
            public ulong TotalCounterCount;
            public ulong TimeStamp;
            public ulong PerfTime;
            public ulong PerfTimeFreq;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERF_COUNTER_DATA_BLOCK
        {
            public uint ByteLength;
            public uint NumberOfEntries;
            public uint OffsetFirstEntry;
            public uint Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERF_COUNTER_DATA
        {
            public uint Size;
            public uint CounterId;
            public uint InstanceId;
            private int padding;
            public ulong Value;
            public ulong TimeStamp;
        }

        [DllImport(PerfApiDll, CharSet = CharSet.Unicode)]
        private static extern uint PerfEnumerateCounterSet(string szMachine, IntPtr pCounterSetIds, uint cCounterSetIds, out uint pcCounterSetIdsActual);

        [DllImport(PerfApiDll, CharSet = CharSet.Unicode)]
        private static extern uint PerfOpenQueryHandle(string szMachine, out IntPtr phQuery);

        [DllImport(PerfApiDll)]
        private static extern uint PerfAddCounters(IntPtr hQuery, IntPtr pCounters, uint cbCounters);

        [DllImport(PerfApiDll)]
        private static extern uint PerfQueryCounterData(IntPtr hQuery, IntPtr pCounterBlock, uint cbCounterBlock, out uint pcbCounterBlockActual);

        [DllImport(PerfApiDll)]
        private static extern uint PerfCloseQueryHandle(IntPtr hQuery);

        #endregion

        #region 字段

        private readonly object _sync = new object();
        private InitState _state = InitState.NotAttempted;
        private string _apiErrorMessage;

        private IntPtr _hQuery;
        private IntPtr _nativeIdentifiers;
        private GCHandle _bufferPin;
        private byte[] _dataBuffer;

        private bool _hasPrevious;
        private ulong _prevUtility;
        private ulong _prevBase;

        private readonly CpuSampler _fallbackPack;

        #endregion

        public PerfLibUtilitySampler()
        {
            _fallbackPack = new CpuSampler();
        }

        #region 诊断

        public string Diagnose()
        {
            EnsureInitialized();

            return _state switch
            {
                InitState.Ready =>
                    $"PerfLib V2 诊断：已启用 % Processor Utility，系统总CPU采用 Utility 口径。",
                InitState.GuidNotFound =>
                    $"PerfLib V2 诊断：PerfLib V2 枚举正常，但本机未注册 Processor Information V2 Counter Set {{B4FC721A-0378-476F-89BA-A5A79F810B36}}，已回退 GetSystemTimes (Processor Time)。",
                InitState.CounterUnavailable =>
                    $"PerfLib V2 诊断：Processor Information Counter Set 存在，但 % Processor Utility/Base (26/27) 不可用，已回退 GetSystemTimes (Processor Time)。",
                InitState.ApiFailed =>
                    $"PerfLib V2 诊断：PerfLib V2 Consumer API 无法调用：{_apiErrorMessage}。已回退 GetSystemTimes (Processor Time)。",
                _ => "PerfLib V2 诊断：尚未初始化。"
            };
        }

        private Guid[] EnumerateCounterSetGuids()
        {
            uint count = 0;
            uint hr = PerfEnumerateCounterSet(null, IntPtr.Zero, 0, out count);
            // 首次取大小在部分系统会返回 ERROR_NOT_ENOUGH_MEMORY(0x8) 但已回填 count，只要 count>0 继续
            if ((hr != ERROR_SUCCESS && hr != 0x8) || count == 0)
                return Array.Empty<Guid>();

            int size = Marshal.SizeOf<PERF_COUNTERSET_GUID>();
            IntPtr buf = Marshal.AllocHGlobal(size * checked((int)count));
            try
            {
                hr = PerfEnumerateCounterSet(null, buf, count, out uint actual);
                if (hr != ERROR_SUCCESS)
                    return Array.Empty<Guid>();

                var guids = new Guid[checked((int)actual)];
                for (uint i = 0; i < actual; i++)
                {
                    var set = Marshal.PtrToStructure<PERF_COUNTERSET_GUID>(IntPtr.Add(buf, size * checked((int)i)));
                    guids[i] = set.CounterSet;
                }
                return guids;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        #endregion

        #region 初始化

        private void EnsureInitialized()
        {
            lock (_sync)
            {
                if (_state != InitState.NotAttempted)
                    return;

                try
                {
                    // 枚举 Counter Set GUID，任何 P/Invoke/加载异常都视为 API 失败，而非 Counter Set 未注册
                    Guid[] guids = EnumerateCounterSetGuids();
                    bool found = Array.IndexOf(guids, ProcessorInformationGuid) >= 0;
                    if (!found)
                    {
                        _state = InitState.GuidNotFound;
                        LogHelper.Warning("[PerfLibUtility] Processor Information V2 Counter Set 未注册，已回退到 GetSystemTimes (Processor Time)。");
                        return;
                    }

                    if (PerfOpenQueryHandle(null, out _hQuery) != ERROR_SUCCESS)
                    {
                        _state = InitState.GuidNotFound;
                        LogHelper.Warning("[PerfLibUtility] PerfOpenQueryHandle 失败，回退 GetSystemTimes (Processor Time)。");
                        return;
                    }

                    // 构造两个标识符（Utility + Base），_Total 实例
                    Guid guid = ProcessorInformationGuid;
                    _nativeIdentifiers = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>() + Marshal.SizeOf<PERF_COUNTER_IDENTIFIER>() * 2);
                    IntPtr guidPtr = _nativeIdentifiers;
                    Marshal.StructureToPtr(guid, guidPtr, false);
                    IntPtr arrPtr = IntPtr.Add(_nativeIdentifiers, Marshal.SizeOf<Guid>());

                    var id0 = new PERF_COUNTER_IDENTIFIER
                    {
                        CounterSetGuid = guidPtr,
                        Size = (uint)Marshal.SizeOf<PERF_COUNTER_IDENTIFIER>(),
                        CounterId = CounterId_ProcessorUtility,
                        InstanceId = 0xFFFFFFFF
                    };
                    var id1 = new PERF_COUNTER_IDENTIFIER
                    {
                        CounterSetGuid = guidPtr,
                        Size = (uint)Marshal.SizeOf<PERF_COUNTER_IDENTIFIER>(),
                        CounterId = CounterId_UtilityBase,
                        InstanceId = 0xFFFFFFFF
                    };
                    Marshal.StructureToPtr(id0, arrPtr, false);
                    Marshal.StructureToPtr(id1, IntPtr.Add(arrPtr, Marshal.SizeOf<PERF_COUNTER_IDENTIFIER>()), false);

                    uint hrAdd = PerfAddCounters(_hQuery, arrPtr, (uint)(Marshal.SizeOf<PERF_COUNTER_IDENTIFIER>() * 2));
                    if (hrAdd != ERROR_SUCCESS)
                    {
                        _state = InitState.CounterUnavailable;
                        LogHelper.Warning($"[PerfLibUtility] Processor Information Counter Set 存在，但 % Processor Utility/Base (26/27) 添加失败 HR=0x{hrAdd:X}，回退 GetSystemTimes (Processor Time)。");
                        CleanupPerfLib();
                        return;
                    }

                    _dataBuffer = new byte[4096];
                    _bufferPin = GCHandle.Alloc(_dataBuffer, GCHandleType.Pinned);
                    _state = InitState.Ready;
                    LogHelper.Info("[PerfLibUtility] 已启用 % Processor Utility，系统总CPU采用 Utility 口径。");
                }
                catch (DllNotFoundException ex)
                {
                    _state = InitState.ApiFailed;
                    _apiErrorMessage = "DllNotFound: " + ex.Message;
                    LogHelper.Warning("[PerfLibUtility] API失败（DllNotFound），回退 GetSystemTimes (Processor Time)。");
                    CleanupPerfLib();
                }
                catch (EntryPointNotFoundException ex)
                {
                    _state = InitState.ApiFailed;
                    _apiErrorMessage = "EntryPointNotFound: " + ex.Message;
                    LogHelper.Warning("[PerfLibUtility] API失败（EntryPointNotFound），回退 GetSystemTimes (Processor Time)。");
                    CleanupPerfLib();
                }
                catch (Exception ex)
                {
                    _state = InitState.ApiFailed;
                    _apiErrorMessage = ex.GetType().Name + ": " + ex.Message;
                    LogHelper.Error("[PerfLibUtility] 初始化异常，回退 GetSystemTimes (Processor Time)。" + ex);
                    CleanupPerfLib();
                }
            }
        }

        private void CleanupPerfLib()
        {
            if (_hQuery != IntPtr.Zero)
            {
                PerfCloseQueryHandle(_hQuery);
                _hQuery = IntPtr.Zero;
            }
            if (_bufferPin.IsAllocated)
                _bufferPin.Free();
            if (_nativeIdentifiers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_nativeIdentifiers);
                _nativeIdentifiers = IntPtr.Zero;
            }
            _dataBuffer = null;
        }

        #endregion

        #region 采样

        public CpuSnapshot Capture(double detailThresholdPercent)
        {
            lock (_sync)
            {
                EnsureInitialized();

                if (_state != InitState.Ready)
                    return _fallbackPack.Capture(detailThresholdPercent);

                var snapshot = new CpuSnapshot { CapturedAt = DateTime.Now };
                try
                {
                    if (!TryQueryUtility(out ulong utility, out ulong baseValue))
                    {
                        _state = InitState.CounterUnavailable;
                        CleanupPerfLib();
                        return _fallbackPack.Capture(detailThresholdPercent);
                    }

                    if (!_hasPrevious)
                    {
                        _prevUtility = utility;
                        _prevBase = baseValue;
                        _hasPrevious = true;
                        snapshot.MetricSource = CpuMetricSource.ProcessorUtility;
                        snapshot.Error = "CPU采样器预热中";
                        var fb = _fallbackPack.Capture(detailThresholdPercent);
                        snapshot.TopProcesses = fb.TopProcesses ?? new System.Collections.Generic.List<ProcUsage>();
                        return snapshot;
                    }

                    ulong deltaUtility = SafeDelta(utility, _prevUtility);
                    ulong deltaBase = SafeDelta(baseValue, _prevBase);

                    snapshot.MetricSource = CpuMetricSource.ProcessorUtility;
                    // PERF_AVERAGE_BULK: (N1-N0)/(B1-B0)，provider 已按百分比缩放，不加 ×100
                    snapshot.UsagePercent = deltaBase == 0
                        ? 0
                        : Math.Round(ClampPercent(deltaUtility * 100.0 / deltaBase), 1);
                    snapshot.IsValid = true;

                    _prevUtility = utility;
                    _prevBase = baseValue;

                    var fallback = _fallbackPack.Capture(detailThresholdPercent);
                    snapshot.TopProcesses = fallback.TopProcesses ?? new System.Collections.Generic.List<ProcUsage>();

                    return snapshot;
                }
                catch (Exception ex)
                {
                    snapshot.IsValid = false;
                    snapshot.Error = "PerfLibUtilitySampler 采样异常: " + ex.Message;
                    _state = InitState.CounterUnavailable;
                    CleanupPerfLib();
                    return _fallbackPack.Capture(detailThresholdPercent);
                }
            }
        }

        private bool TryQueryUtility(out ulong utility, out ulong baseValue)
        {
            utility = 0;
            baseValue = 0;

            if (_hQuery == IntPtr.Zero)
                return false;

            uint used = 0;
            uint hr = PerfQueryCounterData(_hQuery, _bufferPin.AddrOfPinnedObject(), (uint)_dataBuffer.Length, out used);
            if (hr != ERROR_SUCCESS && hr != 0x1A) // 0x1A = ERROR_MORE_DATA
                return false;

            if (used > _dataBuffer.Length)
            {
                int newSize = checked((int)Math.Max((long)used, _dataBuffer.Length * 2L));
                Array.Resize(ref _dataBuffer, newSize);
                _bufferPin.Free();
                _bufferPin = GCHandle.Alloc(_dataBuffer, GCHandleType.Pinned);
                hr = PerfQueryCounterData(_hQuery, _bufferPin.AddrOfPinnedObject(), (uint)_dataBuffer.Length, out used);
                if (hr != ERROR_SUCCESS)
                    return false;
            }

            IntPtr basePtr = _bufferPin.AddrOfPinnedObject();
            IntPtr blockAddr = IntPtr.Add(basePtr, Marshal.SizeOf<PERF_DATA_HEADER>());
            PERF_COUNTER_DATA_BLOCK block = Marshal.PtrToStructure<PERF_COUNTER_DATA_BLOCK>(blockAddr);
            IntPtr entryAddr = IntPtr.Add(blockAddr, checked((int)block.OffsetFirstEntry));

            for (uint i = 0; i < block.NumberOfEntries; i++)
            {
                PERF_COUNTER_DATA data = Marshal.PtrToStructure<PERF_COUNTER_DATA>(entryAddr);
                if (data.CounterId == CounterId_ProcessorUtility)
                    utility = data.Value;
                else if (data.CounterId == CounterId_UtilityBase)
                    baseValue = data.Value;
                entryAddr = IntPtr.Add(entryAddr, checked((int)data.Size));
            }

            return baseValue != 0;
        }

        #endregion

        #region 辅助

        private static ulong SafeDelta(ulong current, ulong previous) => current >= previous ? current - previous : 0;
        private static double ClampPercent(double v) => v < 0 ? 0 : (v > 100 ? 100 : v);

        #endregion
    }
}