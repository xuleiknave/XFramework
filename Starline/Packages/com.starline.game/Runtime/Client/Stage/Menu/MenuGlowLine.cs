using UnityEngine;

namespace Starline.Client
{
    /// <summary>A glowing mana line between two objects of the menu scene; follows them when they move.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class MenuGlowLine : MonoBehaviour
    {
        public Transform from;
        public Transform to;
        [Tooltip("两端相对物体的抬高（米），让光带连在塔顶和水晶上")]
        public float fromHeight = 3.2f;
        public float toHeight = 1.3f;
        public Color color = new Color(0.44f, 0.82f, 0.71f);
        [Min(0f)] public float glow = 3f;
        [Min(0.001f)] public float width = 0.08f;

        private LineRenderer _line;

        private void OnEnable()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = MenuLook.Unlit(color * glow);
        }

        private void OnDisable()
        {
            if (_line != null)
            {
                ViewUtil.DestroySafe(_line.sharedMaterial);
            }
            _line = null;
        }

        private void OnValidate()
        {
            if (_line != null && _line.sharedMaterial != null)
            {
                _line.sharedMaterial.color = color * glow;
            }
        }

        private void LateUpdate()
        {
            if (_line == null || from == null || to == null)
            {
                return;
            }
            _line.startWidth = _line.endWidth = width;
            _line.startColor = _line.endColor = Color.white;
            _line.SetPosition(0, from.position + Vector3.up * fromHeight);
            _line.SetPosition(1, to.position + Vector3.up * toHeight);
        }
    }
}
