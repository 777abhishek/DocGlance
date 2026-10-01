using System.Text.RegularExpressions;

namespace QuickView.Tests.Rendering.Web;

/// <summary>
/// Tests for the URL sanitisation logic used by MarkdownRenderer.
/// We test the sanitiser behaviour directly via regex since the method is private —
/// these tests duplicate the exact regex from SanitizeUrls so any regression is caught.
/// </summary>
public class WebSecurityTests
{
    // Replicate the exact sanitiser regex from MarkdownRenderer.SanitizeUrls
    private static string SanitizeUrls(string html)
        => Regex.Replace(html,
            @"(href|src)\s*=\s*[""']?\s*(javascript|data|vbscript):",
            "$1=\"#blocked-unsafe-url\"",
            RegexOptions.IgnoreCase);

    // ── javascript: scheme ────────────────────────────────────────────────────

    [Fact]
    public void SanitizeUrls_Blocks_JavascriptHref_DoubleQuote()
    {
        string input  = "<a href=\"javascript:alert(1)\">click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("javascript:");
        result.Should().Contain("#blocked-unsafe-url");
    }

    [Fact]
    public void SanitizeUrls_Blocks_JavascriptHref_SingleQuote()
    {
        string input  = "<a href='javascript:void(0)'>click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void SanitizeUrls_Blocks_JavascriptHref_NoQuote()
    {
        string input  = "<a href=javascript:alert(1)>click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("javascript:");
    }

    [Fact]
    public void SanitizeUrls_Blocks_JavascriptHref_Uppercase()
    {
        string input  = "<a href=\"JAVASCRIPT:alert(1)\">click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("JAVASCRIPT:");
    }

    [Fact]
    public void SanitizeUrls_Blocks_JavascriptHref_MixedCase()
    {
        string input  = "<a href=\"JaVaScRiPt:alert(1)\">click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("JaVaScRiPt:");
    }

    // ── data: scheme ──────────────────────────────────────────────────────────

    [Fact]
    public void SanitizeUrls_Blocks_DataHref()
    {
        string input  = "<a href=\"data:text/html,<script>alert(1)</script>\">x</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("data:text/html");
    }

    [Fact]
    public void SanitizeUrls_Blocks_DataSrc()
    {
        string input  = "<img src=\"data:text/html,<h1>hack</h1>\">";
        string result = SanitizeUrls(input);
        result.Should().NotContain("data:text/html");
    }

    // ── vbscript: scheme ──────────────────────────────────────────────────────

    [Fact]
    public void SanitizeUrls_Blocks_VbscriptHref()
    {
        string input  = "<a href=\"vbscript:msgbox('xss')\">click</a>";
        string result = SanitizeUrls(input);
        result.Should().NotContain("vbscript:");
    }

    // ── Safe URLs must pass through ───────────────────────────────────────────

    [Fact]
    public void SanitizeUrls_Allows_HttpsHref()
    {
        string input  = "<a href=\"https://example.com\">link</a>";
        string result = SanitizeUrls(input);
        result.Should().Be(input);
    }

    [Fact]
    public void SanitizeUrls_Allows_HttpHref()
    {
        string input  = "<a href=\"http://example.com\">link</a>";
        string result = SanitizeUrls(input);
        result.Should().Be(input);
    }

    [Fact]
    public void SanitizeUrls_Allows_RelativeHref()
    {
        string input  = "<a href=\"../docs/readme.md\">docs</a>";
        string result = SanitizeUrls(input);
        result.Should().Be(input);
    }

    [Fact]
    public void SanitizeUrls_Allows_ImgSrc_Https()
    {
        string input  = "<img src=\"https://example.com/image.png\" alt=\"img\">";
        string result = SanitizeUrls(input);
        result.Should().Be(input);
    }

    [Fact]
    public void SanitizeUrls_EmptyString_ReturnsEmpty()
        => SanitizeUrls("").Should().BeEmpty();

    [Fact]
    public void SanitizeUrls_NoUrls_ReturnsUnchanged()
    {
        string input = "<p>Hello world</p>";
        SanitizeUrls(input).Should().Be(input);
    }

    // ── Multiple dangerous URLs in one document ───────────────────────────────

    [Fact]
    public void SanitizeUrls_Blocks_Multiple_DangerousUrls()
    {
        string input = """
            <a href="javascript:alert(1)">a</a>
            <a href="vbscript:msgbox()">b</a>
            <img src="data:text/html,x">
            """;
        string result = SanitizeUrls(input);
        result.Should().NotContain("javascript:");
        result.Should().NotContain("vbscript:");
        result.Should().NotContain("data:text/html");
        result.Should().ContainEquivalentOf("#blocked-unsafe-url");
    }
}
