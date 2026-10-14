using Microsoft.UI.Dispatching;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DocGlance.Services.Input;

/// <summary>Registers one system-wide shortcut against a window and receives WM_HOTKEY.</summary>
public sealed class GlobalHotkeyRegistration : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint AllowedModifiers = ModAlt | ModControl | ModShift;
    private const int HotkeyId = 0x474C;
    private static readonly UIntPtr SubclassId = new(0x474C);

    private readonly IntPtr _hwnd;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _onPressed;
    private readonly SubclassProcedure _subclassProcedure;
    private bool _subclassInstalled;
    private bool _registered;
    private bool _disposed;
    private int _key;
    private uint _modifiers;

    public bool IsRegistered => _registered;

    public GlobalHotkeyRegistration(IntPtr hwnd, DispatcherQueue dispatcherQueue, Action onPressed)
    {
        _hwnd = hwnd;
        _dispatcherQueue = dispatcherQueue;
        _onPressed = onPressed;
        _subclassProcedure = HandleWindowMessage;

        if (hwnd == IntPtr.Zero || !SetWindowSubclass(hwnd, _subclassProcedure, SubclassId, UIntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the global shortcut to the DocGlance window.");

        _subclassInstalled = true;
    }

    public bool TryRegister(int virtualKey, uint modifiers, out string error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        error = string.Empty;

        if (!IsValid(virtualKey, modifiers))
        {
            error = "Choose a non-modifier key with Ctrl, Alt, or Shift. Windows-key shortcuts are reserved.";
            return false;
        }

        int previousKey = _key;
        uint previousModifiers = _modifiers;
        bool restorePrevious = _registered;
        UnregisterNative();

        if (RegisterNative(virtualKey, modifiers, out error))
            return true;

        if (restorePrevious)
            RegisterNative(previousKey, previousModifiers, out _);

        return false;
    }

    public void Unregister()
    {
        if (!_disposed)
            UnregisterNative();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        UnregisterNative();
        if (_subclassInstalled)
        {
            RemoveWindowSubclass(_hwnd, _subclassProcedure, SubclassId);
            _subclassInstalled = false;
        }

        _disposed = true;
    }

    private bool RegisterNative(int virtualKey, uint modifiers, out string error)
    {
        if (RegisterHotKey(_hwnd, HotkeyId, modifiers | ModNoRepeat, (uint)virtualKey))
        {
            _key = virtualKey;
            _modifiers = modifiers;
            _registered = true;
            error = string.Empty;
            return true;
        }

        int errorCode = Marshal.GetLastWin32Error();
        error = errorCode == 1409
            ? "Windows or another app already uses that shortcut. Try a different combination."
            : new Win32Exception(errorCode).Message;
        return false;
    }

    private void UnregisterNative()
    {
        if (!_registered)
            return;

        UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private static bool IsValid(int virtualKey, uint modifiers)
    {
        if (virtualKey <= 0 || virtualKey > ushort.MaxValue)
            return false;

        if (virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x5D or 0x7B)
            return false;

        if ((modifiers & ~AllowedModifiers) != 0 || (modifiers & AllowedModifiers) == 0)
            return false;

        return true;
    }

    private IntPtr HandleWindowMessage(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == WmHotkey && wParam.ToUInt32() == HotkeyId)
        {
            _dispatcherQueue.TryEnqueue(() => _onPressed());
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr SubclassProcedure(
        IntPtr hwnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd,
        SubclassProcedure callback,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd,
        SubclassProcedure callback,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
}
