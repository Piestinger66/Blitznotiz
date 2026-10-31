using System.Text.Json;
using Blitztext;
using Xunit;

namespace Blitznotiz.Tests;

public class WavReaderTests
{
    [Fact]
    public void ReadsMono16BitPcmAsFloats()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bn-test-{Guid.NewGuid():N}.wav");
        try
        {
            WriteWav(path, 16000, new short[] { 0, 16384, -32768, 32767 });

            var (rate, samples) = WavReader.Read(path);

            Assert.Equal(16000, rate);
            Assert.Equal(new[] { 0f, 0.5f, -1f, 32767f / 32768f }, samples);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsNon16BitAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bn-test-{Guid.NewGuid():N}.wav");
        try
        {
            WriteWav(path, 16000, new short[] { 1, 2 }, bitsOverride: 8);
            Assert.Throws<NotSupportedException>(() => WavReader.Read(path));
        }
        finally { File.Delete(path); }
    }

    private static void WriteWav(string path, int rate, short[] data, short bitsOverride = 16)
    {
        using var bw = new BinaryWriter(File.Create(path));
        int dataBytes = data.Length * 2;
        bw.Write("RIFF"u8); bw.Write(36 + dataBytes); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write(bitsOverride);
        bw.Write("data"u8); bw.Write(dataBytes);
        foreach (var s in data) bw.Write(s);
    }
}

public class LocalModelTests
{
    [Fact]
    public void IsCompleteOnlyWhenAllModelFilesExist()
    {
        var dir = Directory.CreateTempSubdirectory("bn-model-").FullName;
        try
        {
            Assert.False(LocalModel.IsComplete(dir));
            foreach (var f in LocalModel.RequiredFiles.Skip(1)) File.WriteAllText(Path.Combine(dir, f), "x");
            Assert.False(LocalModel.IsComplete(dir));
            File.WriteAllText(Path.Combine(dir, LocalModel.RequiredFiles[0]), "x");
            Assert.True(LocalModel.IsComplete(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DefaultDirLivesInLocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(local, LocalModel.DefaultDir);
        Assert.EndsWith("parakeet-ultra-int8", LocalModel.DefaultDir);
    }
}

public class TranscriptionPlanTests
{
    [Theory]
    [InlineData(TranscriptionEngine.Cloud, WorkflowKind.Transcribe, true)]
    [InlineData(TranscriptionEngine.Cloud, WorkflowKind.Improve, true)]
    [InlineData(TranscriptionEngine.Local, WorkflowKind.Transcribe, false)]
    [InlineData(TranscriptionEngine.Local, WorkflowKind.Improve, true)]
    [InlineData(TranscriptionEngine.Local, WorkflowKind.Steam, true)]
    [InlineData(TranscriptionEngine.Local, WorkflowKind.Emoji, true)]
    public void NeedsCloudOnlyForCloudEngineOrRewrite(TranscriptionEngine engine, WorkflowKind kind, bool expected)
    {
        var s = new Settings { TranscriptionEngine = engine };
        Assert.Equal(expected, TranscriptionPlan.NeedsCloud(s, kind));
    }

    [Fact]
    public void OldSettingsWithoutEngineDefaultToCloud()
    {
        var s = Settings.FromJson("{\"Mode\":\"OwnKey\",\"TranscriptionModel\":\"whisper-1\"}");
        Assert.Equal(TranscriptionEngine.Cloud, s.TranscriptionEngine);
    }

    [Fact]
    public void EngineIsStoredAsReadableString()
    {
        var json = new Settings { TranscriptionEngine = TranscriptionEngine.Local }.ToJson();
        Assert.Contains("\"TranscriptionEngine\": \"Local\"", json);
        Assert.Equal(TranscriptionEngine.Local, Settings.FromJson(json).TranscriptionEngine);
    }
}

public class LocalTranscriberTests
{
    // Echter Lauf gegen das installierte Modell; ohne Modell nichts zu prüfen.
    [Fact]
    public async Task TranscribesGermanSample()
    {
        if (!LocalModel.IsComplete(LocalModel.DefaultDir)) return;

        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "de.wav");
        var text = await LocalTranscriber.TranscribeAsync(LocalModel.DefaultDir, wav, CancellationToken.None);

        Assert.Contains("Wurst", text);
    }
}
