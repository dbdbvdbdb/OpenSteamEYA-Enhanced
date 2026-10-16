using System.Text;

namespace SteamEyaWinUI.Services;

internal sealed record Cs2ConfigValue(string Section, string Key, string Value);

/// <summary>Read-only Valve KeyValues text parser for CS2 VCFG and video files.
/// Kept separate from the login VDF writer so viewing settings cannot change login behavior.</summary>
internal static class Cs2SettingsParser
{
    public const int MaxCharacters = 4 * 1024 * 1024;
    public const int MaxEntries = 20000;

    public static IReadOnlyList<Cs2ConfigValue> Parse(string text)
    {
        if (text.Length > MaxCharacters) throw new FormatException("Configuration is too large.");
        return new Parser(text).Parse();
    }

    private enum Kind { Text, Open, Close, End }
    private readonly record struct Token(Kind Kind, string Text = "");

    private sealed class Parser(string text)
    {
        private int _position;
        private readonly List<Cs2ConfigValue> _values = [];

        public IReadOnlyList<Cs2ConfigValue> Parse()
        {
            ReadObject([], 0);
            return _values;
        }

        private void ReadObject(List<string> section, int depth)
        {
            if (depth > 32) throw new FormatException("Configuration nesting is too deep.");
            while (true)
            {
                var key = Next();
                if (key.Kind == Kind.End)
                {
                    if (depth != 0) throw new FormatException("Missing closing brace.");
                    return;
                }
                if (key.Kind == Kind.Close)
                {
                    if (depth == 0) throw new FormatException("Unexpected closing brace.");
                    return;
                }
                if (key.Kind != Kind.Text) throw new FormatException("Expected a key.");
                var value = Next();
                if (value.Kind == Kind.Open)
                {
                    section.Add(key.Text);
                    ReadObject(section, depth + 1);
                    section.RemoveAt(section.Count - 1);
                }
                else if (value.Kind == Kind.Text)
                {
                    if (_values.Count >= MaxEntries) throw new FormatException("Too many settings.");
                    _values.Add(new(string.Join('/', section), key.Text, value.Text));
                }
                else throw new FormatException("Expected a value or object.");
            }
        }

        private Token Next()
        {
            SkipTrivia();
            if (_position == text.Length) return new(Kind.End);
            char c = text[_position++];
            if (c == '{') return new(Kind.Open);
            if (c == '}') return new(Kind.Close);
            if (c == '"')
            {
                var value = new StringBuilder();
                while (_position < text.Length)
                {
                    c = text[_position++];
                    if (c == '"') return new(Kind.Text, value.ToString());
                    if (c == '\\' && _position < text.Length)
                    {
                        var escaped = text[_position++];
                        switch (escaped)
                        {
                            case '"': value.Append('"'); break;
                            case '\\': value.Append('\\'); break;
                            case 'n': value.Append('\n'); break;
                            case 'r': value.Append('\r'); break;
                            case 't': value.Append('\t'); break;
                            default: value.Append('\\').Append(escaped); break;
                        }
                    }
                    else value.Append(c);
                }
                throw new FormatException("Unterminated quoted value.");
            }

            int start = _position - 1;
            while (_position < text.Length && !char.IsWhiteSpace(text[_position]) &&
                   text[_position] is not '{' and not '}' and not '"')
            {
                if (IsComment()) break;
                _position++;
            }
            return new(Kind.Text, text[start.._position]);
        }

        private bool IsComment() => _position + 1 < text.Length && text[_position] == '/' &&
                                    text[_position + 1] is '/' or '*';

        private void SkipTrivia()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]) || (_position == 0 && text[_position] == '\uFEFF'))
                {
                    _position++;
                    continue;
                }
                if (!IsComment()) return;
                bool block = text[_position + 1] == '*';
                _position += 2;
                if (block)
                {
                    int end = text.IndexOf("*/", _position, StringComparison.Ordinal);
                    if (end < 0) throw new FormatException("Unterminated comment.");
                    _position = end + 2;
                }
                else
                {
                    while (_position < text.Length && text[_position] is not '\r' and not '\n') _position++;
                }
            }
        }
    }
}
