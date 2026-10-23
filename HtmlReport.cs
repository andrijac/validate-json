using System.Net;
using System.Text;

namespace ValidateJson;

public static class HtmlReport
{
    public static string Render(string fileName, string text, IReadOnlyList<JsonError> errors)
    {
        var tip = new string?[text.Length + 1];
        foreach (var e in errors)
            for (int i = e.Position; i < Math.Min(e.Position + Math.Max(e.Length, 1), tip.Length); i++)
                tip[i] ??= e.Message;

        var badLines = errors.Select(e => e.Line).ToHashSet();
        var starts = JsonValidator.LineStarts(text);
        string Enc(string s) => WebUtility.HtmlEncode(s);

        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>JSON validation: {{Enc(Path.GetFileName(fileName))}}</title>
            <style>
              :root { --bg:#fff; --fg:#1f2328; --muted:#656d76; --line:#d0d7de; --err:#b91c1c; --err-bg:#fecaca; --bad-bg:#fef2f2; --ok:#15803d; }
              @media (prefers-color-scheme: dark) {
                :root { --bg:#0d1117; --fg:#e6edf3; --muted:#8b949e; --line:#30363d; --err:#fca5a5; --err-bg:#7f1d1d; --bad-bg:#2a1214; --ok:#4ade80; }
              }
              body { background:var(--bg); color:var(--fg); font:15px/1.5 system-ui,sans-serif; margin:0 auto; padding:24px 16px; max-width:1000px; }
              h1 { font-size:20px; margin:0 0 4px; word-break:break-all; }
              .status { font-weight:600; margin-bottom:16px; }
              .status.bad { color:var(--err); } .status.ok { color:var(--ok); }
              table { border-collapse:collapse; width:100%; margin-bottom:24px; }
              th, td { text-align:left; padding:6px 10px; border-bottom:1px solid var(--line); vertical-align:top; }
              th { color:var(--muted); font-weight:600; }
              td.pos { white-space:nowrap; font-family:ui-monospace,Consolas,monospace; }
              td.msg { color:var(--err); font-weight:600; }
              a { color:inherit; }
              .src { border:1px solid var(--line); border-radius:6px; overflow-x:auto; font:13px/1.5 ui-monospace,Consolas,monospace; }
              .line { display:flex; border-left:3px solid transparent; }
              .line.bad { background:var(--bad-bg); border-left-color:var(--err); }
              .no { flex:none; width:4em; padding-right:12px; text-align:right; color:var(--muted); user-select:none; }
              .line.bad .no { color:var(--err); font-weight:700; }
              code { white-space:pre; tab-size:4; min-height:1.5em; font:inherit; }
              .err { color:var(--err); background:var(--err-bg); font-weight:700; text-decoration:underline wavy var(--err); border-radius:2px; }
            </style></head><body>
            <h1>{{Enc(fileName)}}</h1>

            """);

        sb.Append(errors.Count == 0
            ? "<div class=\"status ok\">Valid JSON (comments allowed)</div>\n"
            : $"<div class=\"status bad\">{errors.Count} error{(errors.Count == 1 ? "" : "s")} found</div>\n");

        if (errors.Count > 0)
        {
            sb.Append("<table><tr><th>#</th><th>Line</th><th>Column</th><th>Description</th></tr>\n");
            int n = 0;
            foreach (var e in errors)
                sb.Append($"<tr><td>{++n}</td><td class=\"pos\"><a href=\"#L{e.Line}\">{e.Line}</a></td><td class=\"pos\">{e.Column}</td><td class=\"msg\">{Enc(e.Message)}</td></tr>\n");
            sb.Append("</table>\n");
        }

        sb.Append("<div class=\"src\">\n");
        for (int li = 0; li < starts.Count; li++)
        {
            int start = starts[li], end = start;
            while (end < text.Length && text[end] is not ('\n' or '\r')) end++;

            sb.Append($"<div class=\"line{(badLines.Contains(li + 1) ? " bad" : "")}\" id=\"L{li + 1}\"><span class=\"no\">{li + 1}</span><code>");
            for (int i = start; i < end;)
            {
                int j = i;
                while (j < end && tip[j] == tip[i]) j++;
                string chunk = Enc(text[i..j]);
                sb.Append(tip[i] is null ? chunk : $"<span class=\"err\" title=\"{Enc(tip[i]!)}\">{chunk}</span>");
                i = j;
            }
            if (tip[end] is { } eolTip)
                sb.Append($"<span class=\"err\" title=\"{Enc(eolTip)}\">&#8629;</span>");
            sb.Append("</code></div>\n");
        }
        sb.Append("</div>\n</body></html>\n");
        return sb.ToString();
    }
}
