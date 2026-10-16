using System;
using Windows.Foundation;

namespace DocGlance.Features.Viewer;

internal static class PageRotation
{
    public static double Normalize(double angle) =>
        ((angle % 360) + 360) % 360;

    public static (double Width, double Height) GetSize(
        double width,
        double height,
        double angle) =>
        Normalize(angle) is 90 or 270 ? (height, width) : (width, height);

    public static Point RotatePoint(Point point, double width, double height, double angle) =>
        Normalize(angle) switch
        {
            90 => new Point(height - point.Y, point.X),
            180 => new Point(width - point.X, height - point.Y),
            270 => new Point(point.Y, width - point.X),
            _ => point,
        };

    public static Point UnrotatePoint(Point point, double width, double height, double angle) =>
        Normalize(angle) switch
        {
            90 => new Point(point.Y, height - point.X),
            180 => new Point(width - point.X, height - point.Y),
            270 => new Point(width - point.Y, point.X),
            _ => point,
        };

    public static Rect RotateRect(Rect rect, double width, double height, double angle)
    {
        Point topLeft = RotatePoint(new Point(rect.Left, rect.Top), width, height, angle);
        Point topRight = RotatePoint(new Point(rect.Right, rect.Top), width, height, angle);
        Point bottomLeft = RotatePoint(new Point(rect.Left, rect.Bottom), width, height, angle);
        Point bottomRight = RotatePoint(new Point(rect.Right, rect.Bottom), width, height, angle);
        double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect(left, top, right - left, bottom - top);
    }
}
