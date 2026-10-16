using System;
using System.Collections.Generic;
using System.IO;
using DocGlance.Features.Viewer.Contracts;

namespace DocGlance.Features.Viewer;

public sealed class DocumentViewerRegistry
{
    private readonly Dictionary<string, IDocumentViewerFactory> _factories;
    private readonly IDocumentViewerFactory _fallbackFactory;

    public DocumentViewerRegistry(
        IEnumerable<IDocumentViewerFactory> factories,
        IDocumentViewerFactory fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _fallbackFactory = fallbackFactory ?? throw new ArgumentNullException(nameof(fallbackFactory));
        _factories = new Dictionary<string, IDocumentViewerFactory>(StringComparer.OrdinalIgnoreCase);

        foreach (var factory in factories)
        {
            ArgumentNullException.ThrowIfNull(factory);
            foreach (string extension in factory.SupportedExtensions)
            {
                string key = NormalizeExtension(extension);
                if (!_factories.TryAdd(key, factory))
                    throw new ArgumentException($"More than one viewer factory claims '{key}'.", nameof(factories));
            }
        }

        foreach (string extension in _fallbackFactory.SupportedExtensions)
        {
            string key = NormalizeExtension(extension);
            if (!_factories.TryAdd(key, _fallbackFactory))
                throw new ArgumentException($"More than one viewer factory claims '{key}'.", nameof(fallbackFactory));
        }
    }

    public IDocumentViewerFactory Resolve(string extensionOrPath)
    {
        string extension = Path.GetExtension(extensionOrPath);
        if (string.IsNullOrEmpty(extension))
            extension = extensionOrPath;

        string key = NormalizeExtension(extension);
        return _factories.TryGetValue(key, out var factory) ? factory : _fallbackFactory;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string key = extension.Trim();
        if (!key.StartsWith(".", StringComparison.Ordinal))
            key = "." + key;
        return key;
    }
}
