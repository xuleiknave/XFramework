using System;
using System.Globalization;
using System.Text;

namespace Starline.Battle
{
    /// <summary>
    /// Reads and writes replays as JSON (replay-server.md section 4). Fixed-point values are
    /// stored as raw integers and 64-bit numbers as strings, so they round-trip bit for bit.
    /// </summary>
    public static class ReplayJson
    {
        public static string Write(Replay replay)
        {
            var sb = new StringBuilder();
            sb.Append("{\"format\":").Append(Int(replay.Format));
            sb.Append(",\"kernel\":").Append(Int(replay.Kernel));
            sb.Append(",\"data\":").Append(Quote(replay.Data));
            sb.Append(",\"stage\":").Append(Quote(replay.Stage));
            sb.Append(",\"seed\":").Append(Quote(replay.Seed.ToString(CultureInfo.InvariantCulture)));
            sb.Append(",\"power\":").Append(Int(replay.Power));
            sb.Append(",\"player\":[");
            for (int i = 0; i < replay.Player.Placements.Count; i++)
            {
                Placement p = replay.Player.Placements[i];
                sb.Append(i == 0 ? "" : ",");
                sb.Append("{\"unit\":").Append(Quote(p.UnitId));
                sb.Append(",\"depth\":").Append(Int(p.Depth));
                sb.Append(",\"row\":").Append(Int(p.Row));
                sb.Append(",\"star\":").Append(Int(p.Star)).Append('}');
            }
            sb.Append("],\"commands\":[");
            for (int i = 0; i < replay.Commands.Count; i++)
            {
                ReplayCommand c = replay.Commands[i];
                sb.Append(i == 0 ? "" : ",");
                sb.Append("{\"tick\":").Append(Int(c.Tick));
                sb.Append(",\"skill\":").Append(Quote(c.Skill.ToString()));
                sb.Append(",\"x\":").Append(c.X.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"y\":").Append(c.Y.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append("],\"result\":{\"winner\":").Append(Int(replay.Winner));
            sb.Append(",\"ticks\":").Append(Int(replay.Ticks));
            sb.Append(",\"hash\":").Append(Quote(replay.Hash.ToString("x16"))).Append("}}");
            return sb.ToString();
        }

        /// <summary>Parses a replay; throws <see cref="FormatException"/> on malformed input.</summary>
        public static Replay Read(string text)
        {
            JsonValue root = Json.Parse(text, "replay");
            var replay = new Replay
            {
                Format = root["format"].AsInt(),
                Kernel = root["kernel"].AsInt(),
                Data = root["data"].AsString(),
                Stage = root["stage"].AsString(),
                Seed = ParseULong(root["seed"], NumberStyles.None),
                Power = root["power"].AsInt(),
            };
            foreach (JsonValue p in root["player"].Elements)
            {
                replay.Player.Add(p["unit"].AsString(), p["depth"].AsInt(), p["row"].AsInt(), p.Int("star", 1));
            }
            foreach (JsonValue c in root["commands"].Elements)
            {
                replay.Commands.Add(new ReplayCommand
                {
                    Tick = c["tick"].AsInt(),
                    Skill = GameData.ParseEnum<CommanderSkill>(c["skill"]),
                    X = ParseLong(c["x"]),
                    Y = ParseLong(c["y"]),
                });
            }
            JsonValue result = root["result"];
            replay.Winner = result["winner"].AsInt();
            replay.Ticks = result["ticks"].AsInt();
            replay.Hash = ParseULong(result["hash"], NumberStyles.AllowHexSpecifier);
            return replay;
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static long ParseLong(JsonValue value)
        {
            if (value.Kind != JsonKind.Number || !long.TryParse(value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
            {
                throw new FormatException(value.Path + "：应为整数");
            }
            return result;
        }

        private static ulong ParseULong(JsonValue value, NumberStyles style)
        {
            if (!ulong.TryParse(value.AsString(), style, CultureInfo.InvariantCulture, out ulong result))
            {
                throw new FormatException(value.Path + "：无法解析 " + value.AsString());
            }
            return result;
        }

        private static string Quote(string text)
        {
            if (text == null)
            {
                return "null";
            }
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c < ' ')
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
