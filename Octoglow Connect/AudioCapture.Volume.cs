using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OctoglowSender;

internal sealed partial class AudioCapture
{
    internal readonly record struct MasterVolume(string Endpoint, int Percent, bool Muted, int Source);

    // A synchronous read owns every COM reference and apartment on this thread.
    // No capture client, audio stream, or volume setter is used.
    internal static MasterVolume? ReadMasterVolume(MasterVolume? previous = null)
    {
        var com = CoInitializeEx(0, CoinitMultithreaded);
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        IPropertyStore? properties = null;
        nint id = 0;
        try
        {
            ThrowOnFailure(CoCreateInstance(ClsidMMDeviceEnumerator, 0, ClsctxAll, IidMMDeviceEnumerator, out var pointer));
            enumerator = Wrap<IMMDeviceEnumerator>(pointer);
            enumerator.GetDefaultAudioEndpoint(0, 1, out device); // render, multimedia
            device.GetId(out id);
            var endpoint = Marshal.PtrToStringUni(id) ?? "";
            device.Activate(new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), ClsctxAll, 0, out var audio);
            volume = Wrap<IAudioEndpointVolume>(audio);
            volume.GetMasterVolumeLevelScalar(out var scalar);
            volume.GetMute(out var muted);
            int source = previous?.Endpoint == endpoint ? previous.Value.Source : 0;
            if (previous?.Endpoint != endpoint) try
            {
                device.OpenPropertyStore(StgmRead, out properties);
                var formKey = new PropertyKey(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFE7F0CA"), 0);
                properties.GetValue(formKey, out var form);
                try { if (form.Vt == 19 && ((int)form.Pointer == 3 || (int)form.Pointer == 5)) source = 1; }
                finally { PropVariantClear(ref form); }
                // Some drivers identify the transport only in interface metadata.
                // Unknown transport deliberately retains the physical form factor.
                var interfaceName = ReadVolumeProperty(properties, new(new Guid("026E516E-B814-414B-83CD-856D6FEF4822"), 2));
                var instance = ReadVolumeProperty(properties, new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256));
                var metadata = ConnectedAudioAdapter(device) + " " + interfaceName + " " + instance + " " + FriendlyName(device);
                if (metadata.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                    metadata.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                    metadata.Contains("BTHA2DP", StringComparison.OrdinalIgnoreCase) ||
                    metadata.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
                    metadata.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase)) source = 2;
            }
            catch { /* Volume remains usable when a driver omits optional properties. */ }
            return new MasterVolume(endpoint, Math.Clamp((int)Math.Round(scalar * 100), 0, 100), muted != 0, source);
        }
        catch { return null; } // disconnected endpoint or no output device
        finally
        {
            if (id != 0) Marshal.FreeCoTaskMem(id);
            Release(properties);
            Release(volume);
            Release(device);
            Release(enumerator);
            if (com >= 0) CoUninitialize();
        }
    }

    // The connected adapter ID exposes Bluetooth transport even when the
    // endpoint has been renamed. Software-only endpoints may have no topology.
    private static string ConnectedAudioAdapter(IMMDevice device)
    {
        IDeviceTopology? topology = null;
        IConnector? connector = null;
        nint id = 0;
        try
        {
            device.Activate(new Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), ClsctxAll, 0, out var pointer);
            topology = Wrap<IDeviceTopology>(pointer);
            topology.GetConnector(0, out connector);
            connector.GetDeviceIdConnectedTo(out id);
            return Marshal.PtrToStringUni(id) ?? "";
        }
        catch { return ""; }
        finally
        {
            if (id != 0) Marshal.FreeCoTaskMem(id);
            Release(connector);
            Release(topology);
        }
    }

    [GeneratedComInterface]
    [Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
    internal partial interface IDeviceTopology
    {
        void GetConnectorCount(out uint count);
        void GetConnector(uint index, out IConnector connector);
    }

    [GeneratedComInterface]
    [Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
    internal partial interface IConnector
    {
        void GetType(out int type);
        void GetDataFlow(out int flow);
        void ConnectTo(nint connector);
        void Disconnect();
        void IsConnected(out int connected);
        void GetConnectedTo(out nint connector);
        void GetConnectorIdConnectedTo(out nint id);
        void GetDeviceIdConnectedTo(out nint id);
    }

    private static string ReadVolumeProperty(IPropertyStore store, PropertyKey key)
    {
        try
        {
            store.GetValue(key, out var value);
            try { return value.Vt == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        catch { return ""; }
    }

    // Native vtable order through GetMute. BOOL uses a 32-bit integer.
    [GeneratedComInterface]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    internal partial interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(nint callback);
        void UnregisterControlChangeNotify(nint callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, nint context);
        void SetMasterVolumeLevelScalar(float level, nint context);
        void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, nint context);
        void SetChannelVolumeLevelScalar(uint channel, float level, nint context);
        void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute(int muted, nint context);
        void GetMute(out int muted);
    }
}
