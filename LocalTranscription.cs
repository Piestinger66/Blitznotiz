using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SherpaOnnx;

namespace Blitztext;

/// <summary>
/// Ort und Vollständigkeit des lokalen Parakeet-Ultra-Modells (sherpa-onnx, int8).
/// Liegt in %LOCALAPPDATA% (nicht Roaming), weil es ~670 MB groß ist.
/// </summary>
public static class LocalModel
{
    public static readonly string[] RequiredFiles =
        { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" };

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppBrand.AppDataFolder, "models", "parakeet-ultra-int8");

    public static bool IsComplete(string dir) =>
        Directory.Exists(dir) && Array.TrueForAll(RequiredFiles, f => File.Exists(Path.Combine(dir, f)));

    /// <summary>Was fehlt, samt Windows-Fehlercode – leer = vollständig. Fürs Protokoll, wenn
    /// <see cref="IsComplete"/> überraschend false liefert (z.B. direkt nach der Anmeldung).</summary>
    public static IReadOnlyList<string> Diagnose(string dir)
    {
        if (!Directory.Exists(dir))
            return new[] { $"Ordner nicht erreichbar: {dir} ({Win32Status(dir)})" };
        var problems = new List<string>();
        foreach (var f in RequiredFiles)
        {
            var path = Path.Combine(dir, f);
            if (!File.Exists(path)) problems.Add($"{f} nicht erreichbar ({Win32Status(path)})");
        }
        return problems;
    }

    /// <summary>Prüft wiederholt, bis das Modell auffindbar ist – beim Autostart kann das Dateisystem
    /// in den ersten Sekunden nach der Anmeldung noch nicht bereit sein.</summary>
    public static async Task<bool> WaitUntilCompleteAsync(string dir, int attempts, TimeSpan delay,
        Func<string, bool>? check = null)
    {
        check ??= IsComplete;
        for (int i = 1; i <= attempts; i++)
        {
            if (check(dir)) return true;
            if (i < attempts) await Task.Delay(delay).ConfigureAwait(false);
        }
        return false;
    }

    private static string Win32Status(string path)
    {
        uint attrs = GetFileAttributes(path);
        return attrs == uint.MaxValue ? $"Win32 {Marshal.GetLastWin32Error()}" : $"Win32 ok, Attribute 0x{attrs:X}";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFileAttributesW")]
    private static extern uint GetFileAttributes(string path);
}

/// <summary>Entscheidet, welche Schritte eines Diktats die Cloud (OpenAI/Server) brauchen.</summary>
public static class TranscriptionPlan
{
    /// <summary>Lokal transkribiert braucht nur das Umschreiben noch die Cloud.</summary>
    public static bool NeedsCloud(Settings settings, WorkflowKind kind) =>
        settings.TranscriptionEngine == TranscriptionEngine.Cloud || Workflows.NeedsRewrite(kind);
}

/// <summary>Liest 16-bit-PCM-WAV (wie vom Recorder geschrieben) als float-Samples in [-1, 1].
/// Mehrkanal wird auf den ersten Kanal reduziert.</summary>
public static class WavReader
{
    public static (int rate, float[] samples) Read(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        br.ReadBytes(12); // "RIFF" <größe> "WAVE"
        int rate = 0, channels = 1, bits = 16;
        while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
        {
            var id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            if (id == "fmt ")
            {
                br.ReadInt16(); channels = br.ReadInt16(); rate = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                br.ReadBytes(size - 16);
            }
            else if (id == "data")
            {
                if (bits != 16) throw new NotSupportedException($"Nur 16-bit-WAV unterstützt, nicht {bits} bit.");
                int frames = size / 2 / channels;
                var samples = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    samples[i] = br.ReadInt16() / 32768f;
                    for (int c = 1; c < channels; c++) br.ReadInt16();
                }
                return (rate, samples);
            }
            else br.ReadBytes(size + (size & 1)); // RIFF-Chunks sind auf gerade Länge aufgefüllt
        }
        throw new InvalidDataException("WAV ohne data-Chunk.");
    }
}

/// <summary>
/// Offline-Spracherkennung mit Parakeet Ultra über sherpa-onnx. Das Modell wird einmal geladen
/// (~2 s) und danach wiederverwendet; Decodieren läuft serialisiert auf einem Worker-Thread.
/// </summary>
public static class LocalTranscriber
{
    private static readonly object Gate = new();
    private static OfflineRecognizer? _recognizer;
    private static string? _loadedDir;

    /// <summary>Modell im Hintergrund vorladen, damit das erste Diktat nicht warten muss.</summary>
    public static void Preload(string dir)
    {
        _ = Task.Run(() =>
        {
            try { GetRecognizer(dir); }
            catch (Exception ex) { Logger.Log("Lokales Modell vorladen fehlgeschlagen: " + ex.Message); }
        });
    }

    public static Task<string> TranscribeAsync(string dir, string wavPath, CancellationToken ct) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var (rate, samples) = WavReader.Read(wavPath);
            lock (Gate)
            {
                var recognizer = GetRecognizer(dir);
                ct.ThrowIfCancellationRequested();
                var stream = recognizer.CreateStream();
                stream.AcceptWaveform(rate, samples);
                recognizer.Decode(stream);
                return stream.Result.Text.Trim();
            }
        }, ct);

    private static OfflineRecognizer GetRecognizer(string dir)
    {
        lock (Gate)
        {
            if (_recognizer != null && _loadedDir == dir) return _recognizer;
            if (!LocalModel.IsComplete(dir))
                throw new FileNotFoundException("Lokales Sprachmodell nicht gefunden: " + string.Join("; ", LocalModel.Diagnose(dir)));

            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";

            _recognizer?.Dispose();
            _recognizer = new OfflineRecognizer(config);
            _loadedDir = dir;
            Logger.Log($"Lokales Modell geladen: {dir} (Threads {config.ModelConfig.NumThreads}).");
            return _recognizer;
        }
    }
}
