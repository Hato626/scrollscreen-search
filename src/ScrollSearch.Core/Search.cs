using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScrollSearch.Core;

public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public static Box Union(IEnumerable<Box> boxes)
    {
        var b = boxes.ToArray();
        if (b.Length == 0) return default;
        var x = b.Min(i => i.X); var y = b.Min(i => i.Y);
        return new(x, y, b.Max(i => i.Right) - x, b.Max(i => i.Bottom) - y);
    }
}
public sealed record Word(string Text, Box Bounds);
public sealed record TextLine(string Text, Box Bounds, IReadOnlyList<Word> Words);
public sealed record Frame(IReadOnlyList<TextLine> Lines, int Width, int Height);
public sealed record Match(string Text, string Context, Box Bounds, int LineIndex, int Occurrence);
public sealed record LocatedMatch(Match Match, int Segment, double DocumentY, bool Uncertain);
public sealed record HistoryItem(string Id, DateTimeOffset Time, string Query, string Text, string Context,
    string Screenshot, Box Bounds, int Segment, double DocumentY, bool Uncertain)
{
    public string Summary => $"{Time:HH:mm:ss}  {Text}";
    public string Detail => $"{Context}{(Uncertain ? "  ·  新视野，位置未关联" : "")}";
}

public static class TextSearch
{
    // OCR often inserts spaces between Han characters. Whitespace is intentionally ignored.
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static IReadOnlyList<Match> Find(Frame frame, string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0) return Array.Empty<Match>();
        var result = new List<Match>();
        for (var i = 0; i < frame.Lines.Count; i++)
        {
            var line = frame.Lines[i];
            var tokens = line.Words.Select(w => (Word: w, Text: Normalize(w.Text))).ToArray();
            var haystack = string.Concat(tokens.Select(t => t.Text));
            if (tokens.Length == 0) haystack = Normalize(line.Text);
            var occurrence = 0;
            for (var start = haystack.IndexOf(needle, StringComparison.Ordinal); start >= 0;
                 start = haystack.IndexOf(needle, start + Math.Max(1, needle.Length), StringComparison.Ordinal))
            {
                var boxes = new List<Box>(); var offset = 0;
                foreach (var token in tokens)
                {
                    if (offset < start + needle.Length && offset + token.Text.Length > start) boxes.Add(token.Word.Bounds);
                    offset += token.Text.Length;
                }
                var context = string.Join("\n", frame.Lines.Skip(Math.Max(0, i - 1)).Take(i == 0 ? 2 : 3).Select(l => l.Text));
                result.Add(new(line.Text, context, boxes.Count > 0 ? Box.Union(boxes) : line.Bounds, i, occurrence++));
            }
        }
        return result;
    }
}

/// <summary>Conservative scroll registration. Text alone never identifies a history hit.</summary>
public sealed class ScrollTracker
{
    Frame? previous;
    double offset;
    int segment;
    readonly List<(string Text, int Occurrence, int Segment, double Y, double X)> seen = new();
    public void Reset() { previous = null; offset = 0; segment++; seen.Clear(); }
    public void BreakContinuity() { previous = null; offset = 0; segment++; }
    public IReadOnlyList<LocatedMatch> Update(Frame frame, string query)
    {
        var uncertain = false;
        if (previous != null)
        {
            var translation = EstimateTranslation(previous, frame);
            if (translation is double delta) offset += delta;
            else { segment++; offset = 0; uncertain = true; }
        }
        var output = new List<LocatedMatch>();
        foreach (var hit in TextSearch.Find(frame, query))
        {
            var y = hit.Bounds.Y + offset; var text = TextSearch.Normalize(hit.Text);
            var tolerance = Math.Max(5, Math.Min(12, hit.Bounds.Height * .45));
            if (seen.Any(s => s.Segment == segment && s.Text == text && s.Occurrence == hit.Occurrence &&
                              Math.Abs(s.Y - y) <= tolerance && Math.Abs(s.X - hit.Bounds.X) <= 8)) continue;
            seen.Add((text, hit.Occurrence, segment, y, hit.Bounds.X));
            if (seen.Count > 10_000) seen.RemoveRange(0, 1000);
            output.Add(new(hit, segment, y, uncertain));
        }
        previous = frame;
        return output;
    }
    public static double? EstimateTranslation(Frame before, Frame after)
    {
        if (before.Width != after.Width || before.Height != after.Height) return null;
        var a = before.Lines.GroupBy(l => TextSearch.Normalize(l.Text)).Where(g => g.Key.Length > 2 && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var b = after.Lines.GroupBy(l => TextSearch.Normalize(l.Text)).Where(g => g.Key.Length > 2 && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());
        var shifts = new List<(double Delta, double Y)>();
        foreach (var (key, line) in b)
            if (a.TryGetValue(key, out var prior) && Math.Abs(prior.Bounds.X - line.Bounds.X) < 12)
                shifts.Add((prior.Bounds.Y - line.Bounds.Y, line.Bounds.Y));
        if (shifts.Count < 2)
        {
            // An exactly stable frame with repeated text is still safe to deduplicate in-place.
            if (before.Lines.Count == after.Lines.Count && before.Lines.Count > 0 &&
                before.Lines.Zip(after.Lines).All(p => TextSearch.Normalize(p.First.Text) == TextSearch.Normalize(p.Second.Text) &&
                    Math.Abs(p.First.Bounds.Y - p.Second.Bounds.Y) < 3 && Math.Abs(p.First.Bounds.X - p.Second.Bounds.X) < 3)) return 0;
            return null;
        }
        var best = shifts.Select(s => shifts.Where(v => Math.Abs(v.Delta - s.Delta) < 7).ToArray())
            .OrderByDescending(g => g.Length).ThenByDescending(g => g.Max(v => v.Y) - g.Min(v => v.Y)).First();
        if (best.Length < 2 || best.Length * 2 < shifts.Count) return null;
        // Two fixed header lines must not establish registration for a changed body below them.
        if (best.All(v => v.Y < after.Height * .3) &&
            !before.Lines.Select(l => TextSearch.Normalize(l.Text)).SequenceEqual(after.Lines.Select(l => TextSearch.Normalize(l.Text)))) return null;
        return best.Select(v => v.Delta).Order().ElementAt(best.Length / 2);
    }
}

public static class CoordinateMap
{
    public static Box Scale(Box box, double sourceWidth, double sourceHeight, double targetWidth, double targetHeight) =>
        new(box.X * targetWidth / sourceWidth, box.Y * targetHeight / sourceHeight,
            box.Width * targetWidth / sourceWidth, box.Height * targetHeight / sourceHeight);
    public static Box FollowWindow(Box selection, Box oldWindow, Box newWindow) =>
        new(newWindow.X + (selection.X - oldWindow.X) * newWindow.Width / oldWindow.Width,
            newWindow.Y + (selection.Y - oldWindow.Y) * newWindow.Height / oldWindow.Height,
            selection.Width * newWindow.Width / oldWindow.Width, selection.Height * newWindow.Height / oldWindow.Height);
}

public static class LocalStore
{
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
    public static T? Load<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
    public static string Csv(IEnumerable<HistoryItem> items)
    {
        // Prefix spreadsheet formula characters so exported OCR text cannot become a formula.
        static string Cell(string s)
        {
            if (s.TrimStart().Length > 0 && "=+-@".Contains(s.TrimStart()[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var rows = new List<string> { "time,query,text,context,screenshot,segment,document_y,uncertain" };
        rows.AddRange(items.Select(i => string.Join(",", new[] { i.Time.ToString("O"), i.Query, i.Text, i.Context, i.Screenshot,
            i.Segment.ToString(CultureInfo.InvariantCulture), i.DocumentY.ToString("F2", CultureInfo.InvariantCulture), i.Uncertain.ToString() }.Select(Cell))));
        return string.Join("\r\n", rows) + "\r\n";
    }
}
