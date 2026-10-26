using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// Colours a plain mesh in the menu scene (the ground, the moon) without a material asset:
    /// lit for solid things, unlit for things that glow on their own.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public sealed class MenuTint : MonoBehaviour
    {
        public Color color = new Color(0.05f, 0.07f, 0.11f);

        [Tooltip("勾上后不受灯光影响，自己发光（月亮、星星）")]
        public bool unlit;

        [Tooltip("自发光强度，大于 1 时泛光（只对 unlit 有效）")]
        [Min(0f)] public float brightness = 1f;

        private Material _material;

        private void OnEnable() => Apply();

        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            ViewUtil.DestroySafe(_material);
            _material = null;
        }

        private void Apply()
        {
            ViewUtil.DestroySafe(_material);
            _material = unlit ? MenuLook.Unlit(color * brightness) : MenuLook.Lit(color);
            Renderer r = GetComponent<Renderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = unlit ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
