// ViewerViewModel.cs — Observable state for ViewerPage.
// Phase 2 refactoring: activated to own all viewer state for data-binding and testability.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocGlance.Core.Models;
using System;

namespace DocGlance.Features.Viewer;

/// <summary>
/// Sole owner of all document-viewer state: page, total pages, zoom, rotation,
/// sidebar visibility, loading/error state, and active document reference.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    // ── Document ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyPropertyChangedFor(nameof(IsPdf))]
    private DocumentItem? _document;

    public string DocumentTitle => Document?.Title ?? string.Empty;

    public bool IsPdf =>
        string.Equals(Document?.FileType, "PDF", StringComparison.OrdinalIgnoreCase);

    // ── Pagination ────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageDisplay))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    public string PageDisplay => $"{CurrentPage} / {TotalPages}";
    public bool   CanGoPrev   => CurrentPage > 1;
    public bool   CanGoNext   => CurrentPage < TotalPages;

    // ── Zoom ──────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomPercent))]
    [NotifyPropertyChangedFor(nameof(ZoomDisplay))]
    private double _zoom = 1.0;

    /// <summary>Natural zoom factor (96 / RenderDpi). Set by ViewerPage after load.</summary>
    [ObservableProperty]
    private double _naturalZoom = 1.0;

    /// <summary>Zoom as 0–100+ percentage relative to 100% reading size.</summary>
    public double ZoomPercent => NaturalZoom > 0 ? Math.Round(Zoom / NaturalZoom * 100.0) : 100.0;

    public string ZoomDisplay => $"{ZoomPercent}%";

    // ── Rotation ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    private double _rotation;

    // ── Sidebar ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _sidebarOpen = true;

    // ── Loading / Error state ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string value)
        => OnPropertyChanged(nameof(HasError));

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Raised when the page should change. ViewerPage handles the actual scroll.</summary>
    public event EventHandler<int>? PageChangeRequested;
    public event EventHandler?      ZoomInRequested;
    public event EventHandler?      ZoomOutRequested;
    public event EventHandler?      ZoomResetRequested;

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PreviousPage() => PageChangeRequested?.Invoke(this, CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => PageChangeRequested?.Invoke(this, CurrentPage + 1);

    [RelayCommand]
    private void GoToPage(int page) => PageChangeRequested?.Invoke(this, page);

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ResetZoom() => ZoomResetRequested?.Invoke(this, EventArgs.Empty);

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// <summary>Resets all state for a new document load.</summary>
    public void Reset()
    {
        Document     = null;
        CurrentPage  = 1;
        TotalPages   = 1;
        Zoom         = 1.0;
        NaturalZoom  = 1.0;
        Rotation     = 0;
        IsLoading    = false;
        ErrorMessage = string.Empty;
    }
}
