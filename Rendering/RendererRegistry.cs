using QuickView.Core.Interfaces;
using System;
using System.Collections.Generic;

namespace QuickView.Rendering;

/// <summary>
/// Central registry that maps file extensions to their <see cref="IFileRenderer"/> implementation.
/// Register renderers at startup; call <see cref="GetRenderer"/> to resolve at runtime.
/// </summary>
public static class RendererRegistry
{
    private static readonly Dictionary<string, IFileRenderer> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IFileRenderer renderer)
    {
        foreach (var ext in renderer.SupportedExtensions)
            _map[ext] = renderer;
    }

    /// <returns>The registered renderer, or <c>null</c> if none matches.</returns>
    public static IFileRenderer? GetRenderer(string extensionWithDot) =>
        _map.TryGetValue(extensionWithDot, out var r) ? r : null;
}
