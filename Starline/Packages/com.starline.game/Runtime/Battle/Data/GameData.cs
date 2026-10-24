using System;
using System.Collections.Generic;

namespace Starline.Battle
{
    /// <summary>
    /// All game configuration, loaded once from the JSON files in
    /// Runtime/Resources/StarlineData (Unity reads them as TextAssets, the .NET tests read the
    /// files directly). The static catalogs (<see cref="UnitCatalog"/>, <see cref="CounterTable"/>,
    /// <see cref="CommanderSkillDef"/>, ...) all read from here.
    /// </summary>
    public static class GameData
    {
        /// <summary>File names (without extension) that make up the data set, in load order.</summary>
        public static readonly string[] Files = { "units", "counters", "skills", "encounters", "expedition", "power" };

        private static Dictionary<string, JsonValue> _files;

        public static bool IsLoaded => _files != null;

        /// <summary>
        /// Hash of every data file's text (16 hex digits). Replays record it: a replay only
        /// reproduces under the exact numbers it was played with.
        /// </summary>
        public static string Fingerprint { get; private set; }

        /// <summary>Drops a byte-order mark and carriage returns so Windows and Linux checkouts hash the same.</summary>
        private static string NormalizeText(string text)
        {
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            return text.Replace("\r", "");
        }

        /// <summary>Loads every data file through <paramref name="readFile"/> (name without extension → text).</summary>
        public static void Load(Func<string, string> readFile)
        {
            var files = new Dictionary<string, JsonValue>();
            ulong fingerprint = Fnv.Offset;
            foreach (string name in Files)
            {
                string text = readFile(name) ?? throw new InvalidOperationException("找不到数据文件 " + name + ".json");
                files[name] = Json.Parse(text, name + ".json");
                fingerprint = Fnv.Mix(fingerprint, name + "\n");
                fingerprint = Fnv.Mix(fingerprint, NormalizeText(text));
            }
            _files = files;
            Fingerprint = fingerprint.ToString("x16");
            UnitCatalog.Load(files["units"]);
            CounterTable.Load(files["counters"]);
            CommanderSkillDef.Load(files["skills"]);
            PowerRating.Load(files["power"]);
            Loaded?.Invoke();
        }

        /// <summary>Raised after a (re)load so other assemblies can rebuild their caches.</summary>
        public static event Action Loaded;

        /// <summary>The parsed root of a data file.</summary>
        public static JsonValue File(string name)
        {
            if (_files == null)
            {
                throw new InvalidOperationException("游戏数据还没有加载：先调用 GameData.Load");
            }
            return _files[name];
        }

        /// <summary>Reads a [unitId, depth, row(, star)] entry into a formation.</summary>
        public static void AddPlacement(Formation formation, JsonValue entry)
        {
            formation.Add(entry[0].AsString(), entry[1].AsInt(), entry[2].AsInt(), entry.Count > 3 ? entry[3].AsInt() : 1);
        }

        public static T ParseEnum<T>(JsonValue value) where T : struct
        {
            if (!Enum.TryParse(value.AsString(), false, out T result))
            {
                throw new FormatException(value.Path + "：未知的取值 " + value.AsString());
            }
            return result;
        }
    }
}
