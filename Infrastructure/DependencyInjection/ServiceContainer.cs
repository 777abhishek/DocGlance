using Microsoft.Extensions.DependencyInjection;
using QuickView.Services.Documents;
using System;

namespace QuickView.Infrastructure.DependencyInjection;

/// <summary>
/// App-wide DI container. Configured once at startup in App.xaml.cs.
/// Use ServiceContainer.Services.GetRequiredService&lt;T&gt;() to resolve.
/// </summary>
public static class ServiceContainer
{
    private static IServiceProvider? _provider;

    /// <summary>Resolved service provider — available after Configure() is called.</summary>
    public static IServiceProvider Services
        => _provider ?? throw new InvalidOperationException(
               "ServiceContainer.Configure() must be called before resolving services.");

    /// <summary>
    /// Registers all app services and builds the container.
    /// Call once in App.xaml.cs constructor before any navigation.
    /// </summary>
    public static void Configure()
    {
        var services = new ServiceCollection();

        // ── PDF viewer ────────────────────────────────────────────────────────
        // Transient: each ViewerPage tab gets its own isolated service instance
        // so documents, rendered-page caches, and prefetch queues never bleed
        // across tabs.
        services.AddTransient<IPdfViewerService, PdfViewerService>();

        _provider = services.BuildServiceProvider();
    }
}
