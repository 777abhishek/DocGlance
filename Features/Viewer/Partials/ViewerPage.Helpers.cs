using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    internal static Brush ResBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b)
            return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    internal static Style? ResStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Style s)
            return s;
        return null;
    }
}
