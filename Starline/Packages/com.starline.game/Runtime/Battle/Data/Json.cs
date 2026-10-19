using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starline.Battle
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// A parsed JSON value. Numbers keep their source text so they can be converted to
    /// <see cref="Fix"/> exactly (no floating point anywhere in the data path); object
    /// members keep their file order.
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly List<JsonValue> EmptyList = new List<JsonValue>();

        public JsonKind Kind;
        public bool BoolValue;
        /// <summary>The number's source text, or the string's value.</summary>
        public string Text;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Members;
        /// <summary>Where this value sits in the file, for error messages ("units[3].hp").</summary>
        public string Path = "$";

        public bool IsNull => Kind == JsonKind.Null;
        public int Count => Kind == JsonKind.Array ? Items.Count : Kind == JsonKind.Object ? Members.Count : 0;
        public JsonValue this[int index] => Items[index];
        public IReadOnlyList<JsonValue> Elements => Kind == JsonKind.Array ? Items : EmptyList;

        public bool Has(string key) => Get(key) != null;

        /// <summary>The member with this key, or null.</summary>
        public JsonValue Get(string key)
        {
            if (Kind != JsonKind.Object)
            {
                return null;
            }
            foreach (KeyValuePair<string, JsonValue> member in Members)
            {
                if (member.Key == key)
                {
                    return member.Value;
                }
            }
            return null;
        }

        /// <summary>The member with this key; throws a readable error when it is missing.</summary>
        public JsonValue this[string key] => Get(key) ?? throw new FormatException(Path + "." + key + "：缺少字段");

        public string AsString()
        {
            Expect(JsonKind.String);
            return Text;
        }

        public bool AsBool()
        {
            Expect(JsonKind.Bool);
            return BoolValue;
        }

        public int AsInt()
        {
            Expect(JsonKind.Number);
            if (!int.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException(Path + "：应为整数，实际是 " + Text);
            }
            return value;
        }

        /// <summary>Converts a decimal like "1.25" to Fix exactly as 125/100.</summary>
        public Fix AsFix()
        {
            Expect(JsonKind.Number);
            string text = Text;
            if (text.IndexOfAny(new[] { 'e', 'E' }) >= 0)
            {
                throw new FormatException(Path + "：数值不支持科学计数法：" + text);
            }
            int dot = text.IndexOf('.');
            if (dot < 0)
            {
                return Fix.FromInt(AsInt());
            }
            string digits = text.Substring(0, dot) + text.Substring(dot + 1);
            int decimals = text.Length - dot - 1;
            if (decimals > 4 || !long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long numerator))
            {
                throw new FormatException(Path + "：数值格式不支持（最多 4 位小数）：" + text);
            }
            long denominator = 1;
            for (int i = 0; i < decimals; i++)
            {
                denominator *= 10;
            }
            return Fix.FromRatio(numerator, denominator);
        }

        // Optional-member helpers.
        public string String(string key, string fallback = null) => Get(key) is JsonValue v && !v.IsNull ? v.AsString() : fallback;
        public int Int(string key, int fallback = 0) => Get(key) is JsonValue v && !v.IsNull ? v.AsInt() : fallback;
        public Fix FixValue(string key, Fix fallback) => Get(key) is JsonValue v && !v.IsNull ? v.AsFix() : fallback;
        public Fix FixValue(string key) => FixValue(key, Fix.Zero);
        public bool Bool(string key, bool fallback = false) => Get(key) is JsonValue v && !v.IsNull ? v.AsBool() : fallback;

        private void Expect(JsonKind kind)
        {
            if (Kind != kind)
            {
                throw new FormatException(Path + "：应为 " + kind + "，实际是 " + Kind);
            }
        }
    }

    /// <summary>Small dependency-free JSON reader (the core cannot use Unity or NuGet packages).</summary>
    public static class Json
    {
        public static JsonValue Parse(string text, string source = "$")
        {
            var reader = new Reader(text, source);
            JsonValue value = reader.ReadValue(source);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("文件末尾有多余内容");
            }
            return value;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private readonly string _source;
            private int _pos;

            public Reader(string text, string source)
            {
                _text = text;
                _source = source;
                // Tolerate a UTF-8 byte order mark.
                if (_text.Length > 0 && _text[0] == '﻿')
                {
                    _pos = 1;
                }
            }

            public bool AtEnd => _pos >= _text.Length;

            public FormatException Error(string message)
            {
                int line = 1;
                for (int i = 0; i < _pos && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                    }
                }
                return new FormatException(_source + " 第 " + line + " 行：" + message);
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
            }

            public JsonValue ReadValue(string path)
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("意外的文件结尾");
                }
                char c = _text[_pos];
                switch (c)
                {
                    case '{':
                        return ReadObject(path);
                    case '[':
                        return ReadArray(path);
                    case '"':
                        return new JsonValue { Kind = JsonKind.String, Text = ReadString(), Path = path };
                    case 't':
                        ReadWord("true");
                        return new JsonValue { Kind = JsonKind.Bool, BoolValue = true, Path = path };
                    case 'f':
                        ReadWord("false");
                        return new JsonValue { Kind = JsonKind.Bool, BoolValue = false, Path = path };
                    case 'n':
                        ReadWord("null");
                        return new JsonValue { Kind = JsonKind.Null, Path = path };
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber(path);
                        }
                        throw Error("无法识别的字符 '" + c + "'");
                }
            }

            private JsonValue ReadObject(string path)
            {
                var value = new JsonValue { Kind = JsonKind.Object, Members = new List<KeyValuePair<string, JsonValue>>(), Path = path };
                _pos++;
                SkipWhitespace();
                if (Peek('}'))
                {
                    _pos++;
                    return value;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"'))
                    {
                        throw Error("对象的键必须是字符串");
                    }
                    string key = ReadString();
                    SkipWhitespace();
                    Consume(':');
                    value.Members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue(path + "." + key)));
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }
                    Consume('}');
                    return value;
                }
            }

            private JsonValue ReadArray(string path)
            {
                var value = new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>(), Path = path };
                _pos++;
                SkipWhitespace();
                if (Peek(']'))
                {
                    _pos++;
                    return value;
                }
                while (true)
                {
                    value.Items.Add(ReadValue(path + "[" + value.Items.Count + "]"));
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }
                    Consume(']');
                    return value;
                }
            }

            private string ReadString()
            {
                Consume('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("字符串没有结束引号");
                    }
                    char c = _text[_pos++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Error("字符串转义不完整");
                    }
                    char e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length)
                            {
                                throw Error("\\u 转义不完整");
                            }
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw Error("未知的转义 \\" + e);
                    }
                }
            }

            private JsonValue ReadNumber(string path)
            {
                int start = _pos;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0)
                {
                    _pos++;
                }
                return new JsonValue { Kind = JsonKind.Number, Text = _text.Substring(start, _pos - start), Path = path };
            }

            private void ReadWord(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                {
                    throw Error("应为 " + word);
                }
                _pos += word.Length;
            }

            private bool Peek(char c) => !AtEnd && _text[_pos] == c;

            private void Consume(char c)
            {
                if (!Peek(c))
                {
                    throw Error("应为 '" + c + "'");
                }
                _pos++;
            }
        }
    }
}
