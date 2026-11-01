using Blitztext;
using Xunit;

namespace Blitznotiz.Tests;

public class LocalModelDiagnoseTests
{
    [Fact]
    public void ReportsMissingFolderWithWin32Code()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bn-fehlt-{Guid.NewGuid():N}");
        var problems = LocalModel.Diagnose(dir);
        var problem = Assert.Single(problems);
        Assert.Contains("Ordner", problem);
        Assert.Contains("Win32", problem);
    }

    [Fact]
    public void ReportsEachMissingFileAndNothingWhenComplete()
    {
        var dir = Directory.CreateTempSubdirectory("bn-model-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "tokens.txt"), "x");
            var problems = LocalModel.Diagnose(dir);
            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, p => p.Contains("encoder.int8.onnx"));
            Assert.DoesNotContain(problems, p => p.Contains("tokens.txt"));

            foreach (var f in LocalModel.RequiredFiles) File.WriteAllText(Path.Combine(dir, f), "x");
            Assert.Empty(LocalModel.Diagnose(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class LocalModelWaitTests
{
    [Fact]
    public async Task ReturnsAsSoonAsModelAppears()
    {
        int calls = 0;
        bool found = await LocalModel.WaitUntilCompleteAsync("x", attempts: 10, TimeSpan.FromMilliseconds(1),
            check: _ => ++calls >= 3);
        Assert.True(found);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task GivesUpAfterAllAttempts()
    {
        int calls = 0;
        bool found = await LocalModel.WaitUntilCompleteAsync("x", attempts: 4, TimeSpan.FromMilliseconds(1),
            check: _ => { calls++; return false; });
        Assert.False(found);
        Assert.Equal(4, calls);
    }
}
