using System.Text;

namespace Blitztext;

public enum WorkflowKind
{
    Transcribe, // wörtlich, nur Whisper
    Improve,    // Whisper + Umformulieren (Lektor)
    Steam,      // Whisper + "Dampf ablassen" -> sachliche Nachricht
    Emoji       // Whisper + Emojis einfügen
}

/// <summary>
/// System-Prompts und Modell-/Temperatur-Zuordnung – 1:1 aus der originalen Blitztext-App
/// (reine Texte, plattformneutral).
/// </summary>
public static class Workflows
{
    public const string DefaultTranscriptionModel = "gpt-4o-mini-transcribe"; // schnelle Option (nicht Default)
    public const string FallbackTranscriptionModel = "whisper-1";
    public const string TranscriptionModel = FallbackTranscriptionModel;       // sicherer Default
    public const string FastEditModel = "gpt-4o-mini"; // Standard-Umschreibmodell für alle Rewrite-Workflows

    public static bool NeedsRewrite(WorkflowKind kind) => kind != WorkflowKind.Transcribe;

    /// <summary>Nur die Temperatur ist je Workflow verschieden; das Modell ist eine einzige
    /// Einstellung (Settings.RewriteModel). Das beseitigt die frühere tote "RageModel"-Sonderbehandlung.</summary>
    public static double RewriteTemperature(WorkflowKind kind) => kind switch
    {
        WorkflowKind.Steam => 0.4,
        _ => 0.3,
    };

    public static string DisplayName(WorkflowKind kind) => kind switch
    {
        WorkflowKind.Transcribe => "Diktat (wörtlich)",
        WorkflowKind.Improve => "Diktat+ (geschrieben)",
        WorkflowKind.Steam => "Diktat (Dampf ablassen)",
        WorkflowKind.Emoji => "Diktat (Emoji)",
        _ => kind.ToString()
    };

    public static string BuildSystemPrompt(WorkflowKind kind, Settings s)
    {
        return kind switch
        {
            WorkflowKind.Improve => BuildImprovePrompt(s),
            WorkflowKind.Emoji => BuildEmojiPrompt(s),
            WorkflowKind.Steam => string.IsNullOrWhiteSpace(s.SteamSystemPrompt)
                ? DefaultSteamPrompt : s.SteamSystemPrompt,
            _ => ""
        };
    }

    private const string DefaultSteamPrompt =
        "Du erhältst ein emotional gesprochenes Transkript. Erkenne zuerst das eigentliche Ziel, " +
        "Anliegen und den wahren Frust der Person. Formuliere daraus eine klare, respektvolle und " +
        "wirksame Nachricht, mit der die Person ihr Ziel eher erreicht. Bewahre relevante Fakten, " +
        "konkrete Probleme, Grenzen, Erwartungen und die nötige Dringlichkeit. Entferne Beleidigungen, " +
        "Drohungen, Sarkasmus, Unterstellungen und unnötige Eskalation. Wenn mehrere Vorwürfe genannt " +
        "werden, verdichte sie auf die entscheidenden Kernpunkte. Der Ton soll ruhig, menschlich, " +
        "bestimmt und lösungsorientiert sein. Gib NUR die fertige Nachricht zurück.";

    private static string BuildImprovePrompt(Settings s)
    {
        if (!string.IsNullOrWhiteSpace(s.ImproveSystemPrompt))
            return WithTerms(s.ImproveSystemPrompt, s.CustomTerms);

        var sb = new StringBuilder();
        sb.Append("Du bist ein Lektor und Schreibassistent. Verbessere den folgenden Text:\n");
        sb.Append("- Korrigiere Rechtschreibung und Grammatik\n");
        sb.Append("- Verbessere die Formulierung und den Lesefluss\n");
        sb.Append("- Behalte die ursprüngliche Bedeutung bei\n");
        sb.Append("- Gib NUR den verbesserten Text zurück, keine Erklärungen");
        sb.Append(s.Tone switch
        {
            "formal" => "\n- Verwende einen formellen, professionellen Ton",
            "casual" => "\n- Verwende einen lockeren, natürlichen Ton",
            _ => "\n- Verwende einen neutralen, klaren Ton",
        });
        return WithTerms(sb.ToString(), s.CustomTerms);
    }

    private static string BuildEmojiPrompt(Settings s)
    {
        var density = s.EmojiDensity switch
        {
            "wenig" => "Setze nur vereinzelt Emojis ein, maximal 1-2 pro Absatz.",
            "viel" => "Setze großzügig Emojis ein, gerne mehrere pro Satz.",
            _ => "Setze regelmäßig passende Emojis ein, etwa alle 1-2 Sätze.",
        };
        return "Du erhältst ein gesprochenes Transkript. Gib den Text möglichst originalgetreu zurück, " +
            "aber füge passende Emojis ein. " + density + " Korrigiere offensichtliche Sprach- und " +
            "Grammatikfehler. Behalte den Stil und die Bedeutung bei. Gib NUR den Text mit Emojis " +
            "zurück, keine Erklärungen.";
    }

    private static string WithTerms(string prompt, string customTerms)
    {
        if (string.IsNullOrWhiteSpace(customTerms)) return prompt;
        return prompt + "\n\nWichtig: Diese Eigennamen und Fachbegriffe müssen exakt so geschrieben werden: "
            + customTerms.Trim();
    }

    public static string? TranscriptionPrompt(Settings s)
    {
        if (string.IsNullOrWhiteSpace(s.CustomTerms)) return null;
        return "Eigennamen und Begriffe: " + s.CustomTerms.Trim();
    }
}
