# Slice 2: File Browser - Implementation Plan

## Goal
Build a functional file browser UI using exact WinUI Gallery samples for TreeView (folder navigation), ListView (file list), BreadcrumbBar (path navigation), and file selection.

## Upstream Resources

### Primary Samples (C:\Code\WinUI3\WinUIGallery)

1. **TreeView Sample**
   - Location: `Samples\TreeView\TreeViewPage.xaml`
   - Location: `Samples\TreeView\TreeViewPage.xaml.cs`
   - Usage: Folder tree navigation in sidebar
   
2. **ListView Sample**
   - Location: `Samples\ListView\ListViewPage.xaml`
   - Location: `Samples\ListView\ListViewPage.xaml.cs`
   - Usage: File list display in main content area
   
3. **BreadcrumbBar Sample**
   - Location: `Samples\BreadcrumbBar\BreadcrumbBarPage.xaml`
   - Location: `Samples\BreadcrumbBar\BreadcrumbBarPage.xaml.cs`
   - Usage: Navigation path at top of file browser
   
4. **StoragePickers Sample**
   - Location: `Samples\StoragePickers\StoragePickersPage.xaml`
   - Location: `Samples\StoragePickers\StoragePickersPage.xaml.cs`
   - Usage: Reference for file system access patterns

## Architecture

```
FileBrowser/
├── FileBrowserPage.xaml           (Main composite UI)
├── FileBrowserPage.xaml.cs        
├── FolderTreeView.xaml            (Extracted TreeView)
├── FolderTreeView.xaml.cs         
├── FileListView.xaml              (Extracted ListView)
├── FileListView.xaml.cs           
└── PathBreadcrumb.xaml            (Extracted BreadcrumbBar)
    └── PathBreadcrumb.xaml.cs     
```

## Integration Points

### Namespace Changes
- `WinUIGallery.ControlPages` → `QuickView.FileBrowser`
- `WinUIGallery.Samples` → `QuickView.FileBrowser`

### Data Models
Create minimal data models:
- `FileItem` (Name, Path, Size, DateModified, IsDirectory, Icon)
- `FolderNode` (Name, Path, Children)

### Service Integration
- Use `Windows.Storage` APIs for file system access
- Reference StoragePickers sample for proper async file access patterns

## Implementation Steps

1. **Read upstream samples**
   - TreeViewPage.xaml/.cs
   - ListViewPage.xaml/.cs
   - BreadcrumbBarPage.xaml/.cs
   - StoragePickersPage.xaml/.cs

2. **Extract reusable components**
   - TreeView XAML → FolderTreeView.xaml
   - ListView XAML → FileListView.xaml
   - BreadcrumbBar XAML → PathBreadcrumb.xaml

3. **Create composite FileBrowserPage**
   - Grid layout with TreeView on left, ListView on right
   - BreadcrumbBar at top
   - Wire up navigation events

4. **Integrate into Shell**
   - Add FileBrowser as navigation item in TitleBarWindow
   - Create navigation logic

5. **Build and validate**
   - Build with zero errors
   - Launch and test navigation
   - Verify file browsing works

## Validation Criteria

- [ ] Build succeeds with zero errors
- [ ] App launches with FileBrowser visible
- [ ] TreeView displays folder structure
- [ ] ListView displays files
- [ ] BreadcrumbBar shows current path
- [ ] Clicking folders navigates correctly
- [ ] All upstream licenses preserved

## Stop Conditions

- If TreeView/ListView/BreadcrumbBar samples require Gallery-specific infrastructure not present in samples
- If Windows.Storage APIs require capabilities not suitable for the app
- If integration requires custom UI code beyond simple wiring

## Next Slice

After successful completion: **Slice 3 - File Detection**
