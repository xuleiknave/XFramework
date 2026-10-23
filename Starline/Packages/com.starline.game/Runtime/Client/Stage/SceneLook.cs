using UnityEngine;
#if STARLINE_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Starline.Client
{
    /// <summary>
    /// Lighting and post-processing: a cool key light, dim blue ambient,
    /// and (with URP) HDR bloom so lasers, supply lines and explosions glow.
    /// </summary>
    public static class SceneLook
    {
        private static readonly Color DayAmbient = new Color(0.16f, 0.2f, 0.3f);
        private static readonly Color DaySky = new Color(0.02f, 0.03f, 0.06f);

        private static Light _sun;
        private static Light _rim;

        public static void Apply(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DaySky;
            camera.allowHDR = true;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = DayAmbient;

            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
            }
            sun.type = LightType.Directional;
            sun.color = new Color(0.85f, 0.9f, 1f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            _sun = sun;

            // A warm rim light from the enemy side separates units from the dark floor.
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(1f, 0.6f, 0.4f);
            rim.intensity = 0.35f;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(25f, 120f, 0f);
            _rim = rim;

#if STARLINE_URP
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.4f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.65f);

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);

            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
#endif
        }

        /// <summary>
        /// Lighting while the menu scene shows: the board's sun and rim go dark (the menu
        /// prefab brings its own moonlight), with the scene's ambient and sky colours.
        /// </summary>
        public static void SetNight(bool night, Color ambient, Color sky)
        {
            RenderSettings.ambientLight = night ? ambient : DayAmbient;
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.backgroundColor = night ? sky : DaySky;
            }
            if (_sun != null)
            {
                _sun.enabled = !night;
            }
            if (_rim != null)
            {
                _rim.enabled = !night;
            }
        }

        /// <summary>Makes a lit material glow a little in its own colour (picked up by bloom).</summary>
        public static void SetGlow(Material material, Color color, float strength)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
        }
    }
}
