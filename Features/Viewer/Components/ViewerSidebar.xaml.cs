using Microsoft.UI.Xaml.Controls;
using System;

namespace QuickView.Features.Viewer.Components;

public sealed partial class ViewerSidebar : UserControl
{
#pragma warning disable CS0067  // subscribed by ViewerPage at runtime
    public event EventHandler<int>? PageSelected;
#pragma warning restore CS0067

    public ViewerSidebar() => InitializeComponent();
}
