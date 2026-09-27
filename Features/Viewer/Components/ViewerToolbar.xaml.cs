using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace QuickView.Features.Viewer.Components;

public sealed partial class ViewerToolbar : UserControl
{
    // Events forwarded to ViewerPage
#pragma warning disable CS0067  // events are subscribed by ViewerPage at runtime
    public event EventHandler? SidebarToggled;
    public event EventHandler? FitWidthRequested;
    public event EventHandler? FitPageRequested;
    public event EventHandler<double>? RotateRequested;
    public event EventHandler? FindRequested;
#pragma warning restore CS0067

    public ViewerToolbar() => InitializeComponent();
}
