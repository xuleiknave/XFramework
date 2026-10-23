using UnityEngine;
using Starline.Battle;

namespace Starline.Client
{
    /// <summary>Shared colours, conversions and primitive helpers for the placeholder visuals.</summary>
    public static class ViewUtil
    {
        public static readonly Color PlayerColor = new Color(0.35f, 0.72f, 1f);
        public static readonly Color EnemyColor = new Color(1f, 0.56f, 0.29f);
        public static readonly Color SupplyColor = new Color(0.44f, 0.82f, 0.71f); // #6FD0B4, same as the UI
        public static readonly Color CutColor = new Color(1f, 0.3f, 0.3f);
        public static readonly Color GraceColor = new Color(0.91f, 0.76f, 0.35f);
        /// <summary>Glow of undead models (ghostly green); the UI keeps orange as the enemy colour.</summary>
        public static readonly Color UndeadGlowColor = new Color(0.56f, 0.88f, 0.42f);
        public static readonly Color HiddenColor = new Color(0.77f, 0.55f, 1f);

        private static Material _unlit;

        /// <summary>Battle plane (x, y) maps to world (x, 0, z).</summary>
        public static Vector3 ToWorld(FixVec2 p) => new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());

        public static Color SideColor(int side) => side == BoardLayout.PlayerSide ? PlayerColor : EnemyColor;

        /// <summary>Unlit material for lines and glow effects (tinted per instance, HDR-capable).</summary>
        public static Material Unlit
        {
            get
            {
                if (_unlit == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shader == null)
                    {
                        shader = Shader.Find("Sprites/Default");
                    }
                    _unlit = new Material(shader);
                }
                return _unlit;
            }
        }

        /// <summary>Destroy that also works in edit mode (menu scene components run in the editor).</summary>
        public static void DestroySafe(Object obj)
        {
            if (obj == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }

        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }

        public static LineRenderer Line(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.material = Unlit;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>
        /// Positions and colours a line. Glow above 1 pushes the colour into HDR so bloom
        /// picks it up; the tint lives on the line's own material because vertex colours are
        /// clamped to the 0..1 range.
        /// </summary>
        public static void SetLine(LineRenderer line, Vector3 a, Vector3 b, Color color, float width, float glow = 1f)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.startColor = line.endColor = Color.white;
            line.material.color = color * glow;
            line.startWidth = line.endWidth = width;
        }
    }
}
