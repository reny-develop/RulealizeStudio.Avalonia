// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RulealizeStudio.Generator;

/// <summary>A JSON value, as much of one as a generator needs to read a rule set's shape.</summary>
internal abstract class Json
{
    /// <summary>Reads a document, comments and trailing commas allowed, as a rule set may have them.</summary>
    /// <param name="text">The document.</param>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The text is not JSON.</exception>
    public static Json Parse(string text)
    {
        Reader reader = new(text);
        Json value = reader.Value();
        reader.End();
        return value;
    }

    /// <summary>A record: fields in the order written.</summary>
    internal sealed class Record(List<KeyValuePair<string, Json>> fields) : Json
    {
        public List<KeyValuePair<string, Json>> Fields { get; } = fields;

        public Json? this[string key]
        {
            get
            {
                foreach (KeyValuePair<string, Json> field in Fields)
                {
                    if (field.Key == key)
                    {
                        return field.Value;
                    }
                }

                return null;
            }
        }
    }

    /// <summary>A list.</summary>
    internal sealed class List(List<Json> items) : Json
    {
        public List<Json> Items { get; } = items;
    }

    /// <summary>Text.</summary>
    internal sealed class Text(string value) : Json
    {
        public string Value { get; } = value;
    }

    /// <summary>A number, kept as written.</summary>
    internal sealed class Number(string value) : Json
    {
        public string Value { get; } = value;

        public bool IsWhole => Value.IndexOfAny(['.', 'e', 'E']) < 0;
    }

    /// <summary>True or false.</summary>
    internal sealed class Flag(bool value) : Json
    {
        public bool Value { get; } = value;
    }

    /// <summary>Null.</summary>
    internal sealed class Nothing : Json
    {
        public static Nothing Instance { get; } = new();
    }

    private sealed class Reader(string text)
    {
        private int _at;

        public Json Value()
        {
            Skip();

            if (_at >= text.Length)
            {
                throw Fail("a value");
            }

            char next = text[_at];

            switch (next)
            {
                case '{':
                    return ReadRecord();
                case '[':
                    return ReadList();
                case '"':
                    return new Text(ReadText());
                case 't':
                    Word("true");
                    return new Flag(true);
                case 'f':
                    Word("false");
                    return new Flag(false);
                case 'n':
                    Word("null");
                    return Nothing.Instance;
                default:
                    if (next == '-' || char.IsDigit(next))
                    {
                        return ReadNumber();
                    }

                    throw Fail("a value");
            }
        }

        public void End()
        {
            Skip();
            if (_at < text.Length)
            {
                throw Fail("the end of the document");
            }
        }

        private Record ReadRecord()
        {
            _at++;
            List<KeyValuePair<string, Json>> fields = [];

            while (true)
            {
                Skip();
                if (Take('}'))
                {
                    return new Record(fields);
                }

                if (text[_at] != '"')
                {
                    throw Fail("a field name");
                }

                string key = ReadText();
                Skip();
                if (!Take(':'))
                {
                    throw Fail("':'");
                }

                fields.Add(new KeyValuePair<string, Json>(key, Value()));
                Skip();

                if (!Take(','))
                {
                    Skip();
                    if (!Take('}'))
                    {
                        throw Fail("',' or '}'");
                    }

                    return new Record(fields);
                }
            }
        }

        private List ReadList()
        {
            _at++;
            List<Json> items = [];

            while (true)
            {
                Skip();
                if (Take(']'))
                {
                    return new List(items);
                }

                items.Add(Value());
                Skip();

                if (!Take(','))
                {
                    Skip();
                    if (!Take(']'))
                    {
                        throw Fail("',' or ']'");
                    }

                    return new List(items);
                }
            }
        }

        private string ReadText()
        {
            _at++;
            StringBuilder built = new();

            while (_at < text.Length)
            {
                char c = text[_at++];

                if (c == '"')
                {
                    return built.ToString();
                }

                if (c != '\\')
                {
                    built.Append(c);
                    continue;
                }

                if (_at >= text.Length)
                {
                    break;
                }

                char escaped = text[_at++];
                switch (escaped)
                {
                    case 'n': built.Append('\n'); break;
                    case 't': built.Append('\t'); break;
                    case 'r': built.Append('\r'); break;
                    case 'b': built.Append('\b'); break;
                    case 'f': built.Append('\f'); break;
                    case 'u' when _at + 4 <= text.Length:
                        built.Append((char)int.Parse(text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _at += 4;
                        break;
                    default: built.Append(escaped); break;
                }
            }

            throw Fail("a closing '\"'");
        }

        private Number ReadNumber()
        {
            int start = _at;
            while (_at < text.Length && "+-0123456789.eE".IndexOf(text[_at]) >= 0)
            {
                _at++;
            }

            return new Number(text.Substring(start, _at - start));
        }

        private void Word(string word)
        {
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) != 0)
            {
                throw Fail(word);
            }

            _at += word.Length;
        }

        private bool Take(char c)
        {
            if (_at < text.Length && text[_at] == c)
            {
                _at++;
                return true;
            }

            return false;
        }

        /// <summary>Passes over white space and comments.</summary>
        private void Skip()
        {
            while (_at < text.Length)
            {
                char c = text[_at];

                if (char.IsWhiteSpace(c))
                {
                    _at++;
                }
                else if (c == '/' && _at + 1 < text.Length && text[_at + 1] == '/')
                {
                    while (_at < text.Length && text[_at] != '\n')
                    {
                        _at++;
                    }
                }
                else if (c == '/' && _at + 1 < text.Length && text[_at + 1] == '*')
                {
                    int end = text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
                    _at = end < 0 ? text.Length : end + 2;
                }
                else
                {
                    return;
                }
            }
        }

        private FormatException Fail(string expected) =>
            new($"Expected {expected} at offset {_at}.");
    }
}
