using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OctoglowSender;

// Shared-mode WASAPI capture from one recording endpoint, picked by its name -
// what NAudio's MMDeviceEnumerator and WasapiCapture did for the Voicemeeter
// tile. NAudio reaches Windows' audio through [ComImport] interfaces, which need
// the built-in COM support a Native AOT build leaves out; these are generated at
// compile time instead, and cover only what the tile uses.
internal sealed partial class AudioCapture : IDisposable
{
    public readonly record struct CaptureFormat(int SampleRate, int Channels, int BitsPerSample, bool IsFloat)
    {
        public int BlockAlign => Channels * BitsPerSample / 8;
    }

    public CaptureFormat Format { get; }

    // The captured bytes and how many of them are new, on the capture thread.
    public event Action<byte[], int>? DataAvailable;

    private readonly IAudioClient _client;
    private readonly IAudioCaptureClient _captureClient;
    private readonly Thread _thread;
    private volatile bool _stopping;
    private volatile bool _ended;

    // False once the capture thread has stopped - its endpoint went away, say -
    // after which nothing more arrives and the capture must be opened again.
    public bool IsCapturing => !_ended && !_stopping;

    private AudioCapture(IAudioClient client, IAudioCaptureClient captureClient, CaptureFormat format)
    {
        _client = client;
        _captureClient = captureClient;
        Format = format;
        _thread = new Thread(Run) { IsBackground = true, Name = "Octoglow audio capture" };
    }

    // The first active recording endpoint whose name `matches`, capturing; null
    // when there is none or it can't be opened.
    public static AudioCapture? Start(Func<string, bool> matches)
    {
        var com = CoInitializeEx(0, CoinitMultithreaded);
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        IMMDevice? device = null;
        try
        {
            ThrowOnFailure(CoCreateInstance(ClsidMMDeviceEnumerator, 0, ClsctxAll, IidMMDeviceEnumerator, out var pointer));
            enumerator = Wrap<IMMDeviceEnumerator>(pointer);
            enumerator.EnumAudioEndpoints(DataFlowCapture, DeviceStateActive, out devices);
            devices.GetCount(out var count);
            for (uint i = 0; i < count && device is null; i++)
            {
                devices.Item(i, out var candidate);
                if (matches(FriendlyName(candidate))) device = candidate;
                else Release(candidate);
            }
            return device is null ? null : Open(device);
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(device);
            Release(devices);
            Release(enumerator);
            if (com >= 0) CoUninitialize();
        }
    }

    private static AudioCapture? Open(IMMDevice device)
    {
        IAudioClient? client = null;
        nint format = 0;
        try
        {
            device.Activate(IidAudioClient, ClsctxAll, 0, out var pointer);
            client = Wrap<IAudioClient>(pointer);
            client.GetMixFormat(out format);
            var captureFormat = ReadFormat(format);
            // 100 ms of buffer, as NAudio's capture had.
            client.Initialize(ShareModeShared, 0, 1_000_000, 0, format, 0);
            client.GetService(IidAudioCaptureClient, out var service);
            var capture = new AudioCapture(client, Wrap<IAudioCaptureClient>(service), captureFormat);
            client.Start();
            capture._thread.Start();
            client = null;
            return capture;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (format != 0) Marshal.FreeCoTaskMem(format);
            Release(client);
        }
    }

    // WAVEFORMATEX, and the sub-format of a WAVEFORMATEXTENSIBLE.
    private static CaptureFormat ReadFormat(nint format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = (ushort)Marshal.ReadInt16(format, 2);
        var sampleRate = Marshal.ReadInt32(format, 4);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        var isFloat = tag == WaveFormatIeeeFloat;
        if (tag == WaveFormatExtensible)
        {
            var subFormat = Marshal.PtrToStructure<Guid>(format + 24);
            isFloat = subFormat == SubtypeIeeeFloat;
        }
        return new CaptureFormat(sampleRate, Math.Max((int)channels, 1), bits, isFloat);
    }

    private static string FriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            device.OpenPropertyStore(StgmRead, out store);
            var key = PkeyDeviceFriendlyName;
            store.GetValue(key, out var value);
            try
            {
                return value.Vt == VtLpwstr && value.Pointer != 0 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return "";
        }
        finally
        {
            Release(store);
        }
    }

    // Drains what the endpoint has captured every 10 ms, a packet at a time.
    private void Run()
    {
        var com = CoInitializeEx(0, CoinitMultithreaded);
        var buffer = new byte[Format.BlockAlign * 4800];
        try
        {
            while (!_stopping)
            {
                Thread.Sleep(10);
                _captureClient.GetNextPacketSize(out var packet);
                while (packet > 0 && !_stopping)
                {
                    var result = _captureClient.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if (result < 0) Marshal.ThrowExceptionForHR(result);
                    if (result == BufferEmpty) break;
                    var bytes = (int)frames * Format.BlockAlign;
                    if (bytes > buffer.Length) buffer = new byte[bytes];
                    if ((flags & BufferFlagsSilent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    _captureClient.ReleaseBuffer(frames);
                    DataAvailable?.Invoke(buffer, bytes);
                    _captureClient.GetNextPacketSize(out packet);
                }
            }
        }
        catch
        {
            // The endpoint went away - Voicemeeter closed, say. Nothing more comes.
        }
        finally
        {
            _ended = true;
            if (com >= 0) CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(500);
        try
        {
            _client.Stop();
        }
        catch
        {
            // Already gone with its device.
        }
        Release(_captureClient);
        Release(_client);
    }

    // ------------------------------------------------------------------ COM

    private static readonly StrategyBasedComWrappers Wrappers = new();

    private static T Wrap<T>(nint pointer) where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    // Lets go of a COM object now rather than whenever the finalizer runs.
    private static void Release(object? instance)
    {
        if (instance is ComObject com) com.FinalRelease();
    }

    private static void ThrowOnFailure(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private const uint ClsctxAll = 0x17;
    private const uint CoinitMultithreaded = 0;
    private const int DataFlowCapture = 1;
    private const uint DeviceStateActive = 1;
    private const uint StgmRead = 0;
    private const ushort VtLpwstr = 31;
    private const int ShareModeShared = 0;
    private const uint BufferFlagsSilent = 2;
    private const int BufferEmpty = 0x08890001;
    private const ushort WaveFormatIeeeFloat = 3;
    private const ushort WaveFormatExtensible = 0xFFFE;
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    // The interfaces' ids spelled out, rather than read back from their [Guid] at run time.
    private static readonly Guid IidMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly PropertyKey PkeyDeviceFriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PropertyKey(Guid FormatId, int PropertyId);

    // Only the part of a PROPVARIANT the friendly name needs: its type, and the
    // string it points to.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Pointer;
        public nint Pointer2;
    }

    [GeneratedComInterface]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        void GetDevice(nint id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(nint client);
        void UnregisterEndpointNotificationCallback(nint client);
    }

    [GeneratedComInterface]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    internal partial interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [GeneratedComInterface]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        void Activate(in Guid iid, uint context, nint activationParams, out nint instance);
        void OpenPropertyStore(uint access, out IPropertyStore store);
        void GetId(out nint id);
        void GetState(out uint state);
    }

    [GeneratedComInterface]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal partial interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(in PropertyKey key, out PropVariant value);
        void SetValue(in PropertyKey key, in PropVariant value);
        void Commit();
    }

    [GeneratedComInterface]
    [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    internal partial interface IAudioClient
    {
        void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint frames);
        [PreserveSig]
        int IsFormatSupported(int shareMode, nint format, out nint closest);
        void GetMixFormat(out nint format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(nint handle);
        void GetService(in Guid iid, out nint service);
    }

    [GeneratedComInterface]
    [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    internal partial interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong counterPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
}
