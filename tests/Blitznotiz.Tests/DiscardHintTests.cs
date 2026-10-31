using Blitztext;
using Xunit;

namespace Blitznotiz.Tests;

public class DiscardHintTests
{
    [Fact]
    public void TooShortAsksToHoldKeyLonger()
    {
        var hint = DiscardHint.For(DiscardReason.TooShort, peak: 0.5f);
        Assert.Contains("zu kurz", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NothingRecognizedWithSilenceAsksToCheckMicrophone()
    {
        var hint = DiscardHint.For(DiscardReason.NothingRecognized, peak: 0.005f);
        Assert.Contains("Mikrofon", hint);
    }

    [Fact]
    public void NothingRecognizedWithSoundAsksToSpeakLonger()
    {
        var hint = DiscardHint.For(DiscardReason.NothingRecognized, peak: 0.3f);
        Assert.Contains("Nichts erkannt", hint);
        Assert.DoesNotContain("Mikrofon", hint);
    }
}

public class PeakLevelTests
{
    [Fact]
    public void PeakOfPcm16IsLargestAbsoluteSample()
    {
        // little-endian 16 bit: 0, 16384, -32768
        var buffer = new byte[] { 0x00, 0x00, 0x00, 0x40, 0x00, 0x80 };
        Assert.Equal(1f, Recorder.PeakOf(buffer, buffer.Length));
        Assert.Equal(0.5f, Recorder.PeakOf(buffer, 4));
    }
}
