using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Blitztext;

/// <summary>
/// Mikrofon-Aufnahme nach WAV (16 kHz, mono, 16 bit) – das Format, das Whisper erwartet.
/// DataAvailable feuert auf einem NAudio-Worker-Thread, RecordingStopped/Cancel auf dem
/// UI-Thread; der Zugriff auf _writer ist deshalb durch _writerLock serialisiert.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly object _writerLock = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    private long _bytesWritten;
    private TaskCompletionSource<bool>? _stopped;

    public float PeakLevel { get; private set; }
    /// <summary>Lautester Wert der ganzen Aufnahme (0..1) – zeigt, ob das Mikrofon überhaupt Ton hatte.</summary>
    public float MaxPeak { get; private set; }
    public bool IsRecording => _waveIn != null;

    public static string TempDir => Path.Combine(Path.GetTempPath(), "Blitztext");

    public static void SweepOldTempFiles()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var file in Directory.EnumerateFiles(TempDir, "blitztext-*.wav"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    public void Start()
    {
        Directory.CreateDirectory(TempDir);
        _path = Path.Combine(TempDir, $"blitztext-{Guid.NewGuid():N}.wav");
        _bytesWritten = 0;
        PeakLevel = 0;
        MaxPeak = 0;

        try
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };
            _writer = new WaveFileWriter(_path, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
        }
        catch
        {
            lock (_writerLock) { _writer?.Dispose(); _writer = null; }
            _waveIn?.Dispose();
            _waveIn = null;
            DeleteFile();
            throw;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            lock (_writerLock)
            {
                if (_writer == null) return;
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
                _bytesWritten += e.BytesRecorded;
            }
        }
        catch { return; }

        float max = PeakOf(e.Buffer, e.BytesRecorded);
        PeakLevel = max;
        if (max > MaxPeak) MaxPeak = max;
    }

    /// <summary>Größter Betrag in 16-bit-PCM (little-endian), normiert auf 0..1.</summary>
    public static float PeakOf(byte[] buffer, int bytes)
    {
        float max = 0f;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            float abs = Math.Abs((int)sample) / 32768f; // int: Math.Abs(short.MinValue) würfe OverflowException
            if (abs > max) max = abs;
        }
        return max;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            lock (_writerLock)
            {
                _writer?.Dispose();
                _writer = null;
            }
            _waveIn?.Dispose();
            _waveIn = null;
        }
        finally
        {
            _stopped?.TrySetResult(true);
        }
    }

    public async Task<(string path, double seconds)> StopAsync()
    {
        if (_waveIn == null)
            return (_path ?? "", SecondsFromBytes(_bytesWritten));

        _stopped = new TaskCompletionSource<bool>();
        _waveIn.StopRecording();
        await _stopped.Task.ConfigureAwait(true);
        double seconds = SecondsFromBytes(_bytesWritten);
        return (_path ?? "", seconds);
    }

    public void Cancel()
    {
        try
        {
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.StopRecording();
            }
        }
        catch { }
        finally
        {
            lock (_writerLock) { _writer?.Dispose(); _writer = null; }
            _waveIn?.Dispose();
            _waveIn = null;
        }
        DeleteFile();
    }

    public void DeleteFile()
    {
        if (_path != null && File.Exists(_path))
        {
            try { File.Delete(_path); } catch { }
        }
        _path = null;
    }

    private static double SecondsFromBytes(long bytes) => bytes / (16000.0 * 2.0);

    public void Dispose()
    {
        lock (_writerLock) { _writer?.Dispose(); _writer = null; }
        _waveIn?.Dispose();
        _waveIn = null;
        DeleteFile();
    }
}
