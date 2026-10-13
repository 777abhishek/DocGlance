using QuickView.Infrastructure.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace QuickView.Tests.Infrastructure.Logging;

public sealed class LogFileWriterTests : IDisposable
{
    private readonly string _dir;

    public LogFileWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qv_logtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string LogPath => Path.Combine(_dir, "app.log");

    [Fact]
    public void Append_CreatesFileAndWritesContent()
    {
        var writer = new LogFileWriter(LogPath, 5 * 1024 * 1024, 5);

        writer.Append("hello\nworld\n");

        Assert.Equal("hello\nworld\n", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Append_ToMissingDirectory_DoesNotThrow()
    {
        var writer = new LogFileWriter(Path.Combine(_dir, "no_such_dir", "app.log"), 1024, 2);

        writer.Append("line\n"); // must swallow the DirectoryNotFoundException
    }

    [Fact]
    public void Append_OverSizeLimit_RotatesToArchive1()
    {
        var writer = new LogFileWriter(LogPath, maxBytes: 64, retainedArchives: 3);
        writer.Append(new string('a', 100) + "\n"); // exceeds 64 bytes → rotates on NEXT append

        writer.Append("after-rotation\n");

        Assert.True(File.Exists(LogPath + ".1"), "archive app.log.1 must exist");
        Assert.Contains("after-rotation", File.ReadAllText(LogPath));
        Assert.Contains("rotated", File.ReadAllText(LogPath)); // fresh header
        Assert.Contains(new string('a', 100), File.ReadAllText(LogPath + ".1"));
    }

    [Fact]
    public void Append_MultipleRotations_ShiftsArchivesAndCapsCount()
    {
        var writer = new LogFileWriter(LogPath, maxBytes: 32, retainedArchives: 2);

        // Each append is > 32 bytes once accumulated, forcing repeated rotations.
        for (int i = 1; i <= 6; i++)
            writer.Append($"batch-number-{i:00} " + new string('x', 60) + "\n");

        Assert.True(File.Exists(LogPath + ".1"));
        Assert.True(File.Exists(LogPath + ".2"));
        Assert.False(File.Exists(LogPath + ".3"), "archive beyond the retained count must be deleted");
    }

    [Fact]
    public async Task Append_ConcurrentWrites_AllLinesSurvive()
    {
        var writer = new LogFileWriter(LogPath, 5 * 1024 * 1024, 5);
        const int threads = 8, perThread = 200;

        var tasks = Enumerable.Range(0, threads)
            .Select(t => Task.Run(() =>
            {
                for (int i = 0; i < perThread; i++)
                    writer.Append($"thread={t} i={i}\n");
            }));

        await Task.WhenAll(tasks);

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(threads * perThread, lines.Length);
        Assert.Contains(lines, l => l == "thread=0 i=0");
        Assert.Contains(lines, l => l == $"thread={threads - 1} i={perThread - 1}");
    }
}

public sealed class LoggerFormatTests
{
    [Fact]
    public void FormatException_IncludesTypeMessageAndInnerChain()
    {
        var inner = new InvalidOperationException("root cause");
        var middle = new ArgumentException("middle", inner);
        var outer = new Exception("outer", middle);

        var result = Logger.FormatException(outer);

        Assert.Contains("System.Exception", result);
        Assert.Contains("outer", result);
        Assert.Contains("---> Inner:", result);
        Assert.Contains("System.ArgumentException", result);
        Assert.Contains("System.InvalidOperationException", result);
        Assert.Contains("root cause", result);
        Assert.Contains("HResult=0x", result);
    }

    [Fact]
    public void FormatException_TruncatesDeepChains()
    {
        Exception root = new Exception("level0");
        for (int i = 1; i < 10; i++)
            root = new Exception($"level{i}", root);

        var result = Logger.FormatException(root);

        // Chain cap is 6: levels 9..4 printed, level 3 and below truncated.
        Assert.Contains("(more inner exceptions truncated)", result);
        Assert.Contains("level4", result);
        Assert.DoesNotContain("level3", result);
    }

    [Fact]
    public void SanitizePath_ReplacesUsernameSegment()
    {
        var result = Logger.SanitizePath(@"opened C:\Users\JohnDoe\Documents\file.pdf ok");

        Assert.Equal(@"opened C:\Users\***\Documents\file.pdf ok", result);
        Assert.DoesNotContain("JohnDoe", result);
    }

    [Fact]
    public void SanitizePath_LeavesNonUserPathsUntouched()
    {
        var result = Logger.SanitizePath(@"C:\Program Files\QuickView\file.txt");

        Assert.Equal(@"C:\Program Files\QuickView\file.txt", result);
    }
}
