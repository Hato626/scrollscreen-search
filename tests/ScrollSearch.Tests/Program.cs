using System.IO.Compression;
using ScrollSearch.Core;

var passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
TextLine Line(string text, double y, double x = 10) => new(text, new(x, y, 220, 20), new[] { new Word(text, new(x, y, 220, 20)) });
Frame FrameOf(params TextLine[] lines) => new(lines, 600, 400);
Check("Chinese spaces and Unicode width normalization", TextSearch.Normalize("找 字 Ａ") == "找字A");
var f = FrameOf(Line("Hello TARGET world", 40), Line("another Target row", 100));
Check("case-insensitive literal matching", TextSearch.Find(f, "target").Count == 2);
Check("empty search", TextSearch.Find(f, " ").Count == 0);
Check("cross-word query", TextSearch.Find(new(new[] { new TextLine("hello world", new(0, 0, 80, 20), new[] { new Word("hello", new(0, 0, 30, 20)), new Word("world", new(40, 0, 40, 20)) }) }, 100, 50), "helloworld").Single().Bounds.Width == 80);
var repeated = FrameOf(Line("测试测试", 80)); var tracker = new ScrollTracker();
Check("two occurrences inside one OCR word retained", tracker.Update(repeated, "测试").Count == 2);
Check("stable repeated word deduplicates", tracker.Update(repeated, "测试").Count == 0);
tracker.Reset();
var a = FrameOf(Line("anchor A", 100), Line("TARGET identical", 150), Line("anchor B", 200), Line("anchor C", 300));
var b = FrameOf(Line("anchor A", 20), Line("TARGET identical", 70), Line("anchor B", 120), Line("anchor C", 220), Line("TARGET identical", 340));
Check("first occurrence found", tracker.Update(a, "TARGET").Count == 1);
Check("scroll overlap dedup but identical different row retained", tracker.Update(b, "TARGET").Count == 1);
Check("scroll back to original does not duplicate", tracker.Update(a, "TARGET").Count == 0);
var c = FrameOf(Line("new page heading", 100), Line("TARGET identical", 150), Line("no overlap", 300));
Check("unregistered new viewport retained as uncertain", tracker.Update(c, "TARGET").Single().Uncertain);
var headerBefore = FrameOf(Line("Fixed header one", 10), Line("Fixed header two", 45), Line("TARGET old", 200));
var headerAfter = FrameOf(Line("Fixed header one", 10), Line("Fixed header two", 45), Line("TARGET new", 200));
Check("fixed headers do not register changed body", ScrollTracker.EstimateTranslation(headerBefore, headerAfter) == null);
Check("negative monitor coordinate and DPI follow", CoordinateMap.FollowWindow(new(-1800, 100, 600, 300), new(-1920, 0, 1200, 800), new(0, 0, 1800, 1200)) == new Box(180, 150, 900, 450));
Check("OCR scaled coordinates map to screenshot pixels", CoordinateMap.Scale(new(20, 30, 40, 50), 1000, 500, 2000, 1000) == new Box(40, 60, 80, 100));
var temporary = Path.Combine(Path.GetTempPath(), "scrollsearch-core-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var path = Path.Combine(temporary, "history.json");
    var history = new[] { new HistoryItem("id", DateTimeOffset.UtcNow, "query", "=HYPERLINK(\"evil\")", "line\nwith,comma", "capture.png", new(1, 2, 3, 4), 3, 125.5, true) };
    LocalStore.Save(path, history); var restored = LocalStore.Load<HistoryItem[]>(path)!;
    Check("history persistence round-trip", restored.Single() == history.Single());
    LocalStore.Save(path, Array.Empty<HistoryItem>()); Check("atomic overwrite and no temporary leftovers", LocalStore.Load<HistoryItem[]>(path)!.Length == 0 && !File.Exists(path + ".tmp"));
    var csv = LocalStore.Csv(history); Check("CSV quotes, newlines, formula protection", csv.Contains("\"'=HYPERLINK(\"\"evil\"\")\"") && csv.Contains("\"line\nwith,comma\""));
}
finally { Directory.Delete(temporary, true); }
var loop = new ScanLoop<int>(); var observed = 0; var published = 0; var active = 0; var maxActive = 0; var release = new TaskCompletionSource<int>();
loop.Start(async token => { observed++; active++; maxActive = Math.Max(maxActive, active); var value = await release.Task; active--; return value; }, _ => published++, e => throw e, TimeSpan.FromMilliseconds(5));
loop.Stop(); release.SetResult(7); await loop.Completion; await Task.Delay(30);
Check("stop suppresses in-flight uncooperative OCR result", observed == 1 && published == 0);
loop.Start(async token => { active++; maxActive = Math.Max(maxActive, active); await Task.Delay(12, token); active--; return 1; }, _ => published++, e => throw e, TimeSpan.FromMilliseconds(10));
await Task.Delay(160); loop.Stop(); await loop.Completion;
Check("sequential loop never overlaps captures", maxActive == 1 && published >= 2);
var finalPublished = published; await Task.Delay(60); Check("no events after pause", published == finalPublished);
var errors = 0;
loop.Start(_ => Task.FromException<int>(new IOException("test")), _ => { }, _ => errors++, TimeSpan.Zero);
await loop.Completion; Check("error stops loop and reports once", !loop.Running && errors == 1);
Console.WriteLine($"ALL {passed} CHECKS PASSED");
