using System.Diagnostics;
using System.Text;
using ValidateJson;

bool open = args.Contains("--open");
int htmlIdx = Array.IndexOf(args, "--html");
bool html = htmlIdx >= 0 || open;
string? htmlArg = htmlIdx >= 0 && htmlIdx + 1 < args.Length && !args[htmlIdx + 1].StartsWith("--") ? args[htmlIdx + 1] : null;
var positional = args.Where(a => !a.StartsWith("--") && a != htmlArg).ToList();

if (positional.Count == 0 || args.Contains("--help"))
{
    Console.WriteLine("Usage: ValidateJson <file.json> [--html [report.html]] [--open]");
    Console.WriteLine("Validates JSON (// and /* */ comments allowed) and prints errors to the console.");
    Console.WriteLine("  --html [path]  also write an HTML report (default: <file>.report.html)");
    Console.WriteLine("  --open         write the HTML report and open it in the browser");
    return 2;
}

string path = positional[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

string text = File.ReadAllText(path);
var errors = JsonValidator.Validate(text);


if (errors.Count == 0)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"{path}: valid JSON");
    Console.ResetColor();
}
else
{
    var starts = JsonValidator.LineStarts(text);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"{path}: {errors.Count} error(s)");
    foreach (var e in errors)
    {
        int ls = starts[e.Line - 1], le = ls;
        while (le < text.Length && text[le] is not ('\n' or '\r')) le++;
        string line = text[ls..le];
        string pad = new(line.Take(e.Column - 1).Select(c => c == '\t' ? '\t' : ' ').ToArray());
        int caretLen = Math.Max(1, Math.Min(e.Length, line.Length - (e.Column - 1)));
        Console.WriteLine($"  line {e.Line}, column {e.Column}: {e.Message}");
        Console.WriteLine($"    {line}");
        Console.WriteLine($"    {pad}{new string('^', caretLen)}");
    }
    Console.ResetColor();
}

if (html)
{
    string reportPath = Path.GetFullPath(htmlArg ?? Path.ChangeExtension(path, ".report.html"));
    File.WriteAllText(reportPath, HtmlReport.Render(path, text, errors), new UTF8Encoding(false));
    Console.WriteLine($"HTML report: {reportPath}");
    if (open) Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
}
return errors.Count == 0 ? 0 : 1;
