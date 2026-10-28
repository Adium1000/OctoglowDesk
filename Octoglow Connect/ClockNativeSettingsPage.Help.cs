using Microsoft.UI.Xaml.Controls;

namespace OctoglowSender;

// Native version of the firmware's Help Manager. Topics are pages, rather
// than small nested dialogs, so they remain comfortable at every window size.
public sealed partial class ClockNativeSettingsPage
{
    private void BuildHelpManager()
    {
        AddSection(L("Subiecte de ajutor", "Help topics"));
        Add(HelpTopicCard("\uE724", "Octoglow Connect",
            L("Cum trimiteți conținut către dispozitive", "How content is sent to your devices"), "connect"));
        Add(HelpTopicCard("\uEC05", "Access Point",
            L("Conectare și configurare fără router", "Connecting and setting up without a router"), "ap"));
        Add(HelpTopicCard("\uE774", L("Limbi", "Languages"),
            L("Limba interfeței și limbile tile-urilor", "The interface language and tile languages"), "language"));
    }

    private Button HelpTopicCard(string glyph, string title, string description, string topic) =>
        NavCard(glyph, title, description,
            () => OpenLocalSubPage(title, description, () => BuildHelpTopic(topic)));

    private Action? BuildHelpTopic(string topic)
    {
        switch (topic)
        {
            case "connect":
                AddSection("Octoglow Connect");
                Add(SettingsCard("\uE724", L("Un singur loc pentru dispozitivele Octoglow", "One place for your Octoglow devices"),
                    L("Aplicația configurează dispozitivele Octoglow și trimite către ceas datele care există numai pe calculator: redare media, notificări, descărcări, telemetrie din jocuri și starea aplicațiilor integrate.",
                      "The app configures Octoglow devices and sends the clock data that exists only on your PC: media playback, notifications, downloads, game telemetry and integrated app status.")));
                Add(SettingsCard("\uE753", L("Conectare sigură", "Secure connection"),
                    L("Adăugați ceasul din Home și autentificați-vă cu utilizatorul și parola lui. Doar dispozitivele aprobate apar apoi în Home și în bara laterală.",
                      "Add the clock from Home and sign in with its username and password. Only approved devices then appear in Home and the sidebar.")));
                Add(SettingsCard("\uE713", L("Integrările se controlează separat", "Integrations are controlled separately"),
                    L("Activați numai integrările de care aveți nevoie din fila Integrări. Datele sunt procesate local și către ceas se trimit doar valorile necesare tile-ului.",
                      "Enable only the integrations you need from the Integrations tab. Data is processed locally and only the values needed by the tile are sent to the clock.")));
                break;
            case "ap":
                AddSection("Access Point");
                Add(SettingsCard("\uEC05", L("Rețeaua proprie a dispozitivului", "The device's own network"),
                    L("În modul Access Point, ceasul creează propria rețea Wi-Fi. Conectați calculatorul sau telefonul la ea și deschideți adresa afișată de ceas pentru configurare.",
                      "In Access Point mode, the clock creates its own Wi-Fi network. Connect your PC or phone to it and open the address shown by the clock to configure it.")));
                Add(SettingsCard("\uE701", L("Revenirea la Wi-Fi", "Returning to Wi-Fi"),
                    L("Alegeți o rețea din pagina WiFi sau țineți apăsat senzorul tactil 2 secunde. Tile-urile care au nevoie de internet pot fi indisponibile cât timp ceasul rămâne în Access Point.",
                      "Choose a network from the WiFi page or hold the touch sensor for 2 seconds. Tiles that need internet may be unavailable while the clock remains in Access Point mode.")));
                break;
            default:
                AddSection(L("Limbi", "Languages"));
                Add(SettingsCard("\uE774", L("Interfața ceasului", "Clock interface"),
                    L("Managerul de limbi schimbă limba interfeței web, a datei și a informațiilor meteo. Limba aplicației Octoglow Connect se alege separat din Setările aplicației.",
                      "Language Manager changes the web interface, date and weather languages. The Octoglow Connect app language is selected separately in the app Settings.")));
                Add(SettingsCard("\uE8C1", L("Limba fiecărui tile", "Each tile's language"),
                    L("Tile-urile care acceptă mai multe limbi au propriul selector în setările lor. Dacă un tile nu are selector, folosește limba generală sau nu conține text tradus.",
                      "Tiles that support several languages have their own selector in their settings. If a tile has no selector, it uses the general language or contains no translated text.")));
                break;
        }
        return null;
    }
}
