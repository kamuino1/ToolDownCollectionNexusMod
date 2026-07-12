using System.Text;

namespace NexusShared;

// Reads/writes the progress CSV (Index,Name,Url,Status,UpdatedAt), quote-escaped, UTF-8 with BOM
public static class CsvStore
{
    public static List<ModEntry> Load(string path)
    {
        var list = new List<ModEntry>();
        var lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) // skip header row
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = ParseLine(lines[i]);
            if (f.Count < 4) continue;
            list.Add(new ModEntry
            {
                Index = int.TryParse(f[0], out var idx) ? idx : list.Count + 1,
                Name = f[1],
                Url = f[2],
                Status = f[3],
                UpdatedAt = f.Count > 4 ? f[4] : ""
            });
        }
        return list;
    }

    public static void Save(string path, IEnumerable<ModEntry> mods)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Index,Name,Url,Status,UpdatedAt");
            foreach (var m in mods)
            {
                sb.AppendLine(string.Join(",",
                    m.Index,
                    Escape(m.Name),
                    Escape(m.Url),
                    Escape(m.Status),
                    Escape(m.UpdatedAt)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not write progress file: " + ex.Message);
        }
    }

    // Parse one CSV line into fields (handles quoted fields with commas / doubled quotes)
    public static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    // Escape a value for CSV (quote it if it contains a comma/quote/newline)
    public static string Escape(string? s)
    {
        s ??= "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
