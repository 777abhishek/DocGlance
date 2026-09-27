# QuickView

**Fast universal file previewer for Windows**

QuickView is a native Windows application built with WinUI 3 that lets you instantly preview PDFs, documents, images, and code files without opening heavy applications.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

## Features

- **📄 PDF Viewer** — Progressive rendering, thumbnails, outline navigation, full-text search
- **💻 Code Preview** — Syntax highlighting for C#, Python, JavaScript, and 50+ languages
- **📝 Text Files** — Fast preview for TXT, JSON, XML, Markdown with search
- **🖼️ Images** — Support for PNG, JPG, GIF, BMP, WebP (planned)
- **📊 Office Documents** — Preview for DOCX, XLSX, PPTX (planned)
- **🔍 Instant Search** — Find text across PDFs and documents with highlighting
- **⚡ Lightweight** — Native WinUI 3 with minimal memory footprint

## Installation

### Via Installer (Recommended)

1. Download `QuickView-Setup-x64.exe` from [Releases](https://github.com/777abhishek/QuickView/releases)
2. Run the installer
3. Launch QuickView from the Start menu

### Via winget (Coming Soon)

```powershell
winget install QuickView.QuickView
```

### Build from Source

```powershell
git clone https://github.com/777abhishek/QuickView.git
cd QuickView
.\build.ps1
```

**Requirements:**
- Windows 10 version 1809 (build 17763) or later
- .NET 8.0 SDK (for building from source)

## Usage

1. **Open with QuickView** — Right-click any file → Open with → QuickView
2. **Set as Default** — Use Settings page to register file associations
3. **Keyboard Shortcuts:**
   - `Ctrl+F` — Search in document
   - `Ctrl+Mouse Wheel` — Zoom in/out
   - `Escape` — Close window

## File Association

QuickView can register itself as the default handler for supported file types:

1. Open QuickView Settings
2. Click **Register File Associations**
3. Choose QuickView when opening files

Supported extensions: `.pdf`, `.txt`, `.cs`, `.py`, `.js`, `.json`, `.xml`, `.md`, and more.

## Technology

- **Framework:** .NET 8 + WinUI 3
- **UI:** Windows App SDK 2.4
- **PDF:** PDFium (native rendering)
- **Database:** SQLite (EF Core) for indexing
- **Icons:** Multi-resolution embedded icon (16-256px)

## Building

### Prerequisites

- Visual Studio 2022 or VS Code with C# extension
- .NET 8.0 SDK
- Windows 10 SDK (10.0.19041.0 or later)

### Build Commands

```powershell
# Debug build
.\build.ps1

# Release build with installer
.\build-installer.ps1
```

Output:
- Debug: `bin/x64/Debug/net8.0-windows10.0.19041.0/QuickView.exe`
- Installer: `installer/output/QuickView-Setup-x64.exe`

## Project Structure

```
QuickView/
├── Features/          # UI pages (Home, Viewer, Settings, Browser)
├── Rendering/         # File renderers (PDF, Text, Code, Images)
├── FileSystem/        # Indexing, scanning, file watching
├── Infrastructure/    # Logging, storage, registration
├── Controls/          # Reusable UI controls
├── Models/            # Data models
├── Assets/            # Icons, images
├── installer/         # Inno Setup script
└── tools/             # Build utilities
```

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgments

- **PDFium** — PDF rendering engine (Google)
- **Windows App SDK** — Modern Windows UI framework (Microsoft)
- **ICSharpCode.TextEditor** — Syntax highlighting
- **PdfSharpCore** — PDF utilities

---

**Built with ❤️ for Windows users who need fast file previews**
