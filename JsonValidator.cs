using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ValidateJson;

public record JsonError(int Position, int Length, int Line, int Column, string Message);

/// <summary>
/// Recursive-descent JSON validator. Accepts // and /* */ comments and reports every
/// error it can find (with simple recovery) using 1-based line and column.
/// </summary>
public sealed class JsonValidator
{
    sealed class ParseFailure(int pos, int len, string message) : Exception(message)
    {
        public int Pos { get; } = pos;
        public int Len { get; } = len;
    }

    static readonly Regex NumberRx = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$");

    readonly string _s;
    readonly List<int> _lineStarts;
    readonly List<JsonError> _errors = [];
    int _p;

    JsonValidator(string text)
    {
        _s = text;
        _lineStarts = LineStarts(text);
    }

    public static List<JsonError> Validate(string text)
    {
        var v = new JsonValidator(text);
        v.Run();
        return [.. v._errors.OrderBy(e => e.Position)];
    }

    public static List<int> LineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            if (text[i] is '\r' or '\n') starts.Add(i + 1);
        }
        return starts;
    }

    void Run()
    {
        SkipTrivia();
        if (_p >= _s.Length)
        {
            Report(_p, 0, "Empty document, expected a JSON value");
            return;
        }

        try { ParseValue(); }
        catch (ParseFailure f) { Report(f.Pos, f.Len, f.Message); return; }

        SkipTrivia();
        if (_p < _s.Length)
            Report(_p, 1, "Unexpected content after the end of the JSON value");
    }

    void Report(int pos, int len, string message)
    {
        if (_errors.Any(e => e.Position == pos)) return;
        int idx = _lineStarts.BinarySearch(pos);
        if (idx < 0) idx = ~idx - 1;
        _errors.Add(new JsonError(pos, len, idx + 1, pos - _lineStarts[idx] + 1, message));
    }

    [DoesNotReturn]
    static void Fail(int pos, int len, string message) => throw new ParseFailure(pos, len, message);

    void SkipTrivia()
    {
        while (_p < _s.Length)
        {
            char c = _s[_p];
            if (char.IsWhiteSpace(c)) _p++;
            else if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '/')
            {
                while (_p < _s.Length && _s[_p] is not ('\n' or '\r')) _p++;
            }
            else if (c == '/' && _p + 1 < _s.Length && _s[_p + 1] == '*')
            {
                int end = _s.IndexOf("*/", _p + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    Report(_p, 2, "Unterminated block comment, missing '*/'");
                    _p = _s.Length;
                }
                else _p = end + 2;
            }
            else return;
        }
    }

    void ParseValue()
    {
        SkipTrivia();
        if (_p >= _s.Length) Fail(_p, 0, "Unexpected end of input, expected a value");

        char c = _s[_p];
        switch (c)
        {
            case '{': ParseObject(); break;
            case '[': ParseArray(); break;
            case '"': ParseString(); break;
            case '\'': Fail(_p, 1, "Strings must use double quotes, not single quotes"); break;
            default:
                if (c == '-' || char.IsAsciiDigit(c)) ParseNumber();
                else if (char.IsAsciiLetter(c)) ParseLiteral();
                else Fail(_p, 1, $"Unexpected character '{c}', expected a value");
                break;
        }
    }

    void ParseObject()
    {
        int open = _p++;
        var keys = new HashSet<string>();
        int lastComma = -1;

        while (true)
        {
            SkipTrivia();
            if (_p >= _s.Length)
            {
                Report(open, 1, "Unterminated object, expected '}' before end of input");
                return;
            }
            if (_s[_p] == '}')
            {
                if (lastComma >= 0) Report(lastComma, 1, "Trailing comma is not allowed");
                _p++;
                return;
            }
            lastComma = -1;

            try
            {
                if (_s[_p] != '"') Fail(_p, 1, "Expected a property name in double quotes");
                int keyStart = _p;
                string key = ParseString();
                if (!keys.Add(key)) Report(keyStart, _p - keyStart, $"Duplicate property name '{key}'");
                SkipTrivia();
                if (_p >= _s.Length || _s[_p] != ':') Fail(_p, 1, "Expected ':' after property name");
                _p++;
                ParseValue();
            }
            catch (ParseFailure f)
            {
                Report(f.Pos, f.Len, f.Message);
                Resync('}');
            }

            SkipTrivia();
            if (_p >= _s.Length)
            {
                Report(open, 1, "Unterminated object, expected '}' before end of input");
                return;
            }
            char c = _s[_p];
            if (c == ',') { lastComma = _p++; continue; }
            if (c == '}') { _p++; return; }
            if (c == '"') { Report(_p, 1, "Missing comma between properties"); continue; }

            Report(_p, 1, $"Expected ',' or '}}' but found '{c}'");
            Resync('}');
            SkipTrivia();
            if (_p < _s.Length && _s[_p] == ',') lastComma = _p++;
        }
    }

    void ParseArray()
    {
        int open = _p++;
        int lastComma = -1;

        while (true)
        {
            SkipTrivia();
            if (_p >= _s.Length)
            {
                Report(open, 1, "Unterminated array, expected ']' before end of input");
                return;
            }
            if (_s[_p] == ']')
            {
                if (lastComma >= 0) Report(lastComma, 1, "Trailing comma is not allowed");
                _p++;
                return;
            }
            lastComma = -1;

            try { ParseValue(); }
            catch (ParseFailure f)
            {
                Report(f.Pos, f.Len, f.Message);
                Resync(']');
            }

            SkipTrivia();
            if (_p >= _s.Length)
            {
                Report(open, 1, "Unterminated array, expected ']' before end of input");
                return;
            }
            char c = _s[_p];
            if (c == ',') { lastComma = _p++; continue; }
            if (c == ']') { _p++; return; }
            if (c is '"' or '{' or '[' or '-' || char.IsAsciiLetterOrDigit(c))
            {
                Report(_p, 1, "Missing comma between array elements");
                continue;
            }

            Report(_p, 1, $"Expected ',' or ']' but found '{c}'");
            Resync(']');
            SkipTrivia();
            if (_p < _s.Length && _s[_p] == ',') lastComma = _p++;
        }
    }

    string ParseString()
    {
        int start = _p++;
        while (true)
        {
            if (_p >= _s.Length || _s[_p] is '\n' or '\r')
                Fail(start, 1, "Unterminated string, missing closing '\"'");

            char c = _s[_p];
            if (c == '"')
            {
                _p++;
                return _s[(start + 1)..(_p - 1)];
            }
            if (c == '\\')
            {
                if (_p + 1 >= _s.Length || _s[_p + 1] is '\n' or '\r')
                {
                    Report(_p, 1, "Invalid escape sequence at end of line");
                    _p++;
                    continue;
                }
                char e = _s[_p + 1];
                if ("\"\\/bfnrt".Contains(e)) _p += 2;
                else if (e == 'u')
                {
                    int digits = 0;
                    while (digits < 4 && _p + 2 + digits < _s.Length && char.IsAsciiHexDigit(_s[_p + 2 + digits])) digits++;
                    if (digits < 4) Report(_p, 2 + digits, "Invalid \\u escape, expected 4 hex digits");
                    _p += 2 + digits;
                }
                else
                {
                    Report(_p, 2, $"Invalid escape sequence '\\{e}'");
                    _p += 2;
                }
                continue;
            }
            if (c < ' ') Report(_p, 1, "Control character in string must be escaped");
            _p++;
        }
    }

    void ParseNumber()
    {
        int start = _p;
        while (_p < _s.Length && (char.IsAsciiLetterOrDigit(_s[_p]) || _s[_p] is '+' or '-' or '.')) _p++;
        string tok = _s[start.._p];
        if (NumberRx.IsMatch(tok)) return;

        string hint = Regex.IsMatch(tok, "^-?0[0-9]") ? " (leading zeros are not allowed)" : "";
        Fail(start, tok.Length, $"Invalid number '{tok}'{hint}");
    }

    void ParseLiteral()
    {
        int start = _p;
        while (_p < _s.Length && (char.IsAsciiLetterOrDigit(_s[_p]) || _s[_p] == '_')) _p++;
        string tok = _s[start.._p];
        if (tok is "true" or "false" or "null") return;

        string lower = tok.ToLowerInvariant();
        if (lower is "true" or "false" or "null")
            Fail(start, tok.Length, $"Invalid value '{tok}', literals are case-sensitive (use '{lower}')");
        Fail(start, tok.Length, $"Invalid value '{tok}', expected a string, number, object, array, true, false or null");
    }

    /// <summary>Skips to the next ',' or the closing token of the current container (not consumed).</summary>
    void Resync(char close)
    {
        int depth = 0;
        while (true)
        {
            SkipTrivia();
            if (_p >= _s.Length) return;
            char c = _s[_p];
            if (c == '"')
            {
                _p++;
                while (_p < _s.Length && _s[_p] is not ('"' or '\n' or '\r'))
                    _p += _s[_p] == '\\' ? 2 : 1;
                if (_p < _s.Length && _s[_p] == '"') _p++;
                continue;
            }
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']')
            {
                if (depth > 0) depth--;
                else if (c == close) return;
            }
            else if (c == ',' && depth == 0) return;
            _p++;
        }
    }
}
