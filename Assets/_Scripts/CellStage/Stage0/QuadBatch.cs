using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// A dynamic mesh of camera-facing quads rebuilt every frame — one draw call for hundreds of motes or cells.
    /// Each quad carries: uv0 (local x, local y, a), uv1 (4 params), uv2 (4 params), colour.
    /// </summary>
    public sealed class QuadBatch
    {
        readonly Mesh mesh;
        readonly List<Vector3> v = new();
        readonly List<Vector3> uv0 = new();
        readonly List<Vector4> uv1 = new(), uv2 = new(), uv3 = new();
        readonly List<Color> col = new();
        readonly List<int> idx = new();
        public readonly GameObject go;

        public QuadBatch(string name, Material mat, Transform parent, int layer, int sortingOrder)
        {
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        public void Clear() { v.Clear(); uv0.Clear(); uv1.Clear(); uv2.Clear(); uv3.Clear(); col.Clear(); idx.Clear(); }

        /// A quad centred at `c` with half-extent `half` (local uv spans −ext..ext), rotated by `rot` radians.
        public void Add(Vector2 c, float half, float rot, float ext, float a, Vector4 p1, Vector4 p2, Color color, float z = 0f, Vector4 p3 = default)
        {
            float cs = Mathf.Cos(rot) * half, sn = Mathf.Sin(rot) * half;
            int b = v.Count;
            for (int k = 0; k < 4; k++)
            {
                float lx = (k == 1 || k == 2) ? 1f : -1f, ly = k >= 2 ? 1f : -1f;
                v.Add(new Vector3(c.x + lx * cs - ly * sn, c.y + lx * sn + ly * cs, z));
                uv0.Add(new Vector3(lx * ext, ly * ext, a));
                uv1.Add(p1); uv2.Add(p2); uv3.Add(p3); col.Add(color);
            }
            idx.Add(b); idx.Add(b + 2); idx.Add(b + 1); idx.Add(b); idx.Add(b + 3); idx.Add(b + 2);
        }

        public void Upload()
        {
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetUVs(2, uv2); mesh.SetUVs(3, uv3); mesh.SetColors(col);
            mesh.SetTriangles(idx, 0, false);
            mesh.bounds = new Bounds(new Vector3(TidePool.W * 0.5f, TidePool.H * 0.5f, 0f), new Vector3(TidePool.W + 40f, TidePool.H + 40f, 10f));
        }
    }
}
