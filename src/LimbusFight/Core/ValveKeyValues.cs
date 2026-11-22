using System.Globalization;
using System.Text;

namespace LimbusFight.Core;

/// <summary>
/// One node of a Valve KeyValues document (the text format used by Steam's
/// libraryfolders.vdf and appmanifest_*.acf files).
/// A node is either a leaf (<see cref="Value"/> set) or a section (<see cref="Children"/>).
/// Key lookups are case-insensitive, matching how Steam itself reads these files.
/// </summary>
public sealed class KvNode
{
    private readonly List<KvNode> _children = new();

    public KvNode(string key, string? value = null)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>Leaf value, or null when this node is a section.</summary>
    public string? Value { get; }

    public bool IsSection => Value is null;

    public IReadOnlyList<KvNode> Children => _children;

    internal void Add(KvNode child) => _children.Add(child);

    /// <summary>First direct child with this key (case-insensitive), or null.</summary>
    public KvNode? Child(string key)
    {
        foreach (KvNode child in _children)
        {
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Leaf value of a direct child, or null if missing / not a leaf.</summary>
    public string? GetString(string key) => Child(key)?.Value;

    public long? GetInt64(string key)
    {
        string? raw = GetString(key);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    public int? GetInt32(string key)
    {
        string? raw = GetString(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    public override string ToString() => IsSection ? $"\"{Key}\" {{ {_children.Count} children }}" : $"\"{Key}\" \"{Value}\"";
}

/// <summary>
/// Minimal, dependency-free parser for Valve's text KeyValues format.
/// Handles quoted and unquoted tokens, escape sequences (\\ \" \n \t),
/// // line comments, nested sections and [$PLATFORM] conditionals (ignored).
/// </summary>
public static class ValveKeyValues
{
    /// <summary>
    /// Reads a KeyValues file without locking it (Steam may be writing it at the same moment).
    /// Returns a synthetic root whose children are the document's top-level nodes.
    /// </summary>
    public static KvNode ParseFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses KeyValues text. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static KvNode Parse(string text)
    {
        var tokenizer = new Tokenizer(text);
        var root = new KvNode("<root>");
        ParseSectionBody(tokenizer, root, isTopLevel: true);
        return root;
    }

    private static void ParseSectionBody(Tokenizer tokenizer, KvNode parent, bool isTopLevel)
    {
        while (true)
        {
            Token keyToken = tokenizer.Next();

            if (keyToken.Kind == TokenKind.EndOfFile)
            {
                if (!isTopLevel)
                {
                    throw new FormatException($"Unexpected end of file: section \"{parent.Key}\" was never closed.");
                }

                return;
            }

            if (keyToken.Kind == TokenKind.CloseBrace)
            {
                if (isTopLevel)
                {
                    throw new FormatException($"Unexpected '}}' at offset {keyToken.Offset}.");
                }

                return;
            }

            if (keyToken.Kind != TokenKind.String)
            {
                throw new FormatException($"Expected a key at offset {keyToken.Offset}, found '{keyToken.Kind}'.");
            }

            Token valueToken = tokenizer.Next();

            switch (valueToken.Kind)
            {
                case TokenKind.String:
                    parent.Add(new KvNode(keyToken.Text, valueToken.Text));
                    break;

                case TokenKind.OpenBrace:
                    var section = new KvNode(keyToken.Text);
                    ParseSectionBody(tokenizer, section, isTopLevel: false);
                    parent.Add(section);
                    break;

                default:
                    throw new FormatException(
                        $"Expected a value or '{{' after key \"{keyToken.Text}\" at offset {valueToken.Offset}.");
            }
        }
    }

    private enum TokenKind
    {
        String,
        OpenBrace,
        CloseBrace,
        EndOfFile,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Offset);

    private sealed class Tokenizer
    {
        private readonly string _text;
        private int _pos;

        public Tokenizer(string text)
        {
            _text = text;
            _pos = 0;
        }

        public Token Next()
        {
            while (true)
            {
                SkipWhitespaceAndComments();

                if (_pos >= _text.Length)
                {
                    return new Token(TokenKind.EndOfFile, string.Empty, _pos);
                }

                char c = _text[_pos];

                // [$WIN32] / [!$X360] style conditionals: skip entirely.
                if (c == '[')
                {
                    int close = _text.IndexOf(']', _pos);
                    _pos = close < 0 ? _text.Length : close + 1;
                    continue;
                }

                int start = _pos;

                if (c == '{')
                {
                    _pos++;
                    return new Token(TokenKind.OpenBrace, "{", start);
                }

                if (c == '}')
                {
                    _pos++;
                    return new Token(TokenKind.CloseBrace, "}", start);
                }

                if (c == '"')
                {
                    return new Token(TokenKind.String, ReadQuoted(), start);
                }

                return new Token(TokenKind.String, ReadUnquoted(), start);
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];

                if (char.IsWhiteSpace(c))
                {
                    _pos++;
                    continue;
                }

                if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
                {
                    int newline = _text.IndexOf('\n', _pos);
                    _pos = newline < 0 ? _text.Length : newline + 1;
                    continue;
                }

                break;
            }
        }

        private string ReadQuoted()
        {
            int openOffset = _pos;
            _pos++; // opening quote
            var sb = new StringBuilder();

            while (_pos < _text.Length)
            {
                char c = _text[_pos++];

                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c == '\\' && _pos < _text.Length)
                {
                    char escaped = _text[_pos++];
                    switch (escaped)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        default:
                            // Unknown escape: keep it literally (Steam does the same).
                            sb.Append('\\').Append(escaped);
                            break;
                    }

                    continue;
                }

                sb.Append(c);
            }

            throw new FormatException($"Unterminated quoted string starting at offset {openOffset}.");
        }

        private string ReadUnquoted()
        {
            int start = _pos;

            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (char.IsWhiteSpace(c) || c == '"' || c == '{' || c == '}')
                {
                    break;
                }

                _pos++;
            }

            return _text.Substring(start, _pos - start);
        }
    }
}
