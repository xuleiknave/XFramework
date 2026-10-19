using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Starline.Editor
{
    /// <summary>
    /// Creates and activates a URP pipeline asset (HDR, MSAA, shadows) if the project has none.
    /// Runs automatically after the URP package is installed; can be rerun from the menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class UrpSetup
    {
        private const string Folder = "Assets/Settings";
        private const string PipelinePath = Folder + "/Starline_URP.asset";
        private const string RendererPath = Folder + "/Starline_Renderer.asset";

        static UrpSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
                {
                    Configure();
                }
            };
        }

        [MenuItem("Starline/重新配置 URP 渲染")]
        private static void Configure()
        {
            Directory.CreateDirectory(Folder);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                AssignDefaultPostProcessData(renderer);

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.supportsHDR = true;
                pipeline.msaaSampleCount = 4;
                pipeline.shadowDistance = 60f;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            Debug.Log("Starline：已启用 URP 渲染管线（" + PipelinePath + "）。");
        }

        /// <summary>
        /// Post-processing needs the renderer's PostProcessData. The helper that returns the
        /// default one is internal and has moved between URP versions, so look it up by name.
        /// </summary>
        private static void AssignDefaultPostProcessData(UniversalRendererData renderer)
        {
            FieldInfo field = typeof(UniversalRendererData).GetField("postProcessData", BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.GetValue(renderer) != null)
            {
                return;
            }
            System.Type dataType = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.PostProcessData");
            MethodInfo getDefault = dataType?.GetMethod("GetDefaultPostProcessData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (getDefault == null)
            {
                return;
            }
            field.SetValue(renderer, getDefault.Invoke(null, null));
            EditorUtility.SetDirty(renderer);
        }
    }
}
