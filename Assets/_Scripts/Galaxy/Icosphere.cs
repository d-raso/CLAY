using System.Collections.Generic;
using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>
    /// Builds a unit icosphere (subdivided icosahedron) — evenly-sized triangles and no UV pole pinching,
    /// so it's the right base for a procedurally-displaced planet. Higher <c>subdivisions</c> → more
    /// vertices (642 · 2562 · 10242 · 40962 at levels 3–6), used for zoom LOD.
    /// </summary>
    public static class Icosphere
    {
        public static void Build(int subdivisions, out Vector3[] verts, out int[] tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

            var faces = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
                new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
                new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
                new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
            };

            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                idx = v.Count - 1; mid[key] = idx; return idx;
            }

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int[]>(faces.Count * 4);
                foreach (var f in faces)
                {
                    int a = Mid(f[0], f[1]), b = Mid(f[1], f[2]), c = Mid(f[2], f[0]);
                    next.Add(new[] { f[0], a, c }); next.Add(new[] { f[1], b, a });
                    next.Add(new[] { f[2], c, b }); next.Add(new[] { a, b, c });
                }
                faces = next;
            }

            verts = v.ToArray();
            tris = new int[faces.Count * 3];
            for (int i = 0; i < faces.Count; i++)
            {
                tris[i * 3] = faces[i][0]; tris[i * 3 + 1] = faces[i][1]; tris[i * 3 + 2] = faces[i][2];
            }
        }
    }
}
