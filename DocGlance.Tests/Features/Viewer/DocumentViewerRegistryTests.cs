using DocGlance.Features.Viewer;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Features.Viewer.Formats.Fallback;
using DocGlance.Features.Viewer.Formats.Docx;
using DocGlance.Features.Viewer.Formats.Pdf;
using DocGlance.Features.Viewer.Formats.Pptx;
using DocGlance.Features.Viewer.Formats.Spreadsheet;
using DocGlance.Features.Viewer.Formats.Text;

namespace DocGlance.Tests.Features.Viewer;

public sealed class DocumentViewerRegistryTests
{
    [Fact]
    public void Resolve_ClaimsXlsWithFallbackFactory()
    {
        var fallback = new FallbackViewerFactory();
        var registry = new DocumentViewerRegistry(Array.Empty<IDocumentViewerFactory>(), fallback);

        registry.Resolve(".xls").Should().BeSameAs(fallback);
        registry.Resolve(@"C:\documents\legacy.xls").Should().BeSameAs(fallback);
    }

    [Fact]
    public void Resolve_UsesTextAndSpreadsheetFactoriesForTheirFormats()
    {
        var text = new TextViewerFactory();
        var spreadsheet = new SpreadsheetViewerFactory();
        var registry = new DocumentViewerRegistry(
            new IDocumentViewerFactory[] { text, spreadsheet },
            new FallbackViewerFactory());

        registry.Resolve(".CSV").Should().BeSameAs(text);
        registry.Resolve(".html").Should().BeSameAs(text);
        registry.Resolve(@"C:\documents\page.HTML").Should().BeSameAs(text);
        registry.Resolve(".xlsx").Should().BeSameAs(spreadsheet);
    }

    [Fact]
    public void Resolve_UsesPdfFactoryForPdfDocuments()
    {
        var pdf = new PdfViewerFactory();
        var registry = new DocumentViewerRegistry(
            new IDocumentViewerFactory[] { pdf },
            new FallbackViewerFactory());

        registry.Resolve(".pdf").Should().BeSameAs(pdf);
        registry.Resolve(@"C:\documents\sample.PDF").Should().BeSameAs(pdf);
    }

    [Fact]
    public void Resolve_UsesDocxFactoryForWordDocuments()
    {
        var docx = new DocxViewerFactory();
        var fallback = new FallbackViewerFactory();
        var registry = new DocumentViewerRegistry(
            new IDocumentViewerFactory[] { docx },
            fallback);

        registry.Resolve(".docx").Should().BeSameAs(docx);
        registry.Resolve(@"C:\documents\sample.doc").Should().BeSameAs(fallback);
    }

    [Fact]
    public void Resolve_UsesPptxFactoryForPresentationDocuments()
    {
        var pptx = new PptxViewerFactory();
        var fallback = new FallbackViewerFactory();
        var registry = new DocumentViewerRegistry(
            new IDocumentViewerFactory[] { pptx },
            fallback);

        registry.Resolve(".pptx").Should().BeSameAs(pptx);
        registry.Resolve(@"C:\documents\sample.PPT").Should().BeSameAs(fallback);
        registry.Resolve(".pptm").Should().BeSameAs(pptx);
        registry.Resolve(".ppsx").Should().BeSameAs(pptx);
    }

    [Fact]
    public void Resolve_UsesFallbackForUnclaimedExtensions()
    {
        var fallback = new StubFactory(".xls");
        var registry = new DocumentViewerRegistry(Array.Empty<IDocumentViewerFactory>(), fallback);

        registry.Resolve(".unknown").Should().BeSameAs(fallback);
    }

    [Fact]
    public void Resolve_UsesRegisteredFactoryForClaimedExtension()
    {
        var fallback = new StubFactory(".xls");
        var text = new StubFactory(".txt", ".md");
        var registry = new DocumentViewerRegistry(new[] { text }, fallback);

        registry.Resolve("C:\\documents\\notes.TXT").Should().BeSameAs(text);
    }

    [Fact]
    public void Resolve_UsesLastRegisteredFactoryForDuplicateClaims()
    {
        var first = new StubFactory(".txt");
        var second = new StubFactory(".TXT");

        var registry = new DocumentViewerRegistry(new[] { first, second }, new StubFactory(".xls"));

        registry.Resolve(".txt").Should().BeSameAs(second);
    }

    private sealed class StubFactory(params string[] extensions) : IDocumentViewerFactory
    {
        public IReadOnlyCollection<string> SupportedExtensions { get; } = extensions;

        public IDocumentViewer Create(ViewerViewModel viewModel) =>
            throw new NotSupportedException();
    }
}
