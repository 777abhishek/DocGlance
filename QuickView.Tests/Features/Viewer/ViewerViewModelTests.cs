using QuickView.Features.Viewer;
using QuickView.Core.Models;

namespace QuickView.Tests.Features.Viewer;

public class ViewerViewModelTests
{
    private static DocumentItem MakeDoc(string title = "Report", string fileType = "PDF") => new()
    {
        FilePath  = $@"C:\Docs\{title}.{fileType.ToLower()}",
        Title     = title,
        FileType  = fileType,
    };

    // ── Default state ─────────────────────────────────────────────────────────

    [Fact]
    public void Default_CurrentPage_Is1()
        => new ViewerViewModel().CurrentPage.Should().Be(1);

    [Fact]
    public void Default_TotalPages_Is1()
        => new ViewerViewModel().TotalPages.Should().Be(1);

    [Fact]
    public void Default_Zoom_Is1()
        => new ViewerViewModel().Zoom.Should().Be(1.0);

    [Fact]
    public void Default_Rotation_Is0()
        => new ViewerViewModel().Rotation.Should().Be(0);

    [Fact]
    public void Default_SidebarOpen_IsTrue()
        => new ViewerViewModel().SidebarOpen.Should().BeTrue();

    [Fact]
    public void Default_IsLoading_IsFalse()
        => new ViewerViewModel().IsLoading.Should().BeFalse();

    [Fact]
    public void Default_Document_IsNull()
        => new ViewerViewModel().Document.Should().BeNull();

    [Fact]
    public void Default_ErrorMessage_IsEmpty()
        => new ViewerViewModel().ErrorMessage.Should().BeEmpty();

    // ── PageDisplay ───────────────────────────────────────────────────────────

    [Fact]
    public void PageDisplay_Default_Shows1Of1()
        => new ViewerViewModel().PageDisplay.Should().Be("1 / 1");

    [Fact]
    public void PageDisplay_Updates_WhenPageChanges()
    {
        var vm = new ViewerViewModel { TotalPages = 10, CurrentPage = 5 };
        vm.PageDisplay.Should().Be("5 / 10");
    }

    // ── CanGoPrev / CanGoNext ────────────────────────────────────────────────

    [Fact]
    public void CanGoPrev_False_OnFirstPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 1, TotalPages = 5 };
        vm.CanGoPrev.Should().BeFalse();
    }

    [Fact]
    public void CanGoPrev_True_AfterFirstPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 2, TotalPages = 5 };
        vm.CanGoPrev.Should().BeTrue();
    }

    [Fact]
    public void CanGoNext_False_OnLastPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 5, TotalPages = 5 };
        vm.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void CanGoNext_True_BeforeLastPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 4, TotalPages = 5 };
        vm.CanGoNext.Should().BeTrue();
    }

    // ── ZoomPercent / ZoomDisplay ─────────────────────────────────────────────

    [Fact]
    public void ZoomPercent_100_WhenZoomEqualsNaturalZoom()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0.25, Zoom = 0.25 };
        vm.ZoomPercent.Should().Be(100);
    }

    [Fact]
    public void ZoomPercent_200_WhenZoomIsDoubleNaturalZoom()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0.25, Zoom = 0.50 };
        vm.ZoomPercent.Should().Be(200);
    }

    [Fact]
    public void ZoomPercent_50_WhenZoomIsHalfNaturalZoom()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0.25, Zoom = 0.125 };
        vm.ZoomPercent.Should().Be(50);
    }

    [Fact]
    public void ZoomDisplay_ContainsPercent()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0.25, Zoom = 0.25 };
        vm.ZoomDisplay.Should().Be("100%");
    }

    [Fact]
    public void ZoomPercent_NaturalZoomZero_Returns100()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0, Zoom = 0.25 };
        vm.ZoomPercent.Should().Be(100);
    }

    // ── DocumentTitle / IsPdf ────────────────────────────────────────────────

    [Fact]
    public void DocumentTitle_Empty_WhenNoDocument()
        => new ViewerViewModel().DocumentTitle.Should().BeEmpty();

    [Fact]
    public void DocumentTitle_ReturnsDocumentTitle()
    {
        var vm = new ViewerViewModel { Document = MakeDoc("Annual Report") };
        vm.DocumentTitle.Should().Be("Annual Report");
    }

    [Fact]
    public void IsPdf_True_ForPdfDocument()
    {
        var vm = new ViewerViewModel { Document = MakeDoc(fileType: "PDF") };
        vm.IsPdf.Should().BeTrue();
    }

    [Fact]
    public void IsPdf_False_ForDocxDocument()
    {
        var vm = new ViewerViewModel { Document = MakeDoc(fileType: "DOCX") };
        vm.IsPdf.Should().BeFalse();
    }

    [Fact]
    public void IsPdf_False_WhenNoDocument()
        => new ViewerViewModel().IsPdf.Should().BeFalse();

    // ── HasError ──────────────────────────────────────────────────────────────

    [Fact]
    public void HasError_False_WhenErrorMessageEmpty()
        => new ViewerViewModel().HasError.Should().BeFalse();

    [Fact]
    public void HasError_True_WhenErrorMessageSet()
    {
        var vm = new ViewerViewModel { ErrorMessage = "Cannot open file" };
        vm.HasError.Should().BeTrue();
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllState()
    {
        var vm = new ViewerViewModel
        {
            Document     = MakeDoc(),
            CurrentPage  = 5,
            TotalPages   = 10,
            Zoom         = 2.0,
            NaturalZoom  = 0.25,
            Rotation     = 90,
            IsLoading    = true,
            ErrorMessage = "Some error",
        };

        vm.Reset();

        vm.Document.Should().BeNull();
        vm.CurrentPage.Should().Be(1);
        vm.TotalPages.Should().Be(1);
        vm.Zoom.Should().Be(1.0);
        vm.NaturalZoom.Should().Be(1.0);
        vm.Rotation.Should().Be(0);
        vm.IsLoading.Should().BeFalse();
        vm.ErrorMessage.Should().BeEmpty();
        vm.HasError.Should().BeFalse();
    }

    // ── PropertyChanged notifications ─────────────────────────────────────────

    [Fact]
    public void CurrentPage_Fires_PageDisplay_CanGoPrev_CanGoNext()
    {
        var vm = new ViewerViewModel { TotalPages = 5 };
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.CurrentPage = 3;

        fired.Should().Contain("CurrentPage")
             .And.Contain("PageDisplay")
             .And.Contain("CanGoPrev")
             .And.Contain("CanGoNext");
    }

    [Fact]
    public void TotalPages_Fires_PageDisplay_CanGoNext()
    {
        var vm = new ViewerViewModel();
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.TotalPages = 10;

        fired.Should().Contain("TotalPages")
             .And.Contain("PageDisplay")
             .And.Contain("CanGoNext");
    }

    [Fact]
    public void Document_Fires_DocumentTitle_IsPdf()
    {
        var vm = new ViewerViewModel();
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.Document = MakeDoc();

        fired.Should().Contain("Document")
             .And.Contain("DocumentTitle")
             .And.Contain("IsPdf");
    }

    [Fact]
    public void Zoom_Fires_ZoomPercent_ZoomDisplay()
    {
        var vm = new ViewerViewModel { NaturalZoom = 0.25 };
        var fired = new List<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.Zoom = 0.5;

        fired.Should().Contain("Zoom")
             .And.Contain("ZoomPercent")
             .And.Contain("ZoomDisplay");
    }

    // ── Commands raise PageChangeRequested ───────────────────────────────────

    [Fact]
    public void NextPageCommand_RaisesPageChangeRequested()
    {
        var vm = new ViewerViewModel { CurrentPage = 2, TotalPages = 5 };
        int? requested = null;
        vm.PageChangeRequested += (_, p) => requested = p;

        vm.NextPageCommand.Execute(null);

        requested.Should().Be(3);
    }

    [Fact]
    public void PreviousPageCommand_RaisesPageChangeRequested()
    {
        var vm = new ViewerViewModel { CurrentPage = 3, TotalPages = 5 };
        int? requested = null;
        vm.PageChangeRequested += (_, p) => requested = p;

        vm.PreviousPageCommand.Execute(null);

        requested.Should().Be(2);
    }

    [Fact]
    public void GoToPageCommand_RaisesPageChangeRequested_WithTargetPage()
    {
        var vm = new ViewerViewModel { TotalPages = 10 };
        int? requested = null;
        vm.PageChangeRequested += (_, p) => requested = p;

        vm.GoToPageCommand.Execute(7);

        requested.Should().Be(7);
    }

    [Fact]
    public void NextPageCommand_CannotExecute_OnLastPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 5, TotalPages = 5 };
        vm.NextPageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PreviousPageCommand_CannotExecute_OnFirstPage()
    {
        var vm = new ViewerViewModel { CurrentPage = 1, TotalPages = 5 };
        vm.PreviousPageCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Zoom commands raise events ────────────────────────────────────────────

    [Fact]
    public void ZoomInCommand_RaisesZoomInRequested()
    {
        var vm = new ViewerViewModel();
        bool raised = false;
        vm.ZoomInRequested += (_, _) => raised = true;

        vm.ZoomInCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void ZoomOutCommand_RaisesZoomOutRequested()
    {
        var vm = new ViewerViewModel();
        bool raised = false;
        vm.ZoomOutRequested += (_, _) => raised = true;

        vm.ZoomOutCommand.Execute(null);

        raised.Should().BeTrue();
    }

    [Fact]
    public void ResetZoomCommand_RaisesZoomResetRequested()
    {
        var vm = new ViewerViewModel();
        bool raised = false;
        vm.ZoomResetRequested += (_, _) => raised = true;

        vm.ResetZoomCommand.Execute(null);

        raised.Should().BeTrue();
    }
}
