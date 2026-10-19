using UnityEditor;
using UnityEngine;

namespace Starline.Editor
{
    /// <summary>
    /// Installs the Universal Render Pipeline package the first time the project opens.
    /// The pipeline itself is configured by UrpSetup (Starline.Editor.Urp), which only
    /// compiles once the package is present.
    /// </summary>
    [InitializeOnLoad]
    internal static class UrpInstaller
    {
        private const string PackageName = "com.unity.render-pipelines.universal";
        private const string RequestedKey = "Starline.UrpInstallRequested";

        static UrpInstaller()
        {
            EditorApplication.delayCall += InstallIfMissing;
        }

        private static void InstallIfMissing()
        {
            if (IsInstalled() || SessionState.GetBool(RequestedKey, false))
            {
                return;
            }
            SessionState.SetBool(RequestedKey, true);
            Debug.Log("Starline：正在安装 URP 渲染管线（" + PackageName + "），完成后会自动配置。");
            // No version: the Package Manager picks the one that matches this editor.
            UnityEditor.PackageManager.Client.Add(PackageName);
        }

        private static bool IsInstalled() =>
            System.Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime") != null;
    }
}
