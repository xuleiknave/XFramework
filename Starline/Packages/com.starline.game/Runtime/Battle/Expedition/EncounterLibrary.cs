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
                Name = e["name"].AsString(),
                Briefing = e["briefing"].AsString(),
                Enemy = enemy,
            };
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
