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
        private static readonly Color NightAmbient = new Color(0.07f, 0.09f, 0.16f);
        private static readonly Quaternion SunRotation = Quaternion.Euler(50f, -35f, 0f);
        /// <summary>Moonlight comes from behind the menu scene (the moon is up and to the left).</summary>
        private static readonly Quaternion MoonRotation = Quaternion.Euler(28f, 160f, 0f);

        private static Light _sun;
        private static Light _rim;
#if STARLINE_URP
        private static Bloom _bloom;
#endif

        public static void Apply(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
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
            sun.transform.rotation = SunRotation;
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
            _bloom = bloom;

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
        /// Night lighting for the menu scene: dim blue moonlight from behind, darker ambient, no
        /// warm rim. The board lighting comes back when <paramref name="night"/> is false.
        /// </summary>
        public static void SetNight(bool night)
        {
            RenderSettings.ambientLight = night ? NightAmbient : DayAmbient;
            if (_sun != null)
            {
                _sun.color = night ? new Color(0.55f, 0.66f, 1f) : new Color(0.85f, 0.9f, 1f);
                _sun.intensity = night ? 0.6f : 1.3f;
                _sun.transform.rotation = night ? MoonRotation : SunRotation;
            }
            if (_rim != null)
            {
                _rim.enabled = !night;
            }
        }

        /// <summary>Sun (and moon) shadows, switched off by the "阴影 关" graphics setting.</summary>
        public static void SetShadows(bool on)
        {
            if (_sun != null)
            {
                _sun.shadows = on ? LightShadows.Soft : LightShadows.None;
            }
        }

        /// <summary>Bloom on spells, the mana network and the crystals (graphics setting "泛光").</summary>
        public static void SetBloom(bool on)
        {
#if STARLINE_URP
            if (_bloom != null)
            {
                _bloom.active = on;
            }
#endif
        }

        /// <summary>Makes a lit material glow a little in its own colour (picked up by bloom).</summary>
        public static void SetGlow(Material material, Color color, float strength)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
        }
    }
}
