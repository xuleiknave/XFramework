using System.Collections.Generic;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// A ring of dark rounded hills on the horizon around this object (smooth half-domes, matching
    /// the cartoon models), as one generated mesh. Change the numbers and the ridge regenerates.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MenuMountains : MonoBehaviour
    {
        [Min(0)] public int count = 28;
        public int seed = 11;
        [Tooltip("山峰分布的水平角度范围（度，0 是正前方）")]
        public float spread = 80f;
        public float minDistance = 110f;
        public float maxDistance = 150f;
        public float minHeight = 8f;
        public float maxHeight = 26f;
        public float minWidth = 30f;
        public float maxWidth = 60f;
        [Tooltip("山丘前后的厚度相对宽度的比例")]
        [Range(0.2f, 1f)] public float depth = 0.6f;
        public Color color = new Color(0.045f, 0.065f, 0.11f);

        private Mesh _mesh;
        private Material _material;
        private bool _dirty;

        private void OnEnable() => Rebuild();

        private void OnValidate() => _dirty = true;

        private void OnDisable()
        {
            ViewUtil.DestroySafe(_mesh);
            ViewUtil.DestroySafe(_material);
            _mesh = null;
            _material = null;
        }

        private void Update()
        {
            if (_dirty)
            {
                Rebuild();
            }
        }

        private void Rebuild()
        {
            _dirty = false;
            OnDisable();
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.Lerp(-spread, spread, count > 1 ? i / (float)(count - 1) : 0.5f) + (float)random.NextDouble() * 4f;
                float distance = Mathf.Lerp(minDistance, maxDistance, (float)random.NextDouble());
                float height = Mathf.Lerp(minHeight, maxHeight, (float)random.NextDouble());
                float width = Mathf.Lerp(minWidth, maxWidth, (float)random.NextDouble());
                Vector3 centre = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                // Each hill faces the centre, so its long side runs along the horizon.
                Quaternion turn = Quaternion.Euler(0f, angle, 0f);
                AddHill(vertices, triangles, centre, turn, width, height, depth);
            }
            _mesh = new Mesh { name = "Mountains", hideFlags = HideFlags.DontSave };
            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _material = MenuLook.Lit(color);
            MeshRenderer r = GetComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private const int HillSegments = 20;
        private const int HillRings = 6;

        /// <summary>A half-ellipsoid with shared vertices, so it shades smoothly.</summary>
        private static void AddHill(List<Vector3> vertices, List<int> triangles, Vector3 centre, Quaternion turn, float width, float height, float depth)
        {
            int start = vertices.Count;
            float rx = width * 0.5f;
            float rz = rx * depth;
            for (int ring = 0; ring <= HillRings; ring++)
            {
                // From the ground (ring 0) up to the top (last ring).
                float pitch = ring * Mathf.PI * 0.5f / HillRings;
                float spread = Mathf.Cos(pitch);
                float y = Mathf.Sin(pitch) * height;
                for (int k = 0; k < HillSegments; k++)
                {
                    float a = k * Mathf.PI * 2f / HillSegments;
                    vertices.Add(centre + turn * new Vector3(Mathf.Cos(a) * rx * spread, y, Mathf.Sin(a) * rz * spread));
                }
            }
            for (int ring = 0; ring < HillRings; ring++)
            {
                int row = start + ring * HillSegments;
                int next = row + HillSegments;
                for (int k = 0; k < HillSegments; k++)
                {
                    int k1 = (k + 1) % HillSegments;
                    triangles.Add(row + k);
                    triangles.Add(next + k);
                    triangles.Add(next + k1);
                    triangles.Add(row + k);
                    triangles.Add(next + k1);
                    triangles.Add(row + k1);
                }
            }
        }
    }
}
