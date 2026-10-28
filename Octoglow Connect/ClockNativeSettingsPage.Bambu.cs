using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

public sealed partial class ClockNativeSettingsPage
{
    // Bambu Studio's state on its Integrations card. Octoglow Connect reads the
    // percentage Bambu Studio shows under Device → Status from its window controls,
    // not an official API, so Studio has to stay open on that page with the
    // printer selected; anything else hides the tile rather than leave a stale
    // percentage up.
    private static string BambuShortText(BambuSnapshot snapshot) => snapshot.Connection switch
    {
        BambuConnectionState.Connected when snapshot.PrinterState == "FINISH" =>
            L("Imprimare finalizată", "Print complete"),
        BambuConnectionState.Connected =>
            L($"Progres: {snapshot.Progress}%", $"Progress: {snapshot.Progress}%"),
        BambuConnectionState.StudioClosed => L("Bambu Studio este închis", "Bambu Studio is closed"),
        BambuConnectionState.NoProgress => L("Progres indisponibil", "Progress unavailable"),
        BambuConnectionState.ClockUnsupported => L("Firmware vechi", "Old firmware"),
        _ => L("Inactiv", "Inactive"),
    };

    private static string BambuStateText(BambuSnapshot snapshot) => snapshot.Connection switch
    {
        BambuConnectionState.Connected => L("Procentul din Bambu Studio este trimis ceasului.",
            "The percentage shown in Bambu Studio is sent to the clock."),
        BambuConnectionState.StudioClosed => L("Pornește Bambu Studio și deschide Device → Status.",
            "Start Bambu Studio and open Device → Status."),
        BambuConnectionState.NoProgress => L("Deschide Device → Status, selectează imprimanta și verifică dacă imprimă.",
            "Open Device → Status, select the printer and check whether it is printing."),
        BambuConnectionState.ClockUnsupported => L("Firmware-ul ceasului nu are tile-ul Bambu Studio.",
            "The clock firmware does not have the Bambu Studio tile."),
        _ => L("Pornește tile-ul din Manager de tile-uri.", "Enable the tile in Tile Manager."),
    };
}
