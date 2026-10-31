using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blitztext;

public enum ConnectionMode
{
    OwnKey,  // eigener OpenAI-Schlüssel, direkt an OpenAI
    Server   // eigener Zbox-Server: Serverlink + App-Passwort
}

public enum TranscriptionEngine
{
    Cloud,  // Whisper über OpenAI bzw. eigenen Server
    Local   // Parakeet Ultra offline auf diesem PC
}

public sealed class HotkeyConfig
{
    // Tastennamen wie in KeyNames (z.B. "F9", "Pause", "Insert").
    public string Transcribe { get; set; } = "F9";   // wörtlich
    public string Improve { get; set; } = "F6";       // verbessert/geschrieben (F10 triggert unter Windows Menüs)
    public string Steam { get; set; } = "F8";         // Dampf ablassen
    public string Emoji { get; set; } = "F7";         // mit Emojis
    public string Note { get; set; } = "F11";         // Blitznotiz aufnehmen (gedrückt halten)
    public string Library { get; set; } = "F12";      // Blitznotiz-Bibliothek auf/zu (kurz tippen)
}

public sealed class Settings
{
    public ConnectionMode Mode { get; set; } = ConnectionMode.OwnKey;
    public string ServerUrl { get; set; } = "";
    public string Language { get; set; } = "";             // "" = Auto, sonst z.B. "de", "en"
    public string Tone { get; set; } = "neutral";          // formal | neutral | casual
    public string EmojiDensity { get; set; } = "mittel";   // wenig | mittel | viel
    // Standard zurück auf whisper-1 (gleich schnell wie mini-transcribe, kein Wort-Auslassungs-Risiko);
    // mini/gpt-4o-transcribe bleiben als Auswahl im Einstellungsfenster.
    public string TranscriptionModel { get; set; } = "whisper-1";
    public TranscriptionEngine TranscriptionEngine { get; set; } = TranscriptionEngine.Cloud;
    public string RewriteModel { get; set; } = Workflows.FastEditModel;
    public string CustomTerms { get; set; } = "";
    public string ImproveSystemPrompt { get; set; } = "";
    public string SteamSystemPrompt { get; set; } = "";
    public bool AutoPaste { get; set; } = true;
    public bool StrictPasteTarget { get; set; } = true;
    public bool ClearClipboardAfterPaste { get; set; } = true;
    public int ClipboardClearSeconds { get; set; } = 20;
    public bool LaunchAtLogin { get; set; } = false;
    // Blitznotiz (Phase 4): eigene Ablage + Bibliothek, getrennt vom normalen Diktat.
    public string BlitzNoteRoot { get; set; } = "";           // leer = Standard (Dokumente\Blitznotizen)
    public string BlitzNotePanelSide { get; set; } = "right"; // "right" | "left" – Andock-Seite der Bibliothek
    public bool BlitzNoteImproved { get; set; } = true;       // true = sprachlich verbessert, false = wörtlich
    public HotkeyConfig Hotkeys { get; set; } = new();

    [JsonIgnore]
    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppBrand.AppDataFolder);

    [JsonIgnore]
    public static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return FromJson(File.ReadAllText(FilePath));
        }
        catch
        {
            // Beschädigte Datei: mit Standardwerten weiterarbeiten statt abzustürzen.
        }
        return new Settings();
    }

    public static Settings FromJson(string json) =>
        JsonSerializer.Deserialize<Settings>(json, JsonOpts) ?? new Settings();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, FilePath, overwrite: true);
    }
}
