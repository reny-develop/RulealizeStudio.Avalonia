// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace RulealizeStudio.Server;

/// <summary>Where each part of a JSON document is written, and which lists an entry may be taken out of or put into.</summary>
/// <remarks>
/// This reads the document as JSON and nothing more, and names no operation. A list is any array
/// outside the document's identity and what it requires, whatever its entries are — a test
/// design's <c>edits</c> among them, where a choice is written.
/// </remarks>
public sealed class DocumentMap
{
    /// <summary>What a document declares about itself, which no rule reads.</summary>
    private static readonly string[] Identity = ["/$schema", "/id", "/version", "/requires", "/uses"];

    private readonly string _text;
    private readonly Dictionary<string, (int Start, int Length)> _spans;

    private DocumentMap(string text, Dictionary<string, (int, int)> spans, ImmutableArray<Listing> lists, string? schema)
    {
        _text = text;
        _spans = spans;
        Lists = lists;
        Schema = schema;
    }

    /// <summary>Gets the lists the document writes, in the order their brackets open, leaving out the document's identity and what it requires.</summary>
    public ImmutableArray<Listing> Lists { get; }

    /// <summary>Gets what the document says it is, in <c>$schema</c>; <see langword="null"/> where it does not say.</summary>
    public string? Schema { get; }

    /// <summary>Gets whether the document says it is a rule set, and so is one to compile rather than any JSON at all.</summary>
    public bool IsRuleSet => Schema?.StartsWith("rulealize/ruleset/", StringComparison.Ordinal) == true;

    /// <summary>Reads a document.</summary>
    /// <param name="text">The document, comments and all.</param>
    /// <returns>The map.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static DocumentMap Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int[] chars = CharIndex(bytes);

        Dictionary<string, (int, int)> spans = [];
        Dictionary<string, string> ops = [];
        Dictionary<string, List<string>> arrays = [];
        List<(string Pointer, string? Text)> values = [];
        Stack<Frame> frames = [];

        Utf8JsonReader reader = new(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                frames.Peek().Key = reader.GetString();
                continue;
            }

            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                Frame done = frames.Pop();
                int start = chars[done.Start];
                spans[done.Pointer] = (start, chars[reader.BytesConsumed] - start);
                continue;
            }

            string pointer = Child(frames);
            int from = (int)reader.TokenStartIndex;
            if (frames.TryPeek(out Frame? parent) && parent.IsArray)
            {
                arrays[parent.Pointer].Add(pointer);
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                bool array = reader.TokenType == JsonTokenType.StartArray;
                if (array)
                {
                    arrays[pointer] = [];
                }

                frames.Push(new Frame(pointer, array, from));
                continue;
            }

            int begin = chars[from];
            int length = chars[reader.BytesConsumed] - begin;
            spans[pointer] = (begin, length);

            string? value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (value is not null && frames.Peek().Key == "op")
            {
                ops[Parent(pointer)] = value;
                continue;
            }

            values.Add((pointer, value));
        }

        ImmutableArray<Listing> lists = [.. arrays
            .Where(a => !IsIdentity(a.Key))
            .OrderBy(a => spans[a.Key].Item1)
            .Select(a => new Listing(
                a.Key,
                Name(a.Key, ops, arrays),
                spans[a.Key].Item1,
                spans[a.Key].Item2,
                [.. a.Value.Select(e => new Entry(
                    e,
                    Name(e, ops, arrays),
                    text.Substring(spans[e].Item1, spans[e].Item2),
                    spans[e].Item1,
                    spans[e].Item2))]))];

        string? schema = values.FirstOrDefault(v => v.Pointer == "/$schema").Text;
        return new DocumentMap(text, spans, lists, schema);
    }

    /// <summary>Says how to take one entry out of its list.</summary>
    /// <param name="pointer">The entry, as <see cref="Entry.Pointer"/> names it.</param>
    /// <returns>
    /// The changes that take it out with the comma that went with it, and the line it stood on
    /// where it stood on lines of its own, so that the list reads as if it had never been there.
    /// A comment beside it stays.
    /// </returns>
    public ImmutableArray<Change> Remove(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        Listing list = Lists.FirstOrDefault(l => l.Entries.Any(e => e.Pointer == pointer))
            ?? throw new ArgumentException($"'{pointer}' is not an entry of a list in this document.", nameof(pointer));
        ImmutableArray<Entry> entries = list.Entries;
        int i = entries.IndexOf(entries.First(e => e.Pointer == pointer));
        Entry entry = entries[i];

        if (entries.Length == 1)
        {
            return [Cut(Lines(entry.Start, End(entry)))];
        }

        if (i < entries.Length - 1)
        {
            Entry next = entries[i + 1];
            return IsSeparator(End(entry), next.Start)
                ? [Cut((entry.Start, next.Start))]
                : [Cut(Lines(entry.Start, Comma(End(entry)) + 1))];
        }

        Entry before = entries[i - 1];
        if (IsSeparator(End(before), entry.Start))
        {
            return [Cut((End(before), End(entry)))];
        }

        return [new Change(Comma(End(before)), 1, ""), Cut(Lines(entry.Start, End(entry)))];
    }

    /// <summary>Says how to put an entry into a list.</summary>
    /// <param name="pointer">The list, as <see cref="Listing.Pointer"/> names it.</param>
    /// <param name="index">Where among its entries the new one goes; the number of entries puts it last.</param>
    /// <param name="text">The entry as it is to be written.</param>
    /// <returns>The change that puts it there, separated and laid out the way the list's own entries are.</returns>
    public Change Insert(string pointer, int index, string text)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(text);

        Listing list = Lists.FirstOrDefault(l => l.Pointer == pointer)
            ?? throw new ArgumentException($"'{pointer}' is not a list in this document.", nameof(pointer));
        ImmutableArray<Entry> entries = list.Entries;
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, entries.Length);

        if (entries.IsEmpty)
        {
            // An empty list laid out over lines gets its entry on a line of its own, one step in
            // from the bracket that closes it; one on a line, in the middle of the space inside.
            int close = list.Start + list.Length - 1;
            int inside = close - list.Start - 1;
            return _text.AsSpan(list.Start + 1, inside).Contains('\n')
                ? new Change(list.Start + 1, 0, NewLine() + Indent(close) + Step() + text)
                : new Change(list.Start + 1 + (inside / 2), 0, text);
        }

        string separator = Separator(entries);
        return index < entries.Length
            ? new Change(entries[index].Start, 0, text + separator)
            : new Change(End(entries[^1]), 0, separator + text);
    }

    /// <summary>Finds where a place the runtime named is written.</summary>
    /// <param name="pointer">A place, as <see cref="Rulealize.Abstraction.SourcePath"/> renders one.</param>
    /// <returns>
    /// Where it is, or where the nearest place around it that is written is — a missing key is
    /// reported at the object it is missing from. A value is the whole of it; an object or an
    /// array is its opening bracket, because marking all of one marks everything inside it.
    /// </returns>
    public (int Start, int Length) Locate(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        string at = pointer == "/" ? "" : pointer;
        while (true)
        {
            if (_spans.TryGetValue(at, out (int Start, int Length) span))
            {
                return _text[span.Start] is '{' or '[' ? (span.Start, 1) : span;
            }

            if (at.Length == 0)
            {
                return (0, 0);
            }

            at = Parent(at);
        }
    }

    /// <summary>Finds where a place is written, the whole of it.</summary>
    /// <param name="pointer">A place in the document, as the runtime names one — <c>/inputs/add/when/right</c>.</param>
    /// <returns>Where its value starts and how long it is, brackets included; <see langword="null"/> where nothing is written there.</returns>
    public (int Start, int Length)? Span(string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        return _spans.TryGetValue(pointer, out (int Start, int Length) span) ? span : null;
    }

    private static bool IsIdentity(string pointer) =>
        Identity.Any(i => pointer == i || pointer.StartsWith(i + "/", StringComparison.Ordinal));

    private static int End(Entry entry) => entry.Start + entry.Length;

    private static Change Cut((int From, int To) span) => new(span.From, span.To - span.From, "");

    /// <summary>Whether all that is written between two places is one comma and white space.</summary>
    private bool IsSeparator(int from, int to) => _text.AsSpan(from, to - from).Trim().SequenceEqual(",");

    /// <summary>What separates one entry from the next: what is written between the list's first two, or a comma and the line or space its first stands after.</summary>
    private string Separator(ImmutableArray<Entry> entries)
    {
        if (entries.Length > 1 && IsSeparator(End(entries[0]), entries[1].Start))
        {
            return _text[End(entries[0])..entries[1].Start];
        }

        return StartsLine(entries[0].Start) ? "," + NewLine() + Indent(entries[0].Start) : ", ";
    }

    /// <summary>The comma that follows an entry, past white space and comments. The text is JSON, so one does.</summary>
    private int Comma(int from)
    {
        int at = from;
        while (at < _text.Length && _text[at] != ',')
        {
            if (_text.AsSpan(at).StartsWith("//"))
            {
                int end = _text.IndexOf('\n', at);
                at = end < 0 ? _text.Length : end;
            }
            else if (_text.AsSpan(at).StartsWith("/*"))
            {
                int end = _text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                at = end < 0 ? _text.Length : end + 2;
            }
            else
            {
                at++;
            }
        }

        return at;
    }

    /// <summary>A span, widened to the whole lines it stands on where nothing else is written on them.</summary>
    private (int From, int To) Lines(int from, int to)
    {
        int end = _text.IndexOf('\n', to);
        if (!StartsLine(from) || end < 0 || !_text.AsSpan(to, end - to).IsWhiteSpace())
        {
            return (from, to);
        }

        return (LineStart(from), end + 1);
    }

    private int LineStart(int at) => at == 0 ? 0 : _text.LastIndexOf('\n', at - 1) + 1;

    private bool StartsLine(int at) => _text.AsSpan(LineStart(at), at - LineStart(at)).IsWhiteSpace();

    /// <summary>The white space the line a place is on starts with.</summary>
    private string Indent(int at)
    {
        int line = LineStart(at);
        int first = line;
        while (first < _text.Length && _text[first] is ' ' or '\t')
        {
            first++;
        }

        return _text[line..first];
    }

    /// <summary>One step of indentation: the first the document takes.</summary>
    private string Step()
    {
        foreach (string line in _text.Split('\n'))
        {
            string indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            if (indent.Length > 0 && !string.IsNullOrWhiteSpace(line))
            {
                return indent;
            }
        }

        return "  ";
    }

    private string NewLine() => _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>Names a place the way the document does, with the operation each part of it is.</summary>
    private static string Name(string pointer, Dictionary<string, string> ops, Dictionary<string, List<string>> arrays)
    {
        List<string> parts = [];
        string at = "";

        foreach (string segment in pointer.Split('/').Skip(1))
        {
            bool item = arrays.ContainsKey(at);
            at = at + "/" + segment;

            string part = item && parts.Count > 0
                ? $"{parts[^1]} {int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture) + 1}"
                : Unescape(segment);

            if (item && parts.Count > 0)
            {
                parts.RemoveAt(parts.Count - 1);
            }

            parts.Add(ops.TryGetValue(at, out string? op) ? $"{part} ({op})" : part);
        }

        return string.Join(" › ", parts);
    }

    private static string Child(Stack<Frame> frames)
    {
        if (frames.Count == 0)
        {
            return "";
        }

        Frame parent = frames.Peek();
        string segment = parent.IsArray
            ? (parent.Index++).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : parent.Key!.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

        return parent.Pointer + "/" + segment;
    }

    private static string Parent(string pointer) => pointer[..pointer.LastIndexOf('/')];

    private static string Unescape(string segment) =>
        segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

    /// <summary>For every byte offset, the UTF-16 offset it falls at, one past the end included.</summary>
    private static int[] CharIndex(byte[] bytes)
    {
        int[] index = new int[bytes.Length + 1];
        int chars = 0;

        for (int i = 0; i < bytes.Length; i++)
        {
            index[i] = chars;
            byte b = bytes[i];
            if ((b & 0xC0) != 0x80)
            {
                chars += b >= 0xF0 ? 2 : 1;
            }
        }

        index[bytes.Length] = chars;
        return index;
    }

    private sealed class Frame(string pointer, bool isArray, int start)
    {
        public string Pointer { get; } = pointer;

        public bool IsArray { get; } = isArray;

        public int Start { get; } = start;

        public string? Key { get; set; }

        public int Index { get; set; }
    }
}
