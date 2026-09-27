using Windows.UI;

namespace QuickView.Models;

/// <summary>Visual metadata for a file type: description, icon glyph, gradient colours.</summary>
public record FileTypeInfo(
    string Description,
    string Glyph,
    Color  GradientStart,
    Color  GradientEnd);
