using UnityEngine;
using UnityEngine.Rendering;

namespace CLAY.GalaxyMap
{
    // Drop this on an empty GameObject to get a raymarched volumetric nebula (a hero, close-up cloud with real
    // 3-D structure and self-shadowed pillars). It builds a unit-cube child with the Clay/VolumetricNebula
    // material and feeds it the parameters. This is a NORMAL mesh renderer (not DOTS/BRG), so camera-position
    // access works — it is independent of the instanced galaxy.
    [ExecuteAlways]
    public class VolumetricNebula : MonoBehaviour
    {
        [Header("Size / Placement")]
        public float Size = 40f;
        public Vector3 Shape = Vector3.one;         // per-axis stretch → elongated / flattened / bipolar silhouettes
        public Vector3 StarOffset = Vector3.zero;   // star cluster position, world units from the box centre

        // 0 = planetary (bright ionized shells / bipolar, ~no dust), 1 = big (sculpted emission + dark pillars),
        // 2 = fragment (eroded floaty wisps). Drives the density field's structure in the shader.
        [Range(0, 2)] public int NebulaClass = 1;

        [Header("Colour")]
        public Color Emission = new Color(1.0f, 0.30f, 0.55f, 1f);    // outer gas (H-alpha pink/red)
        public Color Emission2 = new Color(0.25f, 0.85f, 0.8f, 1f);   // hot ionized core (O III teal)
        public Color Emission3 = new Color(0.55f, 0.35f, 0.95f, 1f);  // secondary gas hue (spatial colour variety)
        public Color LightColor = new Color(1.0f, 0.85f, 0.7f, 1f);   // star light on the dust
        public Color Shadowed = new Color(0.16f, 0.10f, 0.07f, 1f);   // dust albedo (dark brown, rim-lit by the star)

        [Header("Shape")]
        [Range(0.5f, 12f)] public float Frequency = 6f;    // higher = finer, more intricate detail
        [Range(0f, 1.5f)] public float Warp = 0.8f;
        [Range(1f, 20f)] public float Density = 7f;
        [Range(0f, 1f)] public float Threshold = 0.32f;
        [Range(1f, 20f)] public float Absorption = 6f;
        [Range(0.5f, 8f)] public float ShadowStrength = 3f;   // higher = darker, punchier shadowed pockets
        [Range(0f, 6f)] public float EmissionStrength = 3.6f;

        [Header("Scattering (physically-based lighting)")]
        [Range(-0.9f, 0.9f)] public float Anisotropy = 0.45f;   // HG g: >0 = forward scatter (bright toward star)
        [Range(0f, 6f)] public float ScatterStrength = 2f;      // strength of scattered starlight
        [Range(0f, 1f)] public float Ambient = 0.25f;           // ambient fill so shadows aren't pure black

        [Header("Quality")]
        [Range(16, 192)] public int Steps = 56;    // upper bound; the shader LODs this down for distant nebulae
        [Range(2, 12)] public int LightSteps = 5;  // self-shadow march — 5 is plenty with the cheap DensityLo field
        public int Seed = 12345;

        Material _mat;
        Transform _box;

        void OnEnable() { Build(); Apply(); }
        void OnValidate() { if (_mat != null) Apply(); }
        void Update() { if (_mat != null) Apply(); }

        void Build()
        {
            if (_box != null) return;
            var sh = Shader.Find("Clay/VolumetricNebula");
            if (sh == null) { Debug.LogError("[VolumetricNebula] Shader 'Clay/VolumetricNebula' not found."); return; }
            _mat = new Material(sh);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "NebulaVolume";
            go.hideFlags = HideFlags.DontSave;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            go.transform.SetParent(transform, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _box = go.transform;
        }

        void Apply()
        {
            if (_mat == null || _box == null) return;
            _box.localScale = new Vector3(Size * Shape.x, Size * Shape.y, Size * Shape.z);
            _mat.SetFloat("_Class", NebulaClass);
            _mat.SetColor("_Emission", Emission);
            _mat.SetColor("_Emission2", Emission2);
            _mat.SetColor("_Emission3", Emission3);
            _mat.SetColor("_LightColor", LightColor);
            _mat.SetColor("_Shadowed", Shadowed);
            _mat.SetVector("_LightPos", StarOffset / Mathf.Max(Size, 1e-3f));   // → object space (unit cube)
            _mat.SetFloat("_Freq", Frequency);
            _mat.SetFloat("_Warp", Warp);
            _mat.SetFloat("_DensityMul", Density);
            _mat.SetFloat("_Threshold", Threshold);
            _mat.SetFloat("_Absorb", Absorption);
            _mat.SetFloat("_ShadowStrength", ShadowStrength);
            _mat.SetFloat("_EmissionMul", EmissionStrength);
            _mat.SetFloat("_Anisotropy", Anisotropy);
            _mat.SetFloat("_ScatterMul", ScatterStrength);
            _mat.SetFloat("_Ambient", Ambient);
            _mat.SetFloat("_Steps", Steps);
            _mat.SetFloat("_LightSteps", LightSteps);
            var r = new System.Random(Seed);
            _mat.SetVector("_Seed", new Vector4((float)r.NextDouble() * 20f, (float)r.NextDouble() * 20f, (float)r.NextDouble() * 20f, 0f));
        }
    }
}
