using Microsoft.UI.Dispatching;
using DocGlance;
using DocGlance.Core.Constants;
using DocGlance.Core.Models;
using DocGlance.Infrastructure.Configuration;
using DocGlance.Infrastructure.Logging;
using DocGlance.UI.Shell;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DocGlance.Services.Input;

public sealed class QuickLookHotkeyService
{
    private static TitleBarWindow? _owner;
    private static GlobalHotkeyRegistration? _registration;
    private readonly TitleBarWindow _window;

    internal bool IsRegistered => _registration?.IsRegistered == true;
    internal static bool HasOwner => _owner != null;

    internal QuickLookHotkeyService(TitleBarWindow window)
    {
        _window = window;
    }

    internal bool TrySetShortcutEnabled(bool enabled, out string error)
    {
        error = string.Empty;
        if (_registration == null)
        {
            error = "The global shortcut service is unavailable.";
            return false;
        }

        if (enabled)
        {
            if (!_registration.TryRegister(
                    AppSettings.QuickLookHotkeyKey,
                    AppSettings.QuickLookHotkeyModifiers,
                    out error))
                return false;
        }
        else
        {
            _registration.Unregister();
        }

        AppSettings.QuickLookSpaceShortcutEnabled = enabled;
        return true;
    }

    internal bool TrySetHotkey(int virtualKey, uint modifiers, out string error)
    {
        error = string.Empty;
        if (_registration == null)
        {
            error = "The global shortcut service is unavailable.";
            return false;
        }

        bool enabled = AppSettings.QuickLookSpaceShortcutEnabled;
        if (!_registration.TryRegister(virtualKey, modifiers, out error))
            return false;

        if (!enabled)
            _registration.Unregister();

        AppSettings.QuickLookHotkeyKey = virtualKey;
        AppSettings.QuickLookHotkeyModifiers = modifiers;
        return true;
    }

    internal void Suspend() => _registration?.Unregister();

    internal bool Resume(out string error)
    {
        error = string.Empty;
        if (!AppSettings.QuickLookSpaceShortcutEnabled)
            return true;

        return _registration?.TryRegister(
            AppSettings.QuickLookHotkeyKey,
            AppSettings.QuickLookHotkeyModifiers,
            out error) == true;
    }

    internal void BecomeOwner()
    {
        _owner = _window;
        try
        {
            _registration = new GlobalHotkeyRegistration(
                _window.Hwnd,
                _window.DispatcherQueue,
                () => (TitleBarWindow.Current ?? _window).HandleGlobalFileOpenHotkey());

            if (AppSettings.QuickLookSpaceShortcutEnabled &&
                !_registration.TryRegister(
                    AppSettings.QuickLookHotkeyKey,
                    AppSettings.QuickLookHotkeyModifiers,
                    out string error))
            {
                Logger.Log($"[QuickLook] Could not register global shortcut: {error}");
            }
        }
        catch (Exception ex)
        {
            _registration?.Dispose();
            _registration = null;
            _owner = null;
            Logger.Error("[QuickLook] Could not initialize global shortcut registration", ex);
        }
    }

    internal void ReleaseOwner()
    {
        if (!ReferenceEquals(_owner, _window))
            return;

        _registration?.Dispose();
        _registration = null;
        _owner = null;

        var replacement = App.AllWindows.FirstOrDefault(window => !ReferenceEquals(window, _window));
        replacement?.BecomeQuickLookHotkeyOwner();
    }

    internal void HandleGlobalFileOpenHotkey()
    {
        try
        {
            var window = TitleBarWindow.Current ?? _window;
            IntPtr foregroundWindow = GetForegroundWindow();
            bool appIsForeground = foregroundWindow == window.Hwnd ||
                App.AllWindows.Any(appWindow => appWindow.Hwnd == foregroundWindow);

            if (appIsForeground || TryGetSelectedExplorerFile(foregroundWindow) is not { } filePath)
                return;

            var document = DocumentItem.FromFileInfo(new FileInfo(filePath));
            window.RestoreAndActivate();
            window.OpenDocument(document);
        }
        catch (Exception ex)
        {
            Logger.Error("[QuickLook] Global file-open shortcut failed", ex);
        }
    }

    private static string? TryGetSelectedExplorerFile(IntPtr foregroundWindow)
    {
        object? shell = null;
        object? windows = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null || Activator.CreateInstance(shellType) is not { } shellObject)
                return null;

            shell = shellObject;
            dynamic shellAutomation = shellObject;
            windows = shellAutomation.Windows();
            dynamic shellWindows = windows;
            int windowCount = Convert.ToInt32(shellWindows.Count);

            for (int i = 0; i < windowCount; i++)
            {
                object? browser = null;
                object? document = null;
                object? selectedItems = null;
                object? selectedItem = null;
                try
                {
                    browser = shellWindows.Item(i);
                    dynamic browserAutomation = browser;
                    if (new IntPtr(Convert.ToInt64(browserAutomation.HWND)) != foregroundWindow)
                        continue;

                    document = browserAutomation.Document;
                    dynamic documentAutomation = document;
                    selectedItems = documentAutomation.SelectedItems();
                    dynamic selection = selectedItems;
                    if (Convert.ToInt32(selection.Count) == 0)
                        return null;

                    selectedItem = selection.Item(0);
                    dynamic file = selectedItem;
                    string? path = Convert.ToString(file.Path);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        return null;

                    string extension = Path.GetExtension(path);
                    return SupportedExtensions.All.Contains(extension, StringComparer.OrdinalIgnoreCase)
                        ? path
                        : null;
                }
                finally
                {
                    ReleaseComObject(selectedItem);
                    ReleaseComObject(selectedItems);
                    ReleaseComObject(document);
                    ReleaseComObject(browser);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("[QuickLook] Could not read the selected Explorer file", ex);
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }

        return null;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
