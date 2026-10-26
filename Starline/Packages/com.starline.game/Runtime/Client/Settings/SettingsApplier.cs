using System.Collections.Generic;
using UnityEngine;
#if STARLINE_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Starline.Client
{
    /// <summary>Puts <see cref="GameSettings"/> into effect: window and resolution, frame pacing, and the render pipeline.</summary>
    public static class SettingsApplier
    {
#if STARLINE_URP
        /// <summary>
        /// A runtime copy of the project's pipeline asset, so changing the quality never writes
        /// into the asset file while playing in the editor.
        /// </summary>
        private static UniversalRenderPipelineAsset _pipeline;
#endif

        public static void ApplyAll(GameSettings settings)
        {
            ApplyDisplay(settings);
            ApplyFrameRate(settings);
            ApplyGraphics(settings);
        }

        /// <summary>The display's own resolution, used when the settings say 0 × 0.</summary>
        public static Vector2Int NativeResolution
        {
            get
            {
                DisplayInfo display = Screen.mainWindowDisplayInfo;
                if (display.width > 0 && display.height > 0)
                {
                    return new Vector2Int(display.width, display.height);
                }
                return new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            }
        }

        /// <summary>Distinct screen sizes the display supports, largest first; always includes the native size.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var sizes = new List<Vector2Int>();
            Vector2Int native = NativeResolution;
            foreach (Resolution resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (size.x <= native.x && size.y <= native.y && !sizes.Contains(size))
                {
                    sizes.Add(size);
                }
            }
            if (!sizes.Contains(native))
            {
                sizes.Add(native);
            }
            sizes.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return sizes;
        }

        public static Vector2Int ResolutionOf(GameSettings settings) =>
            settings.width > 0 && settings.height > 0 ? new Vector2Int(settings.width, settings.height) : NativeResolution;

        public static void ApplyDisplay(GameSettings settings)
        {
            if (Application.isEditor)
            {
                // The game view decides its own size in the editor.
                return;
            }
            Vector2Int size = ResolutionOf(settings);
            Screen.SetResolution(size.x, size.y, ModeOf(settings.displayMode));
        }

        private static FullScreenMode ModeOf(string id)
        {
            switch (id)
            {
                case "borderless": return FullScreenMode.FullScreenWindow;
                case "windowed": return FullScreenMode.Windowed;
                // Exclusive full screen exists only on Windows; elsewhere use the platform's full screen window.
                default:
                    return Application.platform == RuntimePlatform.WindowsPlayer ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
            }
        }

        public static void ApplyFrameRate(GameSettings settings)
        {
            QualitySettings.vSyncCount = settings.vsync ? 1 : 0;
            Application.targetFrameRate = settings.vsync || settings.frameRate <= 0 ? -1 : settings.frameRate;
        }

        /// <summary>Render scale, anti-aliasing, shadows and bloom.</summary>
        public static void ApplyGraphics(GameSettings settings)
        {
            SettingsConfig config = SettingsConfig.Current;
            SettingsConfig.Antialiasing aa = config.FindAntialiasing(settings.antialiasing);
            SettingsConfig.Shadows shadows = config.FindShadows(settings.shadows);
            bool shadowsOn = shadows != null && shadows.distance > 0f;
            SceneLook.SetShadows(shadowsOn);
            SceneLook.SetBloom(settings.bloom);
#if STARLINE_URP
            UniversalRenderPipelineAsset pipeline = Pipeline();
            if (pipeline != null)
            {
                pipeline.renderScale = Mathf.Clamp(settings.renderScale, 10, 200) / 100f;
                pipeline.msaaSampleCount = aa != null ? aa.msaa : 1;
                if (shadowsOn)
                {
                    pipeline.shadowDistance = shadows.distance;
                    SetMainLightShadowmapResolution(pipeline, shadows.resolution);
                }
            }
            Camera camera = Camera.main;
            if (camera != null)
            {
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.antialiasing = CameraAntialiasing(settings.antialiasing);
                data.antialiasingQuality = AntialiasingQuality.High;
            }
#else
            QualitySettings.antiAliasing = aa != null && aa.msaa > 1 ? aa.msaa : 0;
            QualitySettings.shadowDistance = shadowsOn ? shadows.distance : 0f;
#endif
        }

#if STARLINE_URP
        private static AntialiasingMode CameraAntialiasing(string id)
        {
            switch (id)
            {
                case "fxaa": return AntialiasingMode.FastApproximateAntialiasing;
                case "smaa": return AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                default: return AntialiasingMode.None;
            }
        }

        private static System.Reflection.PropertyInfo _shadowResolution;

        /// <summary>
        /// URP 14 (Tuanjie 1.10) keeps the setter internal, so it is called through reflection;
        /// the asset turns the value back into its ShadowResolution enum.
        /// </summary>
        private static void SetMainLightShadowmapResolution(UniversalRenderPipelineAsset pipeline, int resolution)
        {
            if (_shadowResolution == null)
            {
                _shadowResolution = typeof(UniversalRenderPipelineAsset).GetProperty(
                    nameof(UniversalRenderPipelineAsset.mainLightShadowmapResolution),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            System.Reflection.MethodInfo setter = _shadowResolution?.GetSetMethod(true);
            if (setter == null)
            {
                Debug.LogWarning("Starline：当前 URP 版本无法设置主光源阴影分辨率。");
                return;
            }
            setter.Invoke(pipeline, new object[] { resolution });
        }

        private static UniversalRenderPipelineAsset Pipeline()
        {
            if (_pipeline != null)
            {
                return _pipeline;
            }
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset project))
            {
                return null;
            }
            _pipeline = Object.Instantiate(project);
            _pipeline.name = project.name + "（设置）";
            QualitySettings.renderPipeline = _pipeline;
            return _pipeline;
        }
#endif
    }
}
