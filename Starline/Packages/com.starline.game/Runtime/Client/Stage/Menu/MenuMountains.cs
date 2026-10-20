using System.Collections.Generic;
using UnityEngine;

namespace Starline.Client
{
    /// <summary>
    /// A ring of dark low pyramids on the horizon around this object, as one generated mesh.
    /// Change the numbers and the ridge regenerates.
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
        public float maxHeight = 30f;
        public float minWidth = 18f;
        public float maxWidth = 40f;
        public Color color = new Color(0.035f, 0.05f, 0.09f);

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
                Quaternion turn = Quaternion.Euler(0f, (float)random.NextDouble() * 90f, 0f);
                AddPyramid(vertices, triangles, centre, turn, width, height);
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

        /// <summary>Four triangular faces with their own vertices, so each face is flat-shaded.</summary>
        private static void AddPyramid(List<Vector3> vertices, List<int> triangles, Vector3 centre, Quaternion turn, float width, float height)
        {
            Vector3 top = centre + Vector3.up * height;
            float h = width * 0.5f;
            Vector3[] b =
            {
                centre + turn * new Vector3(-h, 0f, -h), centre + turn * new Vector3(h, 0f, -h),
                centre + turn * new Vector3(h, 0f, h), centre + turn * new Vector3(-h, 0f, h),
            };
            for (int i = 0; i < 4; i++)
            {
                int start = vertices.Count;
                vertices.Add(b[i]);
                vertices.Add(top);
                vertices.Add(b[(i + 1) % 4]);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
        }
    }
}
