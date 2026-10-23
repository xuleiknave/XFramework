using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>
    /// Replays saved on this computer (replay-server.md, step R2): one file per stage under
    /// persistentDataPath/replays, holding the player's lowest-power win on that stage.
    /// </summary>
    public sealed class ReplayStore
    {
        public enum SaveResult
        {
            /// <summary>Saved: the stage had no better win.</summary>
            NewBest,
            /// <summary>Not saved: a playable win with lower or equal power is already stored.</summary>
            KeptExisting,
        }

        private readonly string _directory;

        public ReplayStore(string directory)
        {
            _directory = directory;
        }

        public static ReplayStore CreateDefault() => new ReplayStore(Path.Combine(Application.persistentDataPath, "replays"));

        /// <summary>Saves <paramref name="replay"/> if it beats the stored one; <paramref name="existing"/> is the stored replay, if any.</summary>
        public SaveResult SaveIfBest(Replay replay, out Replay existing)
        {
            existing = Load(replay.Stage);
            if (!replay.IsBetterThan(existing))
            {
                return SaveResult.KeptExisting;
            }
            try
            {
                Directory.CreateDirectory(_directory);
                string path = PathFor(replay.Stage);
                string temp = path + ".tmp";
                File.WriteAllText(temp, ReplayJson.Write(replay));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("保存录像失败：" + e.Message);
            }
            return SaveResult.NewBest;
        }

        /// <summary>The stored replay for a stage, or null if there is none or it cannot be read.</summary>
        public Replay Load(string stage) => Read(PathFor(stage));

        /// <summary>Every readable stored replay, ordered by stage id.</summary>
        public List<Replay> All()
        {
            var replays = new List<Replay>();
            if (!Directory.Exists(_directory))
            {
                return replays;
            }
            foreach (string path in Directory.GetFiles(_directory, "*.json"))
            {
                Replay replay = Read(path);
                if (replay != null)
                {
                    replays.Add(replay);
                }
            }
            replays.Sort((a, b) => string.CompareOrdinal(a.Stage, b.Stage));
            return replays;
        }

        private static Replay Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return ReplayJson.Read(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("无法读取录像 " + path + "：" + e.Message);
                return null;
            }
        }

        private string PathFor(string stage)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                stage = stage.Replace(c, '_');
            }
            return Path.Combine(_directory, stage + ".json");
        }
    }
}
