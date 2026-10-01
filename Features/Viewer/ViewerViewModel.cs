using CommunityToolkit.Mvvm.ComponentModel;
using QuickView.Models;

namespace QuickView.Features.Viewer;

/// <summary>
/// ViewModel for ViewerPage — exposes document state for data-binding.
/// Uses CommunityToolkit.Mvvm [ObservableProperty] so every setter
/// automatically fires PropertyChanged and the UI stays in sync.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    [ObservableProperty]
    private DocumentItem? _document;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private bool _sidebarOpen = true;
}
