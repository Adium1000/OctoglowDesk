using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace OctoglowSender;

// BeamNG.drive: the game's OutGauge UDP packets, heard at the address and port
// set in Integrations (127.0.0.1 unless the game runs on another computer),
// turned into whole km/h for the clock's BeamNG tile. Nothing is written to the activity log per packet or per
// request - only when packets start or stop arriving, or something is wrong.
// A game that isn't running, or has OutGauge off, simply sends nothing.
public partial class SenderBackend
{
    private static readonly TimeSpan BeamNgTick = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan BeamNgBindRetryEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BeamNgSendRetryAfter = TimeSpan.FromSeconds(1);
    // A clock with the tile off is asked again only this often, while the game sends.
    private static readonly TimeSpan BeamNgDisabledRecheck = TimeSpan.FromSeconds(5);
    // A firmware without the tile, rarely, in case it gets updated.
    private static readonly TimeSpan BeamNgUnsupportedRecheck = TimeSpan.FromSeconds(30);

    private enum BeamNgSendResult { Sent, Disabled, Unsupported, Failed }

    private async Task BeamNgLoopAsync(CancellationToken token)
    {
        var planner = new SpeedTilePlanner();
        UdpClient? udp = null;
        Task<UdpReceiveResult>? pending = null;
        var boundPort = 0;
        var boundAddress = "";
        var bindRetryAt = DateTime.MinValue;
        var bindFailedAt = "";
        var wasReceiving = false;
        var loggedBadPacket = false;
        var loggedUnsupported = false;
        var holdUntil = DateTime.MinValue;
        string? problem = null;

        void CloseSocket()
        {
            udp?.Dispose();
            udp = null;
            pending = null;
            boundPort = 0;
            boundAddress = "";
        }

        void Take(byte[] datagram, DateTime now)
        {
            if (OutGaugePacket.TryParse(datagram, out var packet))
            {
                planner.OnSpeed(packet.SpeedKmh, now);
                return;
            }
            if (loggedBadPacket) return;
            loggedBadPacket = true;
            _log($"[BEAMNG WARN] Am ignorat un pachet care nu este OutGauge ({datagram.Length} bytes; se așteaptă 92 sau 96).");
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                // 1. Listening at the address and port Integrations name, and only there.
                var address = ConfigStore.BeamNgOutGaugeAddress;
                var port = ConfigStore.BeamNgOutGaugePort;
                var endpoint = $"{address}:{port}";
                if (udp is not null && (port != boundPort || address != boundAddress)) CloseSocket();
                if (udp is null && (now >= bindRetryAt || endpoint != bindFailedAt))
                {
                    try
                    {
                        var ip = BeamNgListenAddress.TryParse(address, out var parsed) ? parsed : IPAddress.Loopback;
                        udp = new UdpClient(new IPEndPoint(ip, port));
                        boundPort = port;
                        boundAddress = address;
                        bindFailedAt = "";
                        problem = null;
                        _log($"[BEAMNG] Ascult OutGauge pe {endpoint}.");
                    }
                    catch (SocketException error)
                    {
                        if (bindFailedAt != endpoint)
                            _log($"[BEAMNG WARN] Adresa OutGauge {endpoint} nu poate fi folosită: {error.Message}");
                        bindFailedAt = endpoint;
                        bindRetryAt = now + BeamNgBindRetryEvery;
                        problem = AppStrings.IsEnglish ? "The address or port can't be used" : "Adresa sau portul nu poate fi folosit";
                    }
                }

                // 2. Whatever arrived, waiting up to one tick for the first packet.
                if (udp is not null)
                {
                    try
                    {
                        pending ??= udp.ReceiveAsync(token).AsTask();
                        var finished = await Task.WhenAny(pending, Task.Delay(BeamNgTick, token));
                        if (finished == pending)
                        {
                            var result = await pending;
                            pending = null;
                            now = DateTime.UtcNow;
                            Take(result.Buffer, now);
                            var remote = new IPEndPoint(IPAddress.Any, 0);
                            while (udp.Available > 0) Take(udp.Receive(ref remote), now);
                        }
                    }
                    catch (SocketException)
                    {
                        // The socket failed under us: opened again next time round.
                        CloseSocket();
                    }
                }
                else
                {
                    await Task.Delay(BeamNgTick, token);
                }

                now = DateTime.UtcNow;
                var receiving = planner.Receiving(now);
                if (receiving != wasReceiving)
                {
                    wasReceiving = receiving;
                    _log(receiving ? "[BEAMNG] Primesc date OutGauge de la BeamNG.drive." : "[BEAMNG] Nu mai sosesc date OutGauge.");
                }
                BeamNgStatus.Set(receiving, boundPort == 0 ? port : boundPort, problem);

                // 3. The clock hears only what the planner lets through.
                if (now < holdUntil) continue;
                var next = planner.Next(now, ConfigStore.StopBeamNgTileWhenVehicleStops);
                if (next is not { } send) continue;
                switch (await SendBeamNgAsync(send, token))
                {
                    case BeamNgSendResult.Sent:
                        planner.Sent(send, DateTime.UtcNow);
                        loggedUnsupported = false;
                        break;
                    case BeamNgSendResult.Disabled:
                        // Switched off on the clock: nothing is up there to keep.
                        planner.Forget();
                        holdUntil = DateTime.UtcNow + BeamNgDisabledRecheck;
                        break;
                    case BeamNgSendResult.Unsupported:
                        planner.Forget();
                        if (!loggedUnsupported)
                            _log("[BEAMNG] Firmware-ul ceasului nu are încă tile-ul BeamNG.drive. Actualizează firmware-ul ceasului.");
                        loggedUnsupported = true;
                        holdUntil = DateTime.UtcNow + BeamNgUnsupportedRecheck;
                        break;
                    default:
                        holdUntil = DateTime.UtcNow + BeamNgSendRetryAfter;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            CloseSocket();
            BeamNgStatus.Set(false, ConfigStore.BeamNgOutGaugePort, null);
        }
    }

    // POST /beamngspeed: speed=<whole km/h>, or active=0 to take the tile down.
    // The clock answers DISABLED while its BeamNG tile is switched off, and 404
    // when its firmware predates the tile.
    private async Task<BeamNgSendResult> SendBeamNgAsync(SpeedSend send, CancellationToken token)
    {
        try
        {
            var form = send.Kind == SpeedSendKind.Stop
                ? new Dictionary<string, string> { ["active"] = "0" }
                : new Dictionary<string, string> { ["speed"] = send.Kmh.ToString(CultureInfo.InvariantCulture) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/beamngspeed")
            {
                Content = new FormUrlEncodedContent(form)
            };
            if (!string.IsNullOrWhiteSpace(_sessionCookie)) request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await LoginAsync(token, retries: 1, delaySeconds: 1);
                return BeamNgSendResult.Failed;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return BeamNgSendResult.Unsupported;
            if (!response.IsSuccessStatusCode) return BeamNgSendResult.Failed;
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return body.Trim() == "DISABLED" ? BeamNgSendResult.Disabled : BeamNgSendResult.Sent;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (token.IsCancellationRequested && error is OperationCanceledException) throw;
            return BeamNgSendResult.Failed;
        }
    }
}
