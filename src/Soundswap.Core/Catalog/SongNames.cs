using System.Globalization;
using System.Text;

namespace Soundswap.Core.Catalog;

/// <summary>Names of one song as Orchestrion's community list has them.</summary>
public sealed record SongNameRow(int Id, string Name, string AlternateName, string SpecialName, string Locations, string Info);

/// <summary>
/// Reads the song lists the Orchestrion plugin ships (MIT): <c>"ID","Title","Alt Title","Special Mode Title",
/// "Locations","Comments"</c>, and <c>"ID","Duration"</c> in seconds. Quoted CSV with "" escapes and line breaks
/// inside quotes.
/// </summary>
public static class SongNames
{
    public static Dictionary<int, SongNameRow> ReadNames(string csv)
    {
        var rows = new Dictionary<int, SongNameRow>();
        foreach (var fields in ReadCsv(csv).Skip(1))
        {
            if (fields.Count < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            string F(int i) => i < fields.Count ? fields[i].Trim() : "";
            var name = F(1);
            if (name is "None" or "Null BGM" or "test") name = "";
            rows[id] = new SongNameRow(id, name, F(2), F(3), F(4), F(5));
        }
        return rows;
    }

    public static Dictionary<int, TimeSpan> ReadDurations(string csv)
    {
        var rows = new Dictionary<int, TimeSpan>();
        foreach (var fields in ReadCsv(csv).Skip(1))
        {
            if (fields.Count < 2) continue;
            if (int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                rows[id] = TimeSpan.FromSeconds(seconds);
        }
        return rows;
    }

    public static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = [];
                    break;
                case '﻿' when field.Length == 0 && row.Count == 0: break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
