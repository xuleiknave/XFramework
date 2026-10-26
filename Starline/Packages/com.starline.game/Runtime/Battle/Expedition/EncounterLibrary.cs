using System.Collections.Generic;
using Starline.Battle;

namespace Starline.Expedition
{
    /// <summary>
    /// Enemy formations from encounters.json. Each encounter is a shared base (the hive and
    /// its nodes) plus its own units; <see cref="Create"/> returns a fresh copy every time.
    /// </summary>
    public static class EncounterLibrary
    {
        public static Encounter Create(string id)
        {
            JsonValue e = Find(id) ?? throw new KeyNotFoundException("Unknown encounter: " + id);
            var enemy = new Formation();
            if (e.String("base") is string baseId)
            {
                foreach (JsonValue p in GameData.File("encounters")["bases"][baseId].Elements)
                {
                    GameData.AddPlacement(enemy, p);
                }
            }
            foreach (JsonValue p in e["units"].Elements)
            {
                GameData.AddPlacement(enemy, p);
            }
            return new Encounter
            {
                Id = id,
                Enemy = enemy,
                Terrain = ReadTerrain(e, id),
            };
        }

        /// <summary>
        /// "terrain" names the environment; "layout" hand-draws the tiles ({"player": [...], "enemy": [...]},
        /// see <see cref="TerrainLayout.ParseHalf"/>), otherwise they are generated from "layoutSeed"
        /// (default: derived from the encounter id, so every visit looks the same).
        /// </summary>
        public static BattleTerrain ReadTerrain(JsonValue e, string id)
        {
            if (!(e.String("terrain") is string terrainId))
            {
                return null;
            }
            if (e.Get("layout") is JsonValue layout)
            {
                var tiles = new TerrainLayout();
                tiles.ParseHalf(BoardLayout.PlayerSide, layout["player"]);
                tiles.ParseHalf(BoardLayout.EnemySide, layout["enemy"]);
                return new BattleTerrain { Def = TerrainCatalog.Get(terrainId), Layout = tiles };
            }
            ulong seed = e.Has("layoutSeed") ? (ulong)e.Int("layoutSeed") : StableHash(id);
            return BattleTerrain.Generate(terrainId, seed);
        }

        private static ulong StableHash(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static bool Exists(string id) => Find(id) != null;

        /// <summary>Every encounter id, in file order.</summary>
        public static List<string> Ids()
        {
            var ids = new List<string>();
            foreach (JsonValue e in GameData.File("encounters")["encounters"].Elements)
            {
                ids.Add(e["id"].AsString());
            }
            return ids;
        }

        private static JsonValue Find(string id)
        {
            foreach (JsonValue e in GameData.File("encounters")["encounters"].Elements)
            {
                if (e["id"].AsString() == id)
                {
                    return e;
                }
            }
            return null;
        }
    }
}
