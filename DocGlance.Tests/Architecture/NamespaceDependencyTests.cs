using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DocGlance.Tests.Architecture;

/// <summary>
/// Architectural dependency tests enforcing the layer boundaries documented in
/// docs/architecture/08-dependency-and-interop-rules.md §1 and §2.
/// </summary>
public sealed class NamespaceDependencyTests
{
    private static readonly string RepoRoot = GetRepositoryRoot();

    private static string GetRepositoryRoot()
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "DocGlance.csproj")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? throw new InvalidOperationException("Could not locate DocGlance repository root.");
    }

    private static IEnumerable<string> GetSourceFiles(string folder)
    {
        string path = Path.Combine(RepoRoot, folder);
        if (!Directory.Exists(path))
            return Enumerable.Empty<string>();

        return Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"));
    }

    private static List<string> FindForbiddenReferences(IEnumerable<string> files, string[] forbiddenPatterns)
    {
        var violations = new List<string>();

        foreach (string file in files)
        {
            string relativePath = Path.GetRelativePath(RepoRoot, file);
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*"))
                    continue;

                foreach (string pattern in forbiddenPatterns)
                {
                    if (Regex.IsMatch(line, pattern))
                    {
                        violations.Add($"{relativePath}:{i + 1} -> {line}");
                    }
                }
            }
        }

        return violations;
    }

    [Fact]
    public void CommonAndCore_MustNotReferenceForbiddenLayers()
    {
        // Core & Common may reference each other, but not higher layers or XAML controls
        var files = GetSourceFiles("Common").Concat(GetSourceFiles("Core"));
        var forbidden = new[]
        {
            @"\busing\s+DocGlance\.Rendering\b",
            @"\busing\s+DocGlance\.Interop\b",
            @"\busing\s+DocGlance\.Features\b",
            @"\busing\s+DocGlance\.Services\b",
            @"\busing\s+DocGlance\.UI\b",
            @"\busing\s+Microsoft\.UI\.Xaml\.Controls\b",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Common and Core must not depend on Rendering, Interop, Features, Services, UI, or XAML controls.");
    }

    [Fact]
    public void Infrastructure_MustNotReferenceForbiddenLayers()
    {
        // Infrastructure may reference Common and Core, but not XAML controls, Features, Rendering, or Interop
        var files = GetSourceFiles("Infrastructure");
        var forbidden = new[]
        {
            @"\busing\s+Microsoft\.UI\.Xaml\.Controls\b",
            @"\busing\s+DocGlance\.Features\b",
            @"\busing\s+DocGlance\.Rendering\b",
            @"\busing\s+DocGlance\.Interop\b",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Infrastructure must not depend on XAML controls, Features, Rendering, or Interop.");
    }

    [Fact]
    public void Interop_MustNotReferenceHigherLayersOrControls()
    {
        // Interop may only reference Infrastructure and Core models
        var files = GetSourceFiles("Interop");
        var forbidden = new[]
        {
            @"\busing\s+Microsoft\.UI\.Xaml\.Controls\b",
            @"\busing\s+DocGlance\.Features\b",
            @"\busing\s+DocGlance\.Services\b",
            @"\busing\s+DocGlance\.UI\b",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Interop must not depend on XAML controls, Features, Services, or UI.");
    }

    [Fact]
    public void Rendering_MustNotReferenceXAMLControlsOrFeaturesOrServices()
    {
        // Rendering must not reference WinUI XAML controls, Features, or Services
        var files = GetSourceFiles("Rendering");
        var forbidden = new[]
        {
            @"\busing\s+Microsoft\.UI\.Xaml\.Controls\b",
            @"\busing\s+DocGlance\.Features\b",
            @"\busing\s+DocGlance\.Services\b",
            @"\busing\s+DocGlance\.UI\b",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Rendering must not depend on XAML controls, Features, Services, or UI.");
    }

    [Fact]
    public void Services_MustNotReferenceXAMLControls()
    {
        // Services must contain no XAML controls (UpdateBanner was moved to UI/Controls in Phase 3)
        var files = GetSourceFiles("Services");
        var forbidden = new[]
        {
            @"\busing\s+Microsoft\.UI\.Xaml\.Controls\b",
            @"\busing\s+DocGlance\.UI\.Controls\b",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Services must not contain XAML control imports (UI controls belong in UI/Controls).");
    }

    [Fact]
    public void FeaturesAndUI_MustNotReferenceInteropDirectlyExceptSanctioned()
    {
        // Rule §2.4: Pages, viewers, and UI shell must not call Interop/* directly — they must use Rendering engines.
        // Documented exceptions: ToolsPage (sanctioned PDF tool operations), DocxViewer & PptxViewer (renderer availability checks).
        var files = GetSourceFiles("Features").Concat(GetSourceFiles("UI"))
            .Where(f => !f.EndsWith("ToolsPage.xaml.cs") &&
                        !f.EndsWith("DocxViewer.xaml.cs") &&
                        !f.EndsWith("PptxViewer.xaml.cs"));

        var forbidden = new[]
        {
            @"\busing\s+DocGlance\.Interop\b",
            @"\bDocGlance\.Interop\.",
        };

        var violations = FindForbiddenReferences(files, forbidden);
        violations.Should().BeEmpty("Features and UI must not bypass Rendering engines to reference Interop directly.");
    }

    [Fact]
    public void InteropConsumers_MustOnlyBeRenderingTranslationAndSanctionedTools()
    {
        // Sanctioned consumers of Interop: Rendering engines, Services/Translation,
        // ToolsPage (PDF ops), and DocxViewer/PptxViewer (native availability checks).
        var allSourceFiles = Directory.GetFiles(RepoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\") && !f.Contains(@"\DocGlance.Tests\") && !f.Contains(@"\tools\"));

        var forbidden = new[]
        {
            @"\busing\s+DocGlance\.Interop\b",
        };

        var violations = new List<string>();

        foreach (string file in allSourceFiles)
        {
            string relativePath = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (relativePath.StartsWith("Interop/") ||
                relativePath.StartsWith("Rendering/") ||
                relativePath.StartsWith("Services/Translation/") ||
                relativePath == "Features/Tools/ToolsPage.xaml.cs" ||
                relativePath == "Features/Viewer/Formats/Docx/DocxViewer.xaml.cs" ||
                relativePath == "Features/Viewer/Formats/Pptx/PptxViewer.xaml.cs")
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*"))
                    continue;

                foreach (string pattern in forbidden)
                {
                    if (Regex.IsMatch(line, pattern))
                    {
                        violations.Add($"{relativePath}:{i + 1} -> {line}");
                    }
                }
            }
        }

        violations.Should().BeEmpty("Only Rendering, Services/Translation, ToolsPage, and format availability guards are sanctioned consumers of DocGlance.Interop.");
    }
}
