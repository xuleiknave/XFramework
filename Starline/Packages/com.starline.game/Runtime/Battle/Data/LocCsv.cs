using System;
using System.Collections.Generic;
using System.Text;

namespace Starline.Battle
{
    /// <summary>
    /// The string table files in Localization/*.csv: a header row "key, language codes…, note"
    /// and one row per key. Quoted fields may contain commas, quotes ("") and line breaks.
    /// The Unity editor imports these files into Localization string tables; the tests read
    /// them to check that every language has the same keys and placeholders.
    /// </summary>
    public sealed class LocCsv
    {
        public const string KeyColumn = "key";
        public const string NoteColumn = "note";

        /// <summary>Language codes in column order, such as zh-Hans and en.</summary>
        public readonly List<string> Languages = new List<string>();
        public readonly List<Row> Rows = new List<Row>();

        public sealed class Row
        {
            public string Key;
            public string Note = "";
            /// <summary>Text per language code; an empty string means not translated yet.</summary>
            public readonly Dictionary<string, string> Text = new Dictionary<string, string>();

            public string Get(string language) => Text.TryGetValue(language, out string text) ? text : "";
        }

        public static LocCsv Parse(string content)
        {
            List<List<string>> records = ReadRecords(content);
            if (records.Count == 0 || records[0].Count == 0 || records[0][0] != KeyColumn)
            {
                throw new FormatException("语言表第一行应以 " + KeyColumn + " 开头");
            }
            List<string> header = records[0];
            var csv = new LocCsv();
            int noteColumn = header.IndexOf(NoteColumn);
            for (int c = 1; c < header.Count; c++)
            {
                if (c != noteColumn)
                {
                    csv.Languages.Add(header[c]);
                }
            }
            var seen = new HashSet<string>();
            for (int r = 1; r < records.Count; r++)
            {
                List<string> fields = records[r];
                if (fields.Count == 1 && fields[0].Length == 0)
                {
                    continue;
                }
                var row = new Row { Key = fields[0].Trim() };
                if (row.Key.Length == 0)
                {
                    throw new FormatException("语言表第 " + (r + 1) + " 行缺少 key");
                }
                if (!seen.Add(row.Key))
                {
                    throw new FormatException("语言表的 key 重复：" + row.Key);
                }
                for (int c = 1; c < header.Count; c++)
                {
                    string value = c < fields.Count ? fields[c] : "";
                    if (c == noteColumn)
                    {
                        row.Note = value;
                    }
                    else
                    {
                        row.Text[header[c]] = value;
                    }
                }
                csv.Rows.Add(row);
            }
            return csv;
        }

        public string Write()
        {
            var sb = new StringBuilder();
            var header = new List<string> { KeyColumn };
            header.AddRange(Languages);
            header.Add(NoteColumn);
            WriteRecord(sb, header);
            foreach (Row row in Rows)
            {
                var fields = new List<string> { row.Key };
                foreach (string language in Languages)
                {
                    fields.Add(row.Get(language));
                }
                fields.Add(row.Note);
                WriteRecord(sb, fields);
            }
            return sb.ToString();
        }

        private static List<List<string>> ReadRecords(string content)
        {
            var records = new List<List<string>>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            int i = content.Length > 0 && content[0] == '﻿' ? 1 : 0;
            for (; i < content.Length; i++)
            {
                char c = content[i];
                if (quoted)
                {
                    if (c != '"')
                    {
                        field.Append(c);
                    }
                    else if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                    {
                        i++;
                    }
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields);
                    fields = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }
            if (quoted)
            {
                throw new FormatException("语言表里有没结束的引号");
            }
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields);
            }
            return records;
        }

        private static void WriteRecord(StringBuilder sb, List<string> fields)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                string f = fields[i] ?? "";
                if (f.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                {
                    sb.Append('"').Append(f.Replace("\"", "\"\"")).Append('"');
                }
                else
                {
                    sb.Append(f);
                }
            }
            sb.Append('\n');
        }
    }
}
