using System.Collections.Generic;
using UnityEngine;

namespace CLAY.CellStage.Stage0
{
    /// <summary>
    /// The cell-stage POP (ported from the approved Gameplay/PopShell look): a burst is ONE metaball shell, not lines
    /// or separate bubbles. N invisible mass-points start on a small ring inside the cell and blow outward with drag;
    /// because they feed a single fused metaball surface (CLAY/MetaballGoo), neighbours merge into torn ARCS and the
    /// gaps open as the points spread — the membrane visibly breaks apart — then the pieces thin out and fade.
    /// Visual only (the spilled contents are real motes, spawned by Kill).
    /// </summary>
    public sealed class BurstShells
    {
        const int Max = 32;
        sealed class Shell
        {
            public GameObject go; public Material mat;
            public Vector2 center; public float size, age, life;
            public Vector2[] p, v; public float[] rad;
            public readonly Vector4[] data = new Vector4[Max];
        }
        readonly List<Shell> live = new();
        readonly Transform parent; readonly int layer, order;
        readonly Shader shader;
        static Mesh quad;

        public BurstShells(Transform parent, int layer, int sortingOrder)
        {
            this.parent = parent; this.layer = layer; order = sortingOrder;
            shader = Shader.Find("CLAY/MetaballGoo");
            if (quad == null)
            {
                quad = new Mesh { name = "BurstQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            }
        }

        public void Spawn(Vector2 pos, float r, Color color, System.Random rng)
        {
            if (shader == null) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(r * 7f), 10, 24);
            var s = new Shell { center = pos, size = r * 9f, life = 2.6f + r * 0.4f, p = new Vector2[n], v = new Vector2[n], rad = new float[n] };
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n + (float)(rng.NextDouble() - 0.5) * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                s.p[k] = pos + dir * r * 0.45f;
                s.v[k] = dir * r * Mathf.Lerp(4f, 6.5f, (float)rng.NextDouble());
                s.rad[k] = r * Mathf.Lerp(0.2f, 0.34f, (float)rng.NextDouble());
            }
            s.go = new GameObject("Burst") { layer = layer };
            s.go.transform.SetParent(parent, false);
            s.go.transform.position = new Vector3(pos.x, pos.y, 0f);
            s.go.transform.localScale = new Vector3(s.size, s.size, 1f);
            s.go.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = s.go.AddComponent<MeshRenderer>();
            s.mat = new Material(shader);
            var tint = color; tint.a = 1f;
            s.mat.SetColor("_Tint", Color.Lerp(tint, Color.white, 0.15f));
            s.mat.SetColor("_RimColor", Color.Lerp(tint, Color.white, 0.45f));
            s.mat.SetFloat("_Threshold", 1.25f);          // arcs fuse; gaps open as the points spread
            s.mat.SetFloat("_EdgeSoftness", 0.24f);
            s.mat.SetFloat("_NoiseScale", Mathf.Lerp(7f, 12f, (float)rng.NextDouble()));
            s.mat.SetFloat("_Opacity", 0.75f);
            s.mat.SetFloat("_GlobalAlpha", 1f);
            s.mat.SetVectorArray("_Blobs", s.data);
            mr.sharedMaterial = s.mat; mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            live.Add(s);
            Push(s);
        }

        public void Tick(float dt, System.Func<Vector2, Vector2> flowAt)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var s = live[i];
                s.age += dt;
                if (s.age >= s.life) { Object.Destroy(s.go); Object.Destroy(s.mat); live.RemoveAt(i); continue; }
                float drag = Mathf.Exp(-2.2f * dt);
                var drift = flowAt != null ? flowAt(s.center) * 0.3f : Vector2.zero;
                for (int k = 0; k < s.p.Length; k++) { s.v[k] *= drag; s.p[k] += (s.v[k] + drift) * dt; }
                Push(s);
            }
        }

        void Push(Shell s)
        {
            float u = Mathf.Clamp01(s.age / s.life);
            float thin = Mathf.Lerp(1f, 0.06f, u * u);                         // the pieces thin out as they drift apart
            for (int k = 0; k < s.p.Length; k++)
            {
                var d = (s.p[k] - s.center) / s.size;
                s.data[k] = new Vector4(0.5f + d.x, 0.5f + d.y, s.rad[k] * thin / s.size, 0f);
            }
            s.mat.SetVectorArray("_Blobs", s.data);
            s.mat.SetInt("_BlobCount", s.p.Length);
            s.mat.SetFloat("_GlobalAlpha", 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.6f) / 0.4f)));
        }

        public void Clear()
        {
            foreach (var s in live) { if (s.go) Object.Destroy(s.go); if (s.mat) Object.Destroy(s.mat); }
            live.Clear();
        }
    }
}
