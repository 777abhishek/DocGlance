using QuickView.Features.Home;
using QuickView.Core.Models;

namespace QuickView.Tests.Features.Home;

public class HomeViewModelTests
{
    private static DocumentItem MakeDoc(string title, string fileType, long sizeBytes = 1024,
        string folder = @"C:\Docs\") => new()
    {
        FilePath      = $@"{folder}{title}.{fileType.ToLower()}",
        Title         = title,
        FileType      = fileType,
        FileSizeBytes = sizeBytes,
        LastWriteTime = DateTime.Now,
    };

    // ── Default State ─────────────────────────────────────────────────────────

    [Fact]
    public void Default_ShowingRecent_IsTrue()
        => new HomeViewModel().ShowingRecent.Should().BeTrue();

    [Fact]
    public void Default_SortDescending_IsFalse()
        => new HomeViewModel().SortDescending.Should().BeFalse();

    [Fact]
    public void Default_Collections_AreEmpty()
    {
        var vm = new HomeViewModel();
        vm.RecentDocuments.Should().BeEmpty();
        vm.FilteredDocuments.Should().BeEmpty();
        vm.ScannedFolders.Should().BeEmpty();
    }

    // ── RefreshFilter — Recent Mode ───────────────────────────────────────────

    [Fact]
    public void RefreshFilter_RecentMode_ShowsRecentDocuments()
    {
        var vm = new HomeViewModel();
        vm.RecentDocuments.Add(MakeDoc("Report", "PDF"));
        vm.RecentDocuments.Add(MakeDoc("Notes", "TXT"));
        vm.ShowingRecent = true;

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(2);
    }

    [Fact]
    public void RefreshFilter_RecentMode_DoesNotShowIndexDocuments()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("IndexedFile", "PDF"));
        vm.ShowingRecent = true;

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().BeEmpty();
    }

    // ── RefreshFilter — All Mode ──────────────────────────────────────────────

    [Fact]
    public void RefreshFilter_AllMode_ShowsBackgroundIndex()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Doc1", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Doc2", "DOCX"));
        vm.ShowingRecent = false;

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(2);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshFilter_Search_FiltersByTitle()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Annual Report", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Meeting Notes", "DOCX"));
        vm.ShowingRecent = false;
        vm.SearchText    = "annual";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(1);
        vm.FilteredDocuments[0].Title.Should().Be("Annual Report");
    }

    [Fact]
    public void RefreshFilter_Search_IsCaseInsensitive()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Budget", "XLSX"));
        vm.ShowingRecent = false;
        vm.SearchText    = "BUDGET";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(1);
    }

    [Fact]
    public void RefreshFilter_Search_NoMatch_ReturnsEmpty()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Report", "PDF"));
        vm.ShowingRecent = false;
        vm.SearchText    = "zzznomatch";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().BeEmpty();
    }

    [Fact]
    public void RefreshFilter_EmptySearch_ShowsAll()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("A", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("B", "DOCX"));
        vm.ShowingRecent = false;
        vm.SearchText    = "";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(2);
    }

    // ── Type Filter ───────────────────────────────────────────────────────────

    [Fact]
    public void RefreshFilter_TypeFilter_PDF_ShowsOnlyPdf()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Doc", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Sheet", "XLSX"));
        vm.ShowingRecent  = false;
        vm.TypeFilterTag  = "PDF";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(1);
        vm.FilteredDocuments[0].FileType.Should().Be("PDF");
    }

    [Fact]
    public void RefreshFilter_TypeFilter_TXT_IncludesMdAndLog()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Readme",   "MD"));
        vm.BackgroundIndex.Add(MakeDoc("Notes",    "TXT"));
        vm.BackgroundIndex.Add(MakeDoc("App",      "LOG"));
        vm.BackgroundIndex.Add(MakeDoc("Slide",    "PPTX"));
        vm.ShowingRecent = false;
        vm.TypeFilterTag = "TXT";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(3);
    }

    [Fact]
    public void RefreshFilter_TypeFilter_Empty_ShowsAll()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Doc",   "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Sheet", "XLSX"));
        vm.ShowingRecent  = false;
        vm.TypeFilterTag  = "";

        vm.RefreshFilter();

        vm.FilteredDocuments.Should().HaveCount(2);
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshFilter_SortByName_Ascending()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Zebra",  "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Alpha",  "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Mango",  "PDF"));
        vm.ShowingRecent    = false;
        vm.SortModeIndex    = 0; // Name
        vm.SortDescending   = false;

        vm.RefreshFilter();

        vm.FilteredDocuments.Select(d => d.Title)
            .Should().ContainInOrder("Alpha", "Mango", "Zebra");
    }

    [Fact]
    public void RefreshFilter_SortByName_Descending()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Zebra", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("Alpha", "PDF"));
        vm.ShowingRecent  = false;
        vm.SortModeIndex  = 0;
        vm.SortDescending = true;

        vm.RefreshFilter();

        vm.FilteredDocuments.Select(d => d.Title)
            .Should().ContainInOrder("Zebra", "Alpha");
    }

    [Fact]
    public void RefreshFilter_SortBySize()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Big",   "PDF", sizeBytes: 9000));
        vm.BackgroundIndex.Add(MakeDoc("Small", "PDF", sizeBytes: 100));
        vm.ShowingRecent  = false;
        vm.SortModeIndex  = 2; // Size
        vm.SortDescending = false;

        vm.RefreshFilter();

        vm.FilteredDocuments.Select(d => d.Title)
            .Should().ContainInOrder("Small", "Big");
    }

    // ── DocumentCountText ─────────────────────────────────────────────────────

    [Fact]
    public void DocumentCountText_RecentMode_Zero_ShowsNoRecentFiles()
    {
        var vm = new HomeViewModel();
        vm.ShowingRecent = true;
        vm.RefreshFilter();
        vm.DocumentCountText.Should().Be("No recent files");
    }

    [Fact]
    public void DocumentCountText_RecentMode_One_ShowsSingular()
    {
        var vm = new HomeViewModel();
        vm.RecentDocuments.Add(MakeDoc("Doc", "PDF"));
        vm.ShowingRecent = true;
        vm.RefreshFilter();
        vm.DocumentCountText.Should().Be("1 recent file");
    }

    [Fact]
    public void DocumentCountText_AllMode_Many_ShowsCount()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("A", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("B", "PDF"));
        vm.BackgroundIndex.Add(MakeDoc("C", "PDF"));
        vm.ShowingRecent = false;
        vm.RefreshFilter();
        vm.DocumentCountText.Should().Be("3 documents");
    }

    // ── Scan Token Management ─────────────────────────────────────────────────

    [Fact]
    public void CreateScanToken_ReturnsCancellationToken()
    {
        var vm  = new HomeViewModel();
        var cts = vm.CreateScanToken(@"C:\Docs");
        cts.Should().NotBeNull();
        cts.IsCancellationRequested.Should().BeFalse();
        vm.RemoveScanToken(@"C:\Docs", cts);
    }

    [Fact]
    public void CancelScanForFolder_CancelsToken()
    {
        var vm  = new HomeViewModel();
        var cts = vm.CreateScanToken(@"C:\Docs");
        vm.CancelScanForFolder(@"C:\Docs");
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void CancelAllScans_CancelsAllTokens()
    {
        var vm   = new HomeViewModel();
        var cts1 = vm.CreateScanToken(@"C:\Docs");
        var cts2 = vm.CreateScanToken(@"C:\Work");
        vm.CancelAllScans();
        cts1.IsCancellationRequested.Should().BeTrue();
        cts2.IsCancellationRequested.Should().BeTrue();
    }

    // ── RemoveFolderFromIndex ─────────────────────────────────────────────────

    [Fact]
    public void RemoveFolderFromIndex_RemovesMatchingFiles()
    {
        var vm = new HomeViewModel();
        vm.BackgroundIndex.Add(MakeDoc("Report", "PDF", folder: @"C:\Docs\"));
        vm.BackgroundIndex.Add(MakeDoc("Budget", "XLSX", folder: @"C:\Work\"));
        vm.RemoveFolderFromIndex(@"C:\Docs");
        vm.BackgroundIndex.Should().HaveCount(1);
        vm.BackgroundIndex.First().FilePath.Should().Contain("Work");
    }

    // ── MatchTypeGroup ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("PDF",  "PDF",   true)]
    [InlineData("DOCX", "DOCX",  true)]
    [InlineData("DOC",  "DOCX",  true)]
    [InlineData("XLSX", "XLSX",  true)]
    [InlineData("CSV",  "XLSX",  true)]
    [InlineData("MD",   "TXT",   true)]
    [InlineData("LOG",  "TXT",   true)]
    [InlineData("TXT",  "TXT",   true)]
    [InlineData("CS",   "CODE",  true)]
    [InlineData("JS",   "CODE",  true)]
    [InlineData("PDF",  "DOCX",  false)]
    [InlineData("XLSX", "TXT",   false)]
    public void MatchTypeGroup_ReturnsExpected(string fileType, string tag, bool expected)
        => HomeViewModel.MatchTypeGroup(fileType, tag).Should().Be(expected);

    // ── NormalizePath ─────────────────────────────────────────────────────────

    [Fact]
    public void NormalizePath_EndsWithSeparator()
        => HomeViewModel.NormalizePath(@"C:\Docs").Should().EndWith(@"\");

    [Fact]
    public void NormalizePath_IsLowerCase()
        => HomeViewModel.NormalizePath(@"C:\DOCS").Should().Be(@"c:\docs\");

    [Fact]
    public void NormalizePath_TrailingSeparatorNotDoubled()
        => HomeViewModel.NormalizePath(@"C:\Docs\").Should().Be(@"c:\docs\");
}
