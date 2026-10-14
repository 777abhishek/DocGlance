// ActivationPaths.cs
// Extracts file paths from every activation shape QuickView can receive.
//
// Used by BOTH sides of the single-instance handover:
//  - the second process (Program.Main) to know what to send over the pipe,
//  - the primary instance (App.TryOpenFromActivationArgs) to decode what arrived.
//
// Shapes handled:
//  - IFileActivatedEventArgs        (MSIX file association / StorageFile payloads)
//  - ILaunchActivatedEventArgs      (classic "QuickView.exe \"path\""; for unpackaged
//                                    apps the Arguments may be the FULL command line
//                                    including the exe, so it is tokenised properly)
//  - ICommandLineActivatedEventArgs (activation via registered command-line surface)
//  - Environment.GetCommandLineArgs (always available fallback in the second process)

using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickView.Services.Activation;

public static class ActivationPaths
{
    /// <summary>
    /// Paths this process was launched with, from the WinRT activation args and the
    /// raw command line, deduplicated case-insensitively. Only paths that exist on
    /// disk are returned (non-file tokens like the exe itself are filtered).
    /// </summary>
    public static IReadOnlyList<string> FromCurrentLaunch()
        => FromActivationArgs(Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs())
           .Concat(FromCommandLine(Environment.GetCommandLineArgs().Skip(1)))
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .Where(IsOpenableFile)
           .ToList();

    /// <summary>
    /// Paths encoded in AppActivationArguments. Returns an empty list for unknown
    /// or empty payloads — callers log that outcome, nothing here throws.
    /// </summary>
    public static IReadOnlyList<string> FromActivationArgs(
        Microsoft.Windows.AppLifecycle.AppActivationArguments? args)
    {
        if (args?.Data is not Windows.ApplicationModel.Activation.IActivatedEventArgs data)
            return Array.Empty<string>();

        if (data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            return fileArgs.Files
                .OfType<Windows.Storage.StorageFile>()
                .Select(f => f.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
        }

        if (data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs)
        {
            // "Arguments" for unpackaged Explorer launches may be a single path or the
            // full command line; tokenise so "\"exe\" \"my file.pdf\"" parses correctly.
            return FromCommandLine(SplitCommandLine(launchArgs.Arguments ?? string.Empty)).ToList();
        }

        if (data is Windows.ApplicationModel.Activation.ICommandLineActivatedEventArgs cmdArgs)
        {
            return FromCommandLine(SplitCommandLine(cmdArgs.Operation?.Arguments ?? string.Empty)).ToList();
        }

        return Array.Empty<string>();
    }

    /// <summary>Filters candidate strings down to real files (not the exe, not missing).</summary>
    public static IEnumerable<string> FromCommandLine(IEnumerable<string> tokens)
        => tokens
            .Select(t => t?.Trim().Trim('"') ?? string.Empty)
            .Where(t => t.Length > 0)
            .Where(t => !t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(t => !string.Equals(t, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            .Where(IsOpenableFile);

    private static bool IsOpenableFile(string path)
        => !string.IsNullOrWhiteSpace(path)
           && path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0
           && System.IO.File.Exists(path);

    /// <summary>
    /// Splits a command-line string using the standard Windows rules: whitespace
    /// separates tokens, double quotes group, backslashes escape quotes only when
    /// they precede a quote (2n backslashes + quote → n backslashes + literal quote;
    /// 2n+1 backslashes + quote → n backslashes + token terminator).
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
            return tokens;

        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool hasContent = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];

            if (c == '\\')
            {
                int backslashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    backslashes++;
                    i++;
                }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    // Backslashes directly before a quote: 2n+1 → n backslashes + literal
                    // quote; 2n → n backslashes + quote toggles quoting. The quote itself
                    // is consumed by the for-loop increment, not here.
                    current.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 1)
                        current.Append('"');
                    else
                        inQuotes = !inQuotes;
                    hasContent = true;
                }
                else
                {
                    current.Append('\\', backslashes);
                    hasContent = true;
                    i--;
                }
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                hasContent = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasContent)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasContent = false;
                }
            }
            else
            {
                current.Append(c);
                hasContent = true;
            }
        }

        if (hasContent)
            tokens.Add(current.ToString());

        return tokens;
    }
}
