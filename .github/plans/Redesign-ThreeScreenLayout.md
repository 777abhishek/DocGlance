# Redesign: Three-Screen Layout with Branding

## Goal
Redesign QuickView with clean 3-screen navigation and proper branding.

## Screen Structure

### 1. Home Screen
- **Purpose**: Scan folders and list all document types
- **Layout**: Grid view with document thumbnails/icons
- **Actions**: Click to open in Viewer

### 2. Viewer Screen  
- **Purpose**: Open and view files
- **Layout**: Full-screen viewer with sidebar bullets for document navigation
- **Features**: 
  - Sidebar shows document pages/sections as bullet points
  - Main area shows document content

### 3. Settings Screen
- **Purpose**: App settings and preferences
- **Layout**: Settings controls from WinUI Gallery

## Navigation
- NavigationView with 3 items only:
  - Home (House icon)
  - Viewer (Document icon)  
  - Settings (Gear icon)

## Branding
- App name: "QuickView" in TitleBar
- Subtitle: "Universal File Viewer"
- Custom icon/logo
- Consistent color scheme

## Upstream Resources (C:\Code\WinUI3\WinUIGallery)

1. **NavigationView** - Already using from TitleBarWindow
2. **GridView** - For Home screen file grid
   - Location: `Samples\GridView\GridViewPage.xaml`
3. **Settings Controls** - For Settings screen
   - Location: `Samples\*` (various setting controls)
4. **ScrollViewer** - For Viewer screen content
5. **ListView with bullets** - For document navigation sidebar

## Implementation Steps

1. Update TitleBarWindow branding (Title, Subtitle)
2. Create HomePage.xaml with GridView for documents
3. Create ViewerPage.xaml with sidebar bullets + content area
4. Create SettingsPage.xaml with settings controls
5. Update NavigationView to only show 3 items
6. Wire up navigation

## Validation
- [ ] Only 3 menu items visible
- [ ] Home shows document grid
- [ ] Viewer has bullet sidebar + content area
- [ ] Settings shows app settings
- [ ] Branding is consistent
- [ ] Build succeeds with zero errors
