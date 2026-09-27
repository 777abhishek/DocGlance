# QuickView — Build Specification

## Goal

Build a **free, lightweight universal file viewer for Windows 10/11** inspired by macOS Quick Look.

Priority: **fast startup, native Windows UI, small installer, offline/private processing.**

---

## Stack

- C# / .NET 10
- WinUI 3
- Windows App SDK
- XAML
- WebView2 where useful
- MSIX + portable build
- x64 first; ARM64 later

## UI

Use Fluent/WinUI design.

Resources:

- Windows App SDK: https://github.com/microsoft/WindowsAppSDK
- WinUI: https://github.com/microsoft/microsoft-ui-xaml
- WinUI Gallery: https://github.com/microsoft/WinUI-Gallery
- CommunityToolkit: https://github.com/CommunityToolkit/Windows
- Fluent UI System Icons: https://github.com/microsoft/fluentui-system-icons

UI requirements:

- Windows 11 Fluent/Mica
- Dark/light/system theme
- Tabbed interface
- Navigation sidebar
- File browser
- Search/quick finder
- Viewer canvas
- CommandBar
- Keyboard shortcut system
- Drag & drop
- Responsive resizing

---

## Core Architecture

```text
QuickView
├── App
├── Shell
├── FileBrowser
├── QuickView
├── ViewerCore
│   ├── ImageViewer
│   ├── PdfViewer
│   ├── TextViewer
│   ├── MarkdownViewer
│   ├── JsonViewer
│   ├── VideoViewer
│   ├── AudioViewer
│   ├── ArchiveViewer
│   ├── OfficeViewer
│   └── ThreeDViewer
├── FileDetection
├── ShellIntegration
├── Settings
└── Services
```

Use a common interface:

```csharp
public interface IViewer
{
    bool CanOpen(string path);
    FrameworkElement Create(string path);
}
```

Detect files using **extension + magic/byte signature**.

---

## V1 Formats

### Documents
- PDF
- TXT
- Markdown
- JSON
- XML
- CSV
- EPUB

### Images
- JPG
- PNG
- GIF
- WebP
- BMP
- SVG
- TIFF

### Media
- MP4
- MKV
- WebM
- MP3
- WAV
- FLAC

### Archives
- ZIP
- 7Z
- TAR
- GZ

### Developer
- 100+ text/code languages through syntax highlighting

---

## Libraries

Prefer mature open-source libraries.

### PDF
- MuPDF: https://github.com/ArtifexSoftware/mupdf

### Images
- ImageSharp: https://github.com/SixLabors/ImageSharp
- Windows Imaging Component where appropriate

### Video / Audio
- FFmpeg: https://github.com/FFmpeg/FFmpeg
- LibVLCSharp: https://github.com/videolan/libvlcsharp

### Archives
- SharpCompress: https://github.com/adamhathcock/sharpcompress

### Markdown
- Markdig: https://github.com/xoofx/markdig

### Syntax highlighting
- ICSharpCode.AvalonEdit: https://github.com/icsharpcode/AvalonEdit

### SQLite
- Microsoft.Data.Sqlite

### 3D
- HelixToolkit: https://github.com/helix-toolkit/helix-toolkit

### Office
Evaluate:
- Open XML SDK: https://github.com/dotnet/Open-XML-SDK
- LibreOffice headless conversion only if necessary

Avoid bundling heavyweight dependencies unless required.

---

## Quick Look

Implement:

```text
File Explorer
     ↓
Select file
     ↓
Press SPACE
     ↓
QuickView preview
     ↓
SPACE / ESC → close
```

Requirements:

- Global/Explorer-compatible shortcut behavior
- Skip preview while inline rename is active
- Fast preview startup
- Preview selected file without opening full application UI

Investigate Windows Shell/Preview Handler APIs before implementing custom hooks.

---

## File Explorer Integration

Support:

- Open With
- Default application registration
- File associations
- Drag/drop
- Explorer context menu where appropriate
- Preview/Quick Look integration

---

## Performance Targets

Target:

- Cold startup: **<500 ms**
- Image preview: **<100 ms**
- Text preview: **<100 ms**
- PDF first page: **<300 ms**
- Quick Look appearance: **<200 ms** where practical
- Idle RAM: **<150 MB**
- Typical installed size: **<150 MB**
- Prefer **<100 MB** if dependencies allow

Do not load every viewer engine at startup.

Use lazy loading.

---

## Privacy

- No cloud services
- No telemetry by default
- No file uploads
- Offline processing
- No network requirement for core functionality
- Block executable files from being automatically executed

---

## V1 UI

```text
┌──────────────────────────────────────────────┐
│ QuickView       +  Tab  ⋮                     │
├──────────────┬───────────────────────────────┤
│ Home         │                               │
│ Recent       │                               │
│ Favorites    │        Viewer                 │
│ Drives       │                               │
│              │                               │
│              │                               │
├──────────────┴───────────────────────────────┤
│ filename.ext          1 / 10       100%      │
└──────────────────────────────────────────────┘
```

For a selected folder, show:

- file list/grid
- thumbnails
- sortable columns
- preview pane

---

## Build Rules

1. **No Rust.**
2. Native WinUI 3 UI.
3. Keep core application modular.
4. Do not implement file parsers from scratch when a reliable library exists.
5. Lazy-load heavy engines.
6. Avoid Electron.
7. Avoid WebView2 for formats that can be rendered natively.
8. No telemetry.
9. Keep dependencies/license-compatible with a free/open-source project.
10. Build x64 first.
11. Installer must be small.
12. Every viewer must fail gracefully when a format cannot be rendered.

## Development Order

1. WinUI shell
2. File browser
3. File detection
4. Image viewer
5. Text/code viewer
6. PDF viewer
7. Video/audio
8. Archive viewer
9. Quick Look / Space integration
10. File associations
11. Settings
12. Performance optimization
13. Installer/portable release
14. Additional formats