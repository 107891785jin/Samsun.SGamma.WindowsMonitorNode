using System;
using System.Linq;
using System.Runtime.InteropServices;
using Samsun.SGamma.WindowsMonitorNode.Helpers;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors.Samplers
{
    /// <summary>
    /// 系统总 CPU 采样（PerfLib V2 Consumer API，% Processor Utility 口径）。
    /// 仅负责 Utility；组合器决定是否采用它，不可用时由 ProcessorTimeCpuSampler 兜底。
    ///
    /// PerfLib V2 Consumer API 位于 advapi32.dll。
    /// Counter Set GUID: {B4FC721A-0378-476F-89BA-A5A79F810B36} (Processor Information V2)
    /// Counter ID: 26 = % Processor Utility, 27 = % Utility Base
    /// </summary>
    internal sealed class ProcessorUtilitySampler : IDisposable
    {
        private const string PerfApiDll = "advapi32.dll";
        private const int ERROR_SUCCESS = 0;

        internal const int CounterId_ProcessorUtility = 26;
        internal const int CounterId_UtilityBase = 27;
        internal static readonly Guid ProcessorInformationGuid = new Guid("B4FC721A-0378-476F-89BA-A5A79F810B36");

        private enum InitState
        {
            NotAttempted,
            ApiFailed,
            QueryUnavailable,
            GuidNotFound,
            CounterUnavailable,
            Ready
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
            public IntPtr CounterSetGuid;
            public uint Size;
            public uint CounterId;
            public uint InstanceId;
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

        /// <summary>是否已确认可读取 % Processor Utility。</summary>
        public bool IsReady => _state == InitState.Ready;

        #endregion

        #region 诊断

        public string Diagnose()
        {
            EnsureInitialized();

            return _state switch
            {
                InitState.Ready =>
                    "CPU口径诊断：已启用 % Processor Utility，系统总CPU采用 Utility 口径。",
                InitState.GuidNotFound =>
                    "CPU口径诊断：PerfLib V2 枚举正常，但本机未注册 Processor Information V2 Counter Set {B4FC721A-0378-476F-89BA-A5A79F810B36}，回退 GetSystemTimes (Processor Time)。",
                InitState.QueryUnavailable =>
                    "CPU口径诊断：Counter Set 存在但无法打开查询句柄，回退 GetSystemTimes (Processor Time)。",
                InitState.CounterUnavailable =>
                    "CPU口径诊断：Counter Set 存在，但 % Processor Utility/Base (26/27) 不可用，回退 GetSystemTimes (Processor Time)。",
                InitState.ApiFailed =>
                    $"CPU口径诊断：PerfLib V2 Consumer API 无法调用：{_apiErrorMessage}。回退 GetSystemTimes (Processor Time)。",
                _ => "CPU口径诊断：尚未初始化。"
            };
        }

        private Guid[] EnumerateCounterSetGuids()
        {
            uint count = 0;
            uint hr = PerfEnumerateCounterSet(null, IntPtr.Zero, 0, out count);
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

        /// <summary>探测本机是否可用 Utility。返回 true 表示已 Ready，可调用 Capture。</summary>
        public bool AcquireIfAvailable()
        {
            lock (_sync)
            {
                EnsureInitialized();
                return _state == InitState.Ready;
            }
        }

        private void EnsureInitialized()
        {
            if (_state != InitState.NotAttempted)
                return;

            try
            {
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
                    _state = InitState.QueryUnavailable;
                    LogHelper.Warning("[PerfLibUtility] PerfOpenQueryHandle 失败，回退 GetSystemTimes (Processor Time)。");
                    return;
                }

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

        /// <summary>
        /// 采样一轮 Utility 总 CPU。
        /// 返回：Ready 且取到值的正常快照；预热轮返回预热快照（IsValid=false、Error=预热中）；
        /// 不可用或查询永久失败返回 null（组合器应切回 Processor Time）。
        /// </summary>
        public CpuSnapshot Capture()
        {
            lock (_sync)
            {
                if (_state != InitState.Ready)
                    return null;

                var snapshot = new CpuSnapshot { CapturedAt = DateTime.Now, MetricSource = CpuMetricSource.ProcessorUtility };

                if (!TryQueryUtility(out ulong utility, out ulong baseValue))
                {
                    _state = InitState.CounterUnavailable;
                    CleanupPerfLib();
                    return null;
                }

                if (!_hasPrevious)
                {
                    _prevUtility = utility;
                    _prevBase = baseValue;
                    _hasPrevious = true;
                    snapshot.Error = "CPU采样器预热中";
                    return snapshot;
                }

                ulong deltaUtility = SafeDelta(utility, _prevUtility);
                ulong deltaBase = SafeDelta(baseValue, _prevBase);

                // PERF_AVERAGE_BULK: (N1-N0)/(B1-B0)，provider 已按百分比缩放，不再额外 ×100
                snapshot.UsagePercent = deltaBase == 0
                    ? 0
                    : Math.Round(ClampPercent(SafePercent(deltaUtility, deltaBase)), 1);
                snapshot.IsValid = true;

                _prevUtility = utility;
                _prevBase = baseValue;
                return snapshot;
            }
        }

        /// <summary>PERF_AVERAGE_BULK 结果：(N1-N0)/(B1-B0)。provider 已按百分比缩放，不额外 ×100。</summary>
        private static double SafePercent(ulong n, ulong b) => (double)n / (double)b;

        private bool TryQueryUtility(out ulong utility, out ulong baseValue)
        {
            utility = 0;
            baseValue = 0;

            if (_hQuery == IntPtr.Zero)
                return false;

            uint used = 0;
            uint hr = PerfQueryCounterData(_hQuery, _bufferPin.AddrOfPinnedObject(), (uint)_dataBuffer.Length, out used);
            if (hr != ERROR_SUCCESS && hr != 0x1A)
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

        public void Dispose()
        {
            lock (_sync)
            {
                CleanupPerfLib();
            }
        }

        #endregion
    }
}