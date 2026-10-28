using System.Text.RegularExpressions;

namespace OctoglowSender;

// A firmware build published for the clock, and where to get it from.
internal sealed record FirmwareRelease(string Version, string Name, string Notes, string AssetName, string DownloadUrl, long Size);

// Use the same published updater files as the clock's web interface. Firmware
// updates are published in Updater/ on the main branch, not GitHub Releases.
internal static partial class FirmwareUpdates
{
    private const string UpdaterBaseUrl = "https://raw.githubusercontent.com/Adium1000/Octoglow/main/Updater/";
    private const string VersionUrl = UpdaterBaseUrl + "UpdaterNewVersion.MD";
    private const string FirmwareUrl = UpdaterBaseUrl + "Update.bin";
    // The clock's OTA partition is below 2 MiB. A larger response is not an
    // installable image and must never be buffered without a limit in Connect.
    private const int MaxFirmwareBytes = 4 * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OctoglowConnect");
        return client;
    }

    public static async Task<FirmwareRelease> FindLatestAsync(CancellationToken token)
    {
        using var response = await Client.GetAsync(VersionUrl, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(L(
                $"Nu s-a putut citi versiunea firmware-ului: GitHub a răspuns cu {(int)response.StatusCode}.",
                $"Could not read the firmware version: GitHub answered with {(int)response.StatusCode}."));

        var version = (await response.Content.ReadAsStringAsync(token)).Trim().TrimStart('\uFEFF').Trim();
        if (!PublishedVersionPattern().IsMatch(version))
            throw new InvalidOperationException(L(
                "Versiunea publicată a firmware-ului este invalidă.",
                "The published firmware version is invalid."));

        return new FirmwareRelease(version,
            L("Firmware Octoglow Clock", "Octoglow Clock firmware"),
            "", "Update.bin", FirmwareUrl, 0);
    }

    public static async Task<byte[]> DownloadAsync(FirmwareRelease release, IProgress<int> progress, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var downloadToken = deadline.Token;
        using var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, downloadToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(L(
                $"Descărcarea a eșuat: GitHub a răspuns cu {(int)response.StatusCode}.",
                $"The download failed: GitHub answered with {(int)response.StatusCode}."));

        var total = response.Content.Headers.ContentLength ?? release.Size;
        if (total > MaxFirmwareBytes)
            throw new InvalidDataException(L("Firmware-ul publicat este prea mare pentru ceas.",
                "The published firmware image is too large for the clock."));
        using var source = await response.Content.ReadAsStreamAsync(downloadToken);
        using var firmware = new MemoryStream();
        var chunk = new byte[64 * 1024];
        var reported = -1;
        while (true)
        {
            var read = await source.ReadAsync(chunk, downloadToken);
            if (read == 0) break;
            if (firmware.Length + read > MaxFirmwareBytes)
                throw new InvalidDataException(L("Firmware-ul publicat este prea mare pentru ceas.",
                    "The published firmware image is too large for the clock."));
            firmware.Write(chunk, 0, read);
            if (total <= 0) continue;
            // Only whole percents, so the card's text isn't rewritten on every chunk.
            var percent = (int)Math.Min(100, 100 * firmware.Length / total);
            if (percent == reported) continue;
            reported = percent;
            progress.Report(percent);
        }
        var bytes = firmware.ToArray();
        // The published Update.bin may be temporarily empty while it is being
        // replaced. Never pass an empty or non-ESP32 image to /swupdateupload.
        if (bytes.Length < 1024 || bytes[0] != 0xE9)
            throw new InvalidDataException(L(
                "Update.bin este gol sau nu este un firmware ESP32 valid. Încearcă din nou după ce este republicat.",
                "Update.bin is empty or is not a valid ESP32 firmware image. Try again after it is republished."));
        return bytes;
    }

    // Numbers first: 0.4 is newer than 0.3.
    public static bool IsNewer(string candidate, string installed)
    {
        var left = Numbers(candidate);
        var right = Numbers(installed);
        if (left.Count == 0 || right.Count == 0)
            return !string.Equals(candidate.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);
        for (var part = 0; part < Math.Max(left.Count, right.Count); part++)
        {
            var one = part < left.Count ? left[part] : 0;
            var other = part < right.Count ? right[part] : 0;
            if (one != other) return one > other;
        }
        return false;
    }

    private static List<int> Numbers(string version)
    {
        var match = VersionPattern().Match(version);
        if (!match.Success) return [];
        return match.Value.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToList();
    }

    [GeneratedRegex(@"\d+(?:\.\d+)*")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^v?\d+(?:\.\d+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedVersionPattern();

    private static string L(string ro, string en) => AppStrings.IsEnglish ? en : ro;
}
