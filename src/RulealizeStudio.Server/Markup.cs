// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text;

namespace RulealizeStudio.Server;

/// <summary>
/// A XAML text as its elements, each with where it is in the text: what a designer edits is the
/// text, so every element is known by the characters it spans, and an edit is a span replaced.
/// </summary>
/// <remarks>
/// Reads XML and nothing of what it means: which type an element is, which property an attribute
/// sets, is the <see cref="Designer"/>'s to ask of the controls the build loads. Comments, the
/// declaration and character data are stepped over and left where they are.
/// </remarks>
internal sealed class Markup
{
    private readonly string _text;
    private int _at;

    private Markup(string text) => _text = text;

    /// <summary>The text read.</summary>
    public string Text => _text;

    /// <summary>The root element.</summary>
    public MarkupElement Root { get; private set; } = null!;

    /// <summary>Whether the text ends its lines as Windows does.</summary>
    public string NewLine => _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Reads a XAML text.</summary>
    /// <exception cref="FormatException">The text is not well-formed XML.</exception>
    public static Markup Read(string text)
    {
        Markup markup = new(text);
        markup.Root = markup.Document();
        return markup;
    }

    /// <summary>The whitespace a line starts with, for the line a place in the text is on.</summary>
    public string IndentAt(int at)
    {
        int start = LineStart(at);
        int end = start;
        while (end < _text.Length && _text[end] is ' ' or '\t')
        {
            end++;
        }

        return _text[start..end];
    }

    /// <summary>Where the line a place in the text is on starts.</summary>
    public int LineStart(int at)
    {
        int start = at;
        while (start > 0 && _text[start - 1] is not ('\n' or '\r'))
        {
            start--;
        }

        return start;
    }

    /// <summary>Whether nothing but whitespace comes before a place on its line.</summary>
    public bool StartsLine(int at) => string.IsNullOrWhiteSpace(_text[LineStart(at)..at]);

    /// <summary>Where the line a place is on ends, past its line break, where nothing but whitespace follows the place; otherwise the place.</summary>
    public int PastLine(int at)
    {
        int end = at;
        while (end < _text.Length && _text[end] is ' ' or '\t')
        {
            end++;
        }

        if (end < _text.Length && _text[end] == '\r')
        {
            end++;
        }

        if (end < _text.Length && _text[end] == '\n')
        {
            return end + 1;
        }

        return end == _text.Length ? end : at;
    }

    private MarkupElement Document()
    {
        MarkupElement? root = null;
        while (true)
        {
            SkipMisc();
            if (_at >= _text.Length)
            {
                return root ?? throw Wrong("There is no element.");
            }

            if (root is not null)
            {
                throw Wrong("There is more than one root element.");
            }

            root = Element(parent: null, new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    /// <summary>Whitespace, comments, the declaration and a document type, between elements.</summary>
    private void SkipMisc()
    {
        while (true)
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
            {
                _at++;
            }

            if (Skip("<!--", "-->") || Skip("<?", "?>") || Skip("<!DOCTYPE", ">"))
            {
                continue;
            }

            if (_at < _text.Length && _text[_at] != '<')
            {
                throw Wrong("There is text outside the root element.");
            }

            return;
        }
    }

    private bool Skip(string open, string close)
    {
        if (string.CompareOrdinal(_text, _at, open, 0, open.Length) != 0)
        {
            return false;
        }

        int end = _text.IndexOf(close, _at + open.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw Wrong($"'{open}' is not closed.");
        }

        _at = end + close.Length;
        return true;
    }

    private MarkupElement Element(MarkupElement? parent, IReadOnlyDictionary<string, string> scope)
    {
        int start = _at;
        _at++;
        string name = Name();
        int nameEnd = _at;

        List<MarkupAttribute> attributes = [];
        bool empty;
        while (true)
        {
            int before = _at;
            SkipSpace();
            if (_at >= _text.Length)
            {
                throw Wrong($"'<{name}' is not closed.");
            }

            if (_text[_at] == '/')
            {
                Expect("/>");
                empty = true;
                break;
            }

            if (_text[_at] == '>')
            {
                _at++;
                empty = false;
                break;
            }

            if (_at == before)
            {
                throw Wrong($"'<{name}' has no space before an attribute.");
            }

            int attributeStart = _at;
            string attribute = Name();
            SkipSpace();
            Expect("=");
            SkipSpace();
            char quote = _at < _text.Length ? _text[_at] : '\0';
            if (quote is not ('"' or '\''))
            {
                throw Wrong($"'{attribute}' has no quoted value.");
            }

            int valueStart = ++_at;
            int valueEnd = _text.IndexOf(quote, valueStart);
            if (valueEnd < 0)
            {
                throw Wrong($"The value of '{attribute}' is not closed.");
            }

            _at = valueEnd + 1;
            attributes.Add(new MarkupAttribute(attribute, attributeStart, _at, valueStart, valueEnd, Decode(_text[valueStart..valueEnd])));
        }

        Dictionary<string, string> inScope = new(scope, StringComparer.Ordinal);
        foreach (MarkupAttribute attribute in attributes)
        {
            if (attribute.Name == "xmlns")
            {
                inScope[string.Empty] = attribute.Value;
            }
            else if (attribute.Name.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                inScope[attribute.Name["xmlns:".Length..]] = attribute.Value;
            }
        }

        MarkupElement element = new(name, start, nameEnd, _at, empty, attributes, parent, inScope);
        if (empty)
        {
            element.Close(_at, _at);
            return element;
        }

        StringBuilder text = new();
        while (true)
        {
            if (_at >= _text.Length)
            {
                throw Wrong($"'<{name}>' is not closed.");
            }

            if (string.CompareOrdinal(_text, _at, "</", 0, 2) == 0)
            {
                int closeStart = _at;
                _at += 2;
                string closed = Name();
                SkipSpace();
                Expect(">");
                if (closed != name)
                {
                    throw Wrong($"'<{name}>' is closed by '</{closed}>'.");
                }

                element.Close(closeStart, _at);
                element.Words = element.Children.Count == 0 && text.ToString().Trim() is { Length: > 0 } said ? said : null;
                return element;
            }

            if (Skip("<!--", "-->") || Skip("<?", "?>"))
            {
                continue;
            }

            if (string.CompareOrdinal(_text, _at, "<![CDATA[", 0, 9) == 0)
            {
                int from = _at + 9;
                Skip("<![CDATA[", "]]>");
                text.Append(_text, from, _at - 3 - from);
                continue;
            }

            if (_text[_at] == '<')
            {
                element.Children.Add(Element(element, inScope));
                continue;
            }

            int next = _text.IndexOf('<', _at);
            next = next < 0 ? _text.Length : next;
            text.Append(Decode(_text[_at..next]));
            _at = next;
        }
    }

    private string Name()
    {
        int start = _at;
        while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] is '_' or ':' or '.' or '-'))
        {
            _at++;
        }

        if (_at == start)
        {
            throw Wrong("A name was expected.");
        }

        return _text[start.._at];
    }

    private void SkipSpace()
    {
        while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
        {
            _at++;
        }
    }

    private void Expect(string what)
    {
        if (string.CompareOrdinal(_text, _at, what, 0, what.Length) != 0)
        {
            throw Wrong($"'{what}' was expected.");
        }

        _at += what.Length;
    }

    private FormatException Wrong(string what)
    {
        int line = 1 + _text.AsSpan(0, Math.Min(_at, _text.Length)).Count('\n');
        return new FormatException($"Line {line}: {what}");
    }

    /// <summary>A value as XML means it, its entities read.</summary>
    private static string Decode(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        StringBuilder decoded = new();
        for (int i = 0; i < value.Length; i++)
        {
            int end = value[i] == '&' ? value.IndexOf(';', i) : -1;
            if (end < 0)
            {
                decoded.Append(value[i]);
                continue;
            }

            string entity = value[(i + 1)..end];
            string? said = entity switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                ['#', 'x', .. string hex] when int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code) => char.ConvertFromUtf32(code),
                ['#', .. string number] when int.TryParse(number, out int code) => char.ConvertFromUtf32(code),
                _ => null,
            };

            if (said is null)
            {
                decoded.Append(value[i]);
                continue;
            }

            decoded.Append(said);
            i = end;
        }

        return decoded.ToString();
    }

    /// <summary>A value written as an attribute's, between double quotes: what XML would read otherwise escaped, and a leading brace not taken for a markup extension.</summary>
    public static string Attribute(string value)
    {
        string escaped = value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
        return escaped.StartsWith('{') ? "{}" + escaped : escaped;
    }
}

/// <summary>An element of a XAML text, and the characters it spans.</summary>
internal sealed class MarkupElement(
    string name,
    int start,
    int nameEnd,
    int openEnd,
    bool empty,
    IReadOnlyList<MarkupAttribute> attributes,
    MarkupElement? parent,
    IReadOnlyDictionary<string, string> scope)
{
    /// <summary>The name as written, with its prefix.</summary>
    public string Name { get; } = name;

    /// <summary>The name without its prefix.</summary>
    public string LocalName => Name[(Name.IndexOf(':', StringComparison.Ordinal) + 1)..];

    /// <summary>The namespace the name is in, as the declarations in scope say.</summary>
    public string? Namespace => Scope.GetValueOrDefault(Name.Contains(':', StringComparison.Ordinal) ? Name[..Name.IndexOf(':', StringComparison.Ordinal)] : string.Empty);

    /// <summary>The namespaces declared where this element is, by prefix; the default one under the empty prefix.</summary>
    public IReadOnlyDictionary<string, string> Scope { get; } = scope;

    /// <summary>Whether this is a property element, <c>Grid.Styles</c>, rather than an object.</summary>
    public bool IsProperty => LocalName.Contains('.', StringComparison.Ordinal);

    /// <summary>Where its start tag begins: its <c>&lt;</c>.</summary>
    public int Start { get; } = start;

    /// <summary>Where its name ends, in its start tag.</summary>
    public int NameEnd { get; } = nameEnd;

    /// <summary>Just past its start tag.</summary>
    public int OpenEnd { get; } = openEnd;

    /// <summary>Whether it closes itself, <c>&lt;Button /&gt;</c>.</summary>
    public bool Empty { get; } = empty;

    /// <summary>Where its end tag begins; where it closes itself, just past it.</summary>
    public int CloseStart { get; private set; }

    /// <summary>Just past the whole element.</summary>
    public int End { get; private set; }

    /// <summary>Its attributes, as written.</summary>
    public IReadOnlyList<MarkupAttribute> Attributes { get; } = attributes;

    /// <summary>The elements inside it, property elements among them.</summary>
    public List<MarkupElement> Children { get; } = [];

    /// <summary>The element it is in.</summary>
    public MarkupElement? Parent { get; } = parent;

    /// <summary>The text inside it where it holds no element, read; otherwise null.</summary>
    public string? Words { get; set; }

    /// <summary>The attribute of this name, where it has one.</summary>
    public MarkupAttribute? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name);

    /// <summary>Where its start tag's last character before <c>&gt;</c> or <c>/&gt;</c> is, with no space before it.</summary>
    public int AttributesEnd => Attributes.Count == 0 ? NameEnd : Attributes[^1].End;

    internal void Close(int closeStart, int end)
    {
        CloseStart = closeStart;
        End = end;
    }
}

/// <summary>An attribute of an element, the characters it spans and its value read.</summary>
/// <param name="Name">The name, with its prefix.</param>
/// <param name="Start">Where its name begins.</param>
/// <param name="End">Just past its closing quote.</param>
/// <param name="ValueStart">Where its value begins, inside the quotes.</param>
/// <param name="ValueEnd">Where its value ends, at the closing quote.</param>
/// <param name="Value">Its value, its entities read.</param>
internal sealed record MarkupAttribute(string Name, int Start, int End, int ValueStart, int ValueEnd, string Value);
