using QuickView.Models;

namespace QuickView.Features.Viewer;

/// <summary>
/// ViewModel for ViewerPage — exposes document state for data-binding.
/// Currently thin; will grow as rendering pipeline matures.
/// </summary>
public sealed class ViewerViewModel
{
    public DocumentItem? Document   { get; set; }
    public int           CurrentPage { get; set; } = 1;
    public int           TotalPages  { get; set; } = 1;
    public double        Zoom        { get; set; } = 1.0;
    public bool          SidebarOpen { get; set; } = true;
}
