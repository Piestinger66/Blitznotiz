namespace Blitztext;

public enum DiscardReason
{
    TooShort,           // Taste kürzer als die Mindestdauer gehalten
    NothingRecognized   // Erkennung lieferte keinen (brauchbaren) Text
}

/// <summary>
/// Kurzer Hinweis für den Nutzer, wenn ein Diktat verworfen wird – vorher geschah das still und
/// sah aus, als hätte die App nicht reagiert.
/// </summary>
public static class DiscardHint
{
    /// <summary>Unter diesem Spitzenpegel (~ -34 dBFS) war praktisch nur Stille auf der Aufnahme.</summary>
    public const float SilencePeak = 0.02f;

    public static string For(DiscardReason reason, float peak) => reason switch
    {
        DiscardReason.TooShort => "Aufnahme zu kurz – Taste beim Sprechen gedrückt halten.",
        _ when peak < SilencePeak => "Kein Ton aufgenommen – Mikrofon prüfen.",
        _ => "Nichts erkannt – Taste erst drücken, dann sprechen, nach dem letzten Wort loslassen.",
    };
}
