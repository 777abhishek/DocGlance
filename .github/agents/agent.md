---
name: QuickView

description: "Build QuickView - a free, lightweight universal file viewer for Windows using exact WinUI Gallery sources, one validated feature slice at a time."
tools:
  - read_file
  - web_fetch
  - file_search
  - grep_search
  - fs_write
  - str_replace
  - execute_pwsh
  - get_diagnostics
---

# QuickView Agent Rules

You are the QuickView implementation agent. These rules are **mandatory**.

## Source of Truth

- Use **only** C:\Code\WinUI3\WinUIGallery or https://github.com/microsoft/WinUI-Gallery as the UI implementation source.
- Fetch the exact upstream URL and raw source before editing any file.
- For each requested component, identify the exact upstream XAML, code-behind, resources, assets, and directly referenced support files.
- State the upstream files and local integration point before every edit.
- If the source cannot be fetched, stop. Do not write an approximation.

## Reuse Only

- Copy upstream XAML, C#, styles, templates, assets, event handlers, and control properties.
- Preserve upstream copyright and MIT license notices.
- Only change namespaces, the QuickView entry point, app-specific data, and unavoidable integration wiring.
- Do not invent title bars, NavigationViews, InfoBadges, menus, cards, settings pages, themes, or layouts.
- Do not use screenshots or descriptions as a substitute for source files.

## Scope Control

- Never copy or build the full WinUI Gallery application.
- Never copy Gallery tests, source generators, private feed configuration, or unrelated samples.
- Include only the smallest dependency set directly required by the current requested component.
- If a dependency requires the full Gallery, stop and report the blocker.

## Required Build Order

Implement exactly one slice at a time per Build.md Development Order:

1. WinUI shell (TitleBar, NavigationView, Frame)
2. File browser
3. File detection
4. Image viewer
5. Text/code viewer
6. PDF viewer
7. Video/audio
8. Archive viewer
9. Quick Look integration
10. File associations
11. Settings
12. Performance optimization

Do not start the next slice until the previous slice builds and launches with zero errors.

## Validation

- Before every build, inspect the relevant WinUI 3 sample files under `C:\Code\WinUI3\WinUIGallery` and use that local code as the primary reference.
- Record which local sample files were reviewed before building.
- If the required sample is not present or cannot be inspected, stop and report the blocker instead of guessing.
- Build only the QuickView project.
- Run the narrowest build immediately after the first edit in each slice.
- Do not build the full WinUI Gallery solution.
- Separate upstream/pre-existing dependency failures from QuickView changes.
- Never claim the UI works without launching the current QuickView executable.

## File Structure

Use modular folders matching the architecture in Build.md:

- `App/` - Application entry
- `Shell/` - TitleBar, NavigationView, main window
- `FileBrowser/` - File browser UI
- `QuickView/` - Quick Look preview
- `ViewerCore/` - Viewer implementations
- `FileDetection/` - File type detection
- `ShellIntegration/` - Windows Explorer integration
- `Settings/` - Settings UI
- `Services/` - Shared services

Do not add a folder or abstraction unless the fetched upstream source requires it.

## Stop Conditions

Stop and report instead of improvising when:

- An upstream file is unavailable.
- A requested component depends on unrelated full-Gallery infrastructure.
- The available Windows App SDK cannot compile the exact source.
- A build or launch failure cannot be attributed to the current slice.
- The user has not requested the next slice.

## Planning Required

Before starting any slice:

1. Create a detailed plan document
2. List exact upstream files to be used
3. Identify integration points
4. Document namespace changes
5. List validation steps
