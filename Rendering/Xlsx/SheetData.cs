using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocGlance.Rendering.Xlsx;

/// <summary>
/// A single cell's display data, deserialized from the Rust FFI JSON.
/// </summary>
public sealed class CellData
{
    [JsonPropertyName("r")] public int Row  { get; set; }
    [JsonPropertyName("c")] public int Col  { get; set; }
    [JsonPropertyName("v")] public string? Value   { get; set; }
    [JsonPropertyName("bg")] public string? Bg     { get; set; }
    [JsonPropertyName("fg")] public string? Fg     { get; set; }
    [JsonPropertyName("bold")]   public bool Bold   { get; set; }
    [JsonPropertyName("italic")] public bool Italic { get; set; }
    [JsonPropertyName("h")]   public string? HAlign { get; set; }
    [JsonPropertyName("vt")]  public string? VAlign { get; set; }
    [JsonPropertyName("wrap")]  public bool Wrap    { get; set; }
    [JsonPropertyName("brd")] public string? Borders { get; set; }
}

/// <summary>
/// A merge range deserialized from JSON.
/// </summary>
public sealed class MergeData
{
    [JsonPropertyName("r")]  public int Row     { get; set; }
    [JsonPropertyName("c")]  public int Col     { get; set; }
    [JsonPropertyName("rs")] public int RowSpan { get; set; }
    [JsonPropertyName("cs")] public int ColSpan { get; set; }
}

/// <summary>
/// Full sheet data returned by SpreadsheetDocument.GetSheetDataJsonAsync.
/// </summary>
public sealed class SheetData
{
    [JsonPropertyName("name")]             public string  Name             { get; set; } = string.Empty;
    [JsonPropertyName("rowCount")]         public int     RowCount         { get; set; }
    [JsonPropertyName("colCount")]         public int     ColCount         { get; set; }
    [JsonPropertyName("defaultColWidth")]  public double  DefaultColWidth  { get; set; } = 64.0;
    [JsonPropertyName("defaultRowHeight")] public double  DefaultRowHeight { get; set; } = 20.0;
    [JsonPropertyName("freezeRow")]        public int     FreezeRow        { get; set; }
    [JsonPropertyName("freezeCol")]        public int     FreezeCol        { get; set; }
    [JsonPropertyName("showGridlines")]    public bool    ShowGridlines    { get; set; } = true;
    [JsonPropertyName("cols")] public Dictionary<string, double> Cols { get; set; } = new();
    [JsonPropertyName("rows")] public Dictionary<string, double> Rows { get; set; } = new();
    [JsonPropertyName("merges")] public List<MergeData>  Merges { get; set; } = new();
    [JsonPropertyName("cells")]  public List<CellData>   Cells  { get; set; } = new();

    public double GetColWidth(int col)
        => Cols.TryGetValue(col.ToString(), out var w) ? w : DefaultColWidth;

    public double GetRowHeight(int row)
        => Rows.TryGetValue(row.ToString(), out var h) ? h : DefaultRowHeight;

    public Dictionary<(int, int), CellData> BuildCellMap()
    {
        var map = new Dictionary<(int, int), CellData>(Cells.Count);
        foreach (var cell in Cells)
            map[(cell.Row, cell.Col)] = cell;
        return map;
    }

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static SheetData? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SheetData>(json, _opts);
        }
        catch
        {
            return null;
        }
    }
}
