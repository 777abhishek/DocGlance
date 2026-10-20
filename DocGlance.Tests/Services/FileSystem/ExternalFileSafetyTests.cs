using DocGlance.Services.FileSystem;

namespace DocGlance.Tests.Services.FileSystem;

public sealed class ExternalFileSafetyTests
{
    [Theory]
    [InlineData(@"C:\documents\run.exe")]
    [InlineData(@"C:\documents\script.PS1")]
    [InlineData(@"C:\documents\open.js")]
    [InlineData(@"C:\documents\shortcut.lnk")]
    [InlineData(@"C:\documents\install.msi")]
    public void IsAllowed_BlocksExecutableAndScriptExtensions(string path)
    {
        ExternalFileSafety.IsAllowed(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"C:\documents\legacy.xls")]
    [InlineData(@"C:\documents\report.pdf")]
    [InlineData(@"C:\documents\unknown.document")]
    public void IsAllowed_AllowsNonExecutableExtensions(string path)
    {
        ExternalFileSafety.IsAllowed(path).Should().BeTrue();
    }
}
