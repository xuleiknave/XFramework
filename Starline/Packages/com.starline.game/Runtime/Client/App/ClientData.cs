using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>Loads the JSON game data from Resources/StarlineData (the same files the .NET tests read).</summary>
    public static class ClientData
    {
        public const string ResourceFolder = "StarlineData";

        public static void EnsureLoaded()
        {
            if (!GameData.IsLoaded)
            {
                GameData.Load(name => Resources.Load<TextAsset>(ResourceFolder + "/" + name)?.text);
            }
        }
    }
}
