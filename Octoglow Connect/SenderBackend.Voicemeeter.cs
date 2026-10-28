using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OctoglowSender;

public partial class SenderBackend
{
    private sealed record VoicemeeterClockState(bool Enabled, int Mode, int Sensitivity);

    private async Task VoicemeeterLoopAsync(CancellationToken token)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        VoicemeeterCapture? capture = null;
        IPEndPoint? target = null;
        byte[] key = [];
        ushort sequence = 0;
        var state = new VoicemeeterClockState(false, 0, 60);
        var nextStateRead = DateTime.MinValue;
        var nextSubscription = DateTime.MinValue;
        var selectedBus = "";
        var sentActive = false;
        var firmwareUnsupportedLogged = false;
        var captureMissingLogged = false;
        var appRunning = false;
        var appStateLogged = (bool?)null;
        var nextAppCheck = DateTime.MinValue;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                if (now >= nextStateRead)
                {
                    var fresh = await ReadVoicemeeterStateAsync(token);
                    if (fresh is not null)
                    {
                        state = fresh;
                        firmwareUnsupportedLogged = false;
                    }
                    else if (!firmwareUnsupportedLogged)
                    {
                        firmwareUnsupportedLogged = true;
                        _log("[VOICEMEETER] Firmware-ul ceasului nu oferă încă tile-ul Voicemeeter.");
                    }
                    nextStateRead = now + TimeSpan.FromSeconds(2);
                }

                // Voicemeeter's virtual devices stay in Windows after the app is
                // closed, and a capture on them opens and keeps running - silent,
                // or passing sound straight through. With the silence stop
                // switched off that alone kept the tile on the clock for good, so
                // the tile only runs while the Voicemeeter application does.
                if (state.Enabled && now >= nextAppCheck)
                {
                    appRunning = IsVoicemeeterRunning();
                    nextAppCheck = now + TimeSpan.FromSeconds(2);
                    if (appStateLogged != appRunning)
                    {
                        if (!appRunning)
                            _log("[VOICEMEETER] Voicemeeter nu rulează. Tile-ul rămâne ascuns până îl pornești.");
                        else if (appStateLogged is not null)
                            _log("[VOICEMEETER] Voicemeeter a pornit.");
                        appStateLogged = appRunning;
                    }
                }

                // The capture thread ends when its device goes away; open it again.
                if (capture is not null && !capture.IsCapturing)
                {
                    capture.Dispose();
                    capture = null;
                }

                if (!state.Enabled || !appRunning)
                {
                    if (sentActive && target is not null && key.Length == 4)
                        await SendVoicemeeterPacketAsync(udp, target, key, sequence++, false, new byte[32], token);
                    sentActive = false;
                    capture?.Dispose();
                    capture = null;
                    selectedBus = "";
                    await Task.Delay(500, token);
                    continue;
                }

                var bus = ConfigStore.VoicemeeterBus;
                if (capture is null || !string.Equals(selectedBus, bus, StringComparison.OrdinalIgnoreCase))
                {
                    capture?.Dispose();
                    capture = VoicemeeterCapture.TryStart(bus);
                    selectedBus = bus;
                    if (capture is null)
                    {
                        if (!captureMissingLogged)
                        {
                            captureMissingLogged = true;
                            _log($"[VOICEMEETER] Nu găsesc ieșirea {bus}. Pornește Voicemeeter și verifică busul ales.");
                        }
                        await Task.Delay(2000, token);
                        continue;
                    }
                    captureMissingLogged = false;
                    _log($"[VOICEMEETER] Analizor audio pornit pe {bus}.");
                }

                if (target is null || key.Length != 4 || now >= nextSubscription)
                {
                    var subscription = await GetVoicemeeterSubscriptionAsync(token);
                    if (subscription is null)
                    {
                        target = null;
                        key = [];
                        await Task.Delay(1000, token);
                        continue;
                    }
                    target = subscription.Value.Target;
                    key = subscription.Value.Key;
                    nextSubscription = now + TimeSpan.FromMinutes(1);
                }

                var autoStop = ConfigStore.VoicemeeterAutoStopOnSilence;
                var silence = TimeSpan.FromSeconds(ConfigStore.VoicemeeterSilenceSeconds);
                var frame = capture.BuildFrame(state.Mode, state.Sensitivity, autoStop, silence, out var active);
                try
                {
                    await SendVoicemeeterPacketAsync(udp, target, key, sequence++, active, frame, token);
                    sentActive = active;
                }
                catch (SocketException)
                {
                    target = null;
                }
                await Task.Delay(50, token); // 20 FPS, one datagram at a time.
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (sentActive && target is not null && key.Length == 4)
            {
                try { await SendVoicemeeterPacketAsync(udp, target, key, sequence, false, new byte[32], CancellationToken.None); }
                catch { }
            }
            capture?.Dispose();
        }
    }

    // Voicemeeter, Banana or Potato, 32 or 64 bit: voicemeeter(_x64),
    // voicemeeterpro(_x64), voicemeeter8(x64). Not its setup, Macro Buttons or
    // the other tools installed beside it.
    private static bool IsVoicemeeterRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true; // Can't tell: let the capture decide, as before.
        }
        var running = false;
        foreach (var process in processes)
        {
            try
            {
                if (!running && VoicemeeterProcessName().IsMatch(process.ProcessName)) running = true;
            }
            catch (InvalidOperationException)
            {
                // Exited while the list was read.
            }
            finally
            {
                process.Dispose();
            }
        }
        return running;
    }

    [GeneratedRegex("^voicemeeter(pro|8)?(_?x64)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoicemeeterProcessName();

    private async Task<VoicemeeterClockState?> ReadVoicemeeterStateAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/voicemeeterstate");
            if (!string.IsNullOrWhiteSpace(_sessionCookie))
                request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root = document.RootElement;
            return new VoicemeeterClockState(
                root.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(),
                root.TryGetProperty("mode", out var mode) && mode.TryGetInt32(out var m) ? Math.Clamp(m, 0, 4) : 0,
                root.TryGetProperty("sensitivity", out var sensitivity) && sensitivity.TryGetInt32(out var s) ? Math.Clamp(s, 10, 100) : 60);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<(IPEndPoint Target, byte[] Key)?> GetVoicemeeterSubscriptionAsync(CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/screensub");
            if (!string.IsNullOrWhiteSpace(_sessionCookie))
                request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!TryParseScreenSubscription(json, out var port, out var key)) return null;
            var addresses = await Dns.GetHostAddressesAsync(_cfg.Esp32Ip, token);
            var address = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork);
            return address is null ? null : (new IPEndPoint(address, port), key);
        }
        catch (Exception error) when (error is HttpRequestException or SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task SendVoicemeeterPacketAsync(
        UdpClient udp,
        IPEndPoint target,
        byte[] key,
        ushort sequence,
        bool active,
        byte[] columns,
        CancellationToken token)
    {
        var packet = new byte[43];
        "OGVM"u8.CopyTo(packet);
        key.CopyTo(packet, 4);
        packet[8] = (byte)(sequence >> 8);
        packet[9] = (byte)sequence;
        packet[10] = active ? (byte)1 : (byte)0;
        Buffer.BlockCopy(columns, 0, packet, 11, 32);
        await udp.SendAsync(packet, target, token);
    }

    private sealed class VoicemeeterCapture : IDisposable
    {
        private const int WindowSize = 1024;
        private readonly object _gate = new();
        private readonly float[] _left = new float[4096];
        private readonly float[] _right = new float[4096];
        private readonly float[] _smooth = new float[32];
        private readonly float[] _peaks = new float[32];
        private readonly int[] _peakHold = new int[32];
        private readonly AudioCapture _capture;
        private int _write;
        private int _count;
        // Nothing heard yet. Starting it at "now" counted the first seconds after
        // the capture opened - Connect starting, the clock coming back, a bus
        // change - as sound, and the clock showed an empty Voicemeeter tile for
        // the length of the silence timeout before dropping it.
        private DateTime _lastAudibleUtc = DateTime.MinValue;
        private float _smoothedLeft;
        private float _smoothedRight;

        private VoicemeeterCapture(AudioCapture capture)
        {
            _capture = capture;
            _capture.DataAvailable += OnData;
        }

        public bool IsCapturing => _capture.IsCapturing;

        // Voicemeeter's output for the bus is a recording device of its own.
        public static VoicemeeterCapture? TryStart(string bus) =>
            AudioCapture.Start(name => MatchesBus(name, bus)) is { } capture ? new VoicemeeterCapture(capture) : null;

        private static bool MatchesBus(string name, string bus)
        {
            var upper = name.ToUpperInvariant();
            if (!upper.Contains("VOICEMEETER")) return false;
            return bus.ToUpperInvariant() switch
            {
                "B2" => upper.Contains("AUX OUTPUT") || upper.Contains("OUT B2"),
                "B3" => upper.Contains("VAIO3 OUTPUT") || upper.Contains("OUT B3"),
                _ => upper.Contains("OUT B1") ||
                     (upper.Contains("OUTPUT") && !upper.Contains("AUX OUTPUT") && !upper.Contains("VAIO3 OUTPUT")),
            };
        }

        private void OnData(byte[] buffer, int bytesRecorded)
        {
            var format = _capture.Format;
            var channels = Math.Max(1, format.Channels);
            var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
            var frameBytes = bytesPerSample * channels;
            lock (_gate)
            {
                for (var offset = 0; offset + frameBytes <= bytesRecorded; offset += frameBytes)
                {
                    var left = ReadSample(buffer, offset, format);
                    var right = channels > 1 ? ReadSample(buffer, offset + bytesPerSample, format) : left;
                    _left[_write] = left;
                    _right[_write] = right;
                    _write = (_write + 1) % _left.Length;
                    if (_count < _left.Length) _count++;
                }
            }
        }

        private static float ReadSample(byte[] data, int offset, AudioCapture.CaptureFormat format)
        {
            if (format.BitsPerSample == 32 && format.IsFloat)
            {
                var value = BitConverter.ToSingle(data, offset);
                if (!float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 8) return Math.Clamp(value, -1, 1);
            }
            return format.BitsPerSample switch
            {
                16 => BitConverter.ToInt16(data, offset) / 32768f,
                24 => Read24(data, offset) / 8388608f,
                32 => BitConverter.ToInt32(data, offset) / 2147483648f,
                _ => 0,
            };
        }

        private static int Read24(byte[] data, int offset)
        {
            var value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
            return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
        }

        public byte[] BuildFrame(int mode, int sensitivity, bool autoStop, TimeSpan silenceTimeout, out bool active)
        {
            var left = new float[WindowSize];
            var right = new float[WindowSize];
            int sampleRate;
            lock (_gate)
            {
                sampleRate = Math.Max(8000, _capture.Format.SampleRate);
                var available = Math.Min(_count, WindowSize);
                var start = (_write - available + _left.Length) % _left.Length;
                for (var i = 0; i < available; i++)
                {
                    left[WindowSize - available + i] = _left[(start + i) % _left.Length];
                    right[WindowSize - available + i] = _right[(start + i) % _right.Length];
                }
            }

            var leftRms = Rms(left);
            var rightRms = Rms(right);
            var audible = Math.Max(leftRms, rightRms) > 0.0025f;
            if (audible) _lastAudibleUtc = DateTime.UtcNow;
            active = !autoStop || audible || DateTime.UtcNow - _lastAudibleUtc < silenceTimeout;
            if (!active) return new byte[32];

            var gain = MathF.Pow(10f, (sensitivity - 55) / 38f);
            leftRms = Math.Clamp(leftRms * gain, 0, 1);
            rightRms = Math.Clamp(rightRms * gain, 0, 1);
            _smoothedLeft = Smooth(_smoothedLeft, leftRms);
            _smoothedRight = Smooth(_smoothedRight, rightRms);

            return Math.Clamp(mode, 0, 4) switch
            {
                1 => BuildVuFrame((_smoothedLeft + _smoothedRight) * 0.5f),
                2 => BuildStereoFrame(left, right, sampleRate, gain),
                3 => BuildWaveformFrame(left, right, gain),
                4 => BuildHorizontalFrame(_smoothedLeft, _smoothedRight),
                _ => BuildSpectrumFrame(left, right, sampleRate, gain),
            };
        }

        private byte[] BuildSpectrumFrame(float[] left, float[] right, int sampleRate, float gain)
        {
            var levels = new float[32];
            for (var band = 0; band < levels.Length; band++)
            {
                var t = band / 31f;
                var frequency = 55f * MathF.Pow(12000f / 55f, t);
                var omega = 2f * MathF.PI * frequency / sampleRate;
                float real = 0, imaginary = 0;
                for (var i = 0; i < WindowSize; i += 2)
                {
                    var sample = (left[i] + right[i]) * 0.5f;
                    var window = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (WindowSize - 1));
                    real += sample * window * MathF.Cos(omega * i);
                    imaginary -= sample * window * MathF.Sin(omega * i);
                }
                levels[band] = Math.Clamp(MathF.Sqrt(real * real + imaginary * imaginary) * gain / 30f, 0, 1);
            }
            return VerticalBars(levels);
        }

        private byte[] BuildStereoFrame(float[] left, float[] right, int sampleRate, float gain)
        {
            var levels = new float[32];
            for (var i = 0; i < 16; i++)
            {
                var start = i * WindowSize / 16;
                var end = (i + 1) * WindowSize / 16;
                levels[i] = SegmentRms(left, start, end) * gain * 4;
                levels[16 + i] = SegmentRms(right, start, end) * gain * 4;
            }
            return VerticalBars(levels);
        }

        private byte[] BuildVuFrame(float level)
        {
            var normalized = Math.Clamp(MathF.Sqrt(level) * 1.8f, 0, 1);
            var levels = Enumerable.Repeat(normalized, 32).ToArray();
            return VerticalBars(levels);
        }

        private byte[] BuildWaveformFrame(float[] left, float[] right, float gain)
        {
            var result = new byte[32];
            var previousY = 4;
            for (var x = 0; x < 32; x++)
            {
                var start = x * WindowSize / 32;
                var sample = (left[start] + right[start]) * 0.5f * gain * 2.4f;
                var y = Math.Clamp(4 - (int)MathF.Round(sample * 3.5f), 0, 7);
                var lo = Math.Min(y, previousY);
                var hi = Math.Max(y, previousY);
                for (var row = lo; row <= hi; row++) result[x] |= (byte)(1 << row);
                previousY = y;
            }
            return result;
        }

        private static byte[] BuildHorizontalFrame(float left, float right)
        {
            var result = new byte[32];
            var leftLength = Math.Clamp((int)MathF.Round(MathF.Sqrt(left) * 32), 0, 32);
            var rightLength = Math.Clamp((int)MathF.Round(MathF.Sqrt(right) * 32), 0, 32);
            for (var x = 0; x < leftLength; x++) result[x] |= 0b0000_0111;
            for (var x = 0; x < rightLength; x++) result[x] |= 0b1110_0000;
            return result;
        }

        private byte[] VerticalBars(float[] raw)
        {
            var result = new byte[32];
            for (var x = 0; x < 32; x++)
            {
                var target = Math.Clamp(raw[x], 0, 1) * 8;
                _smooth[x] = target >= _smooth[x] ? _smooth[x] * 0.35f + target * 0.65f : Math.Max(target, _smooth[x] - 0.42f);
                if (_smooth[x] >= _peaks[x])
                {
                    _peaks[x] = _smooth[x];
                    _peakHold[x] = 9;
                }
                else if (_peakHold[x] > 0)
                {
                    _peakHold[x]--;
                }
                else
                {
                    _peaks[x] = Math.Max(_smooth[x], _peaks[x] - 0.24f);
                }
                var height = Math.Clamp((int)MathF.Ceiling(_smooth[x]), 0, 8);
                for (var row = 8 - height; row < 8; row++) result[x] |= (byte)(1 << row);
                var peakRow = 7 - Math.Clamp((int)MathF.Round(_peaks[x]) - 1, 0, 7);
                if (_peaks[x] >= 0.5f) result[x] |= (byte)(1 << peakRow);
            }
            return result;
        }

        private static float Smooth(float current, float target) =>
            target >= current ? current * 0.35f + target * 0.65f : Math.Max(target, current - 0.035f);

        private static float Rms(float[] samples) => SegmentRms(samples, 0, samples.Length);

        private static float SegmentRms(float[] samples, int start, int end)
        {
            double sum = 0;
            for (var i = start; i < end; i++) sum += samples[i] * samples[i];
            return (float)Math.Sqrt(sum / Math.Max(1, end - start));
        }

        public void Dispose()
        {
            _capture.Dispose();
        }
    }
}
