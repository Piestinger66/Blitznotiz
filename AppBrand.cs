namespace Blitztext;

/// <summary>
/// Zentrale Marken-/Identitäts-Konstanten. Eine saubere "Blitztext"-App — keine Codex-Reste
/// (eigener neuer Mutex, AppData-Ordner schlicht "Blitztext", damit der vorhandene
/// Schlüssel/die settings.json direkt gefunden werden).
/// </summary>
public static class AppBrand
{
    public const string Name = "Blitznotiz";
    public const string ShortName = "Blitznotiz";
    public const string MutexName = "Blitztext_SingleInstance_{1F3C9A4E-7B62-4D08-9C5A-2E1D7F4B6A93}";
    public const string AppDataFolder = "Blitztext";
    public const string AutostartValueName = "Blitztext";
}
