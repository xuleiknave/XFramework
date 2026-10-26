using UnityEditor;
using UnityEngine;

namespace Starline.Editor
{
    /// <summary>
    /// Import settings for the background music in Resources/StarlineMusic: loops stream from
    /// disk instead of sitting decompressed in memory; short stingers stay compressed in memory.
    /// </summary>
    internal sealed class MusicImportSettings : AssetPostprocessor
    {
        private const string Folder = "/Resources/StarlineMusic/";

        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains(Folder))
            {
                return;
            }
            var importer = (AudioImporter)assetImporter;
            bool stinger = assetPath.EndsWith("/victory.ogg") || assetPath.EndsWith("/defeat.ogg");
            importer.forceToMono = false;
            importer.loadInBackground = true;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = stinger ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = stinger;
            importer.defaultSampleSettings = settings;
        }
    }
}
