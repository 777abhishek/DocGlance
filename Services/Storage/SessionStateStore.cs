// SessionStateStore.cs — Saves and restores open tabs/session state

using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickView.Services.Storage;

/// <summary>
/// Stores session state (open tabs, selected tab) so the app can restore
/// the user's workspace when relaunched.
/// </summary>
public static class SessionStateStore
{
    private const string Key = "SessionState";

    public static void SaveOpenTabs(List<string> filePaths, int selectedIndex)
    {
        var state = new SessionState
        {
            OpenFiles = filePaths.Where(p => !string.IsNullOrEmpty(p)).ToList(),
            SelectedTabIndex = selectedIndex
        };
        SettingsStore.Save(Key, state);
    }

    public static SessionState? LoadSessionState()
    {
        return SettingsStore.Load<SessionState>(Key);
    }

    public static void Clear()
    {
        SettingsStore.Save<SessionState?>(Key, null);
    }
}

public class SessionState
{
    public List<string> OpenFiles { get; set; } = new();
    public int SelectedTabIndex { get; set; }
}
