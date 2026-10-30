using System;
using System.IO;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Save slots on disk: one JSON file per slot in Application.persistentDataPath/saves. Used by
    /// <see cref="LocalSaveRepository"/> in builds without the game server.
    /// Writes go to a temporary file first so a crash never leaves a half-written save.
    /// </summary>
    public static class ProfileStore
    {
        public const int SlotCount = Starline.Online.ServerLimits.SlotCount;

        private static string Folder => Path.Combine(Application.persistentDataPath, "saves");

        private static string PathOf(int slot) => Path.Combine(Folder, "slot" + (slot + 1) + ".json");

        /// <summary>The profile in a slot, or null when the slot is empty or unreadable.</summary>
        public static CommanderProfile Load(int slot)
        {
            string path = PathOf(slot);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                CommanderProfile profile = JsonUtility.FromJson<CommanderProfile>(File.ReadAllText(path));
                if (profile == null || string.IsNullOrEmpty(profile.name))
                {
                    Debug.LogWarning("Starline：存档 " + path + " 内容无效，按空存档处理。");
                    return null;
                }
                profile.slot = slot;
                return profile;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Starline：读取存档 " + path + " 失败：" + e.Message);
                return null;
            }
        }

        /// <summary>The first slot without a save file, or -1 when all are taken.</summary>
        public static int FirstEmptySlot()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!File.Exists(PathOf(i)))
                {
                    return i;
                }
            }
            return -1;
        }

        public static void Save(CommanderProfile profile)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = PathOf(profile.slot);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(profile, true));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Starline：保存存档失败：" + e.Message);
            }
        }

        public static void Delete(int slot)
        {
            string path = PathOf(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
