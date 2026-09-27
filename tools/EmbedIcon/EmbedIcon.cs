// EmbedIcon.cs — standalone C# tool (net8.0)
// Embeds a .ico file as Win32 icon resources (RT_ICON + RT_GROUP_ICON) into
// an existing PE using UpdateResource. Works on any .NET 8 exe regardless of
// build system. No external dependencies.
//
// Usage: EmbedIcon.exe <target.exe> <icon.ico>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

if (args.Length < 2)
{
    Console.WriteLine("Usage: EmbedIcon <exe> <ico>");
    return 1;
}

string exePath  = args[0];
string icoPath  = args[1];

if (!File.Exists(exePath)) { Console.Error.WriteLine($"Exe not found: {exePath}"); return 1; }
if (!File.Exists(icoPath)) { Console.Error.WriteLine($"Ico not found: {icoPath}"); return 1; }

// ── Parse .ico file ───────────────────────────────────────────────────────────
var icoData = File.ReadAllBytes(icoPath);
using var ms = new MemoryStream(icoData);
using var br = new BinaryReader(ms);

br.ReadUInt16(); // reserved
ushort type  = br.ReadUInt16(); // 1 = icon
ushort count = br.ReadUInt16();

var entries = new List<(byte w, byte h, byte colorCount, byte reserved,
                         ushort planes, ushort bitCount,
                         uint byteCount, uint offset)>();

for (int i = 0; i < count; i++)
{
    byte w          = br.ReadByte();
    byte h          = br.ReadByte();
    byte colorCount = br.ReadByte();
    byte reserved   = br.ReadByte();
    ushort planes   = br.ReadUInt16();
    ushort bitCount = br.ReadUInt16();
    uint byteCount  = br.ReadUInt32();
    uint offset     = br.ReadUInt32();
    entries.Add((w, h, colorCount, reserved, planes, bitCount, byteCount, offset));
}

// ── Build RT_GROUP_ICON blob ───────────────────────────────────────────────────
// GRPICONDIR header (6 bytes) + GRPICONDIRENTRY (14 bytes each)
using var grpMs = new MemoryStream();
using var grpBw = new BinaryWriter(grpMs);

grpBw.Write((ushort)0);     // reserved
grpBw.Write((ushort)1);     // type = 1 (icon)
grpBw.Write((ushort)count);

for (int i = 0; i < count; i++)
{
    var e = entries[i];
    grpBw.Write(e.w);
    grpBw.Write(e.h);
    grpBw.Write(e.colorCount);
    grpBw.Write(e.reserved);
    grpBw.Write(e.planes);
    grpBw.Write(e.bitCount);
    grpBw.Write(e.byteCount);
    grpBw.Write((ushort)(i + 1)); // icon ID (1-based)
}
grpBw.Flush();
byte[] groupData = grpMs.ToArray();

// ── Write resources into exe ──────────────────────────────────────────────────
const ushort LANG_NEUTRAL    = 0x0000;
IntPtr RT_ICON              = (IntPtr)3;
IntPtr RT_GROUP_ICON        = (IntPtr)14;

IntPtr hUpdate = BeginUpdateResource(exePath, false);
if (hUpdate == IntPtr.Zero)
{
    Console.Error.WriteLine($"BeginUpdateResource failed: {Marshal.GetLastWin32Error()}");
    return 1;
}

bool ok = true;

// Write each RT_ICON (ID = 1-based index)
for (int i = 0; i < count; i++)
{
    var e = entries[i];
    int len = (int)e.byteCount;
    byte[] iconData = new byte[len];
    Array.Copy(icoData, e.offset, iconData, 0, len);

    var pinned = GCHandle.Alloc(iconData, GCHandleType.Pinned);
    try
    {
        ok &= UpdateResource(hUpdate, RT_ICON, (IntPtr)(i + 1),
                             LANG_NEUTRAL, pinned.AddrOfPinnedObject(), (uint)len);
    }
    finally { pinned.Free(); }
}

// Write RT_GROUP_ICON (ID = 1)
{
    var pinned = GCHandle.Alloc(groupData, GCHandleType.Pinned);
    try
    {
        ok &= UpdateResource(hUpdate, RT_GROUP_ICON, (IntPtr)1,
                             LANG_NEUTRAL, pinned.AddrOfPinnedObject(), (uint)groupData.Length);
    }
    finally { pinned.Free(); }
}

if (!EndUpdateResource(hUpdate, false))
{
    Console.Error.WriteLine($"EndUpdateResource failed: {Marshal.GetLastWin32Error()}");
    return 1;
}

if (!ok) { Console.Error.WriteLine("One or more UpdateResource calls failed."); return 1; }

Console.WriteLine($"Icon embedded: {count} image(s) → {Path.GetFileName(exePath)}");
return 0;

// ── P/Invoke ──────────────────────────────────────────────────────────────────

[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
    ushort wLanguage, IntPtr lpData, uint cbData);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
