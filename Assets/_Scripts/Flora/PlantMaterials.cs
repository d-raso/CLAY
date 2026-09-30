using UnityEngine;

namespace CLAY.Flora
{
    /// Builds and configures the three CLAY/Flora materials (wood, foliage, flower) for a genome — shared by the
    /// Flora Lab and the planet-surface ecology so a plant looks identical in both.
    public static class PlantMaterials
    {
        public static Material Make(bool twoSided)
        {
            var m = new Material(Shader.Find("CLAY/Flora")) { enableInstancing = true };
            m.SetFloat("_Smoothness", twoSided ? 0.5f : 0.2f);   // leaves/petals read waxy; bark matte
            m.SetFloat("_SpecStrength", twoSided ? 0.5f : 0.15f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetFloat("_TwoSided", twoSided ? 1f : 0f);
            return m;
        }

        public static void Apply(PlantGenome g, Material wood, Material leaf, Material flower)
        {
            Color stemCol = StemColor(g.stemMaterial, g.pigment, g.woodColor);
            wood.SetColor("_BaseColor", stemCol); wood.SetColor("_AccentColor", stemCol * 1.15f);
            wood.SetFloat("_StemMat", g.stemMaterial); wood.SetFloat("_BarkType", (int)g.barkType);
            wood.SetFloat("_BarkScale", g.barkScale);
            wood.SetFloat("_BarkWarp", g.barkWarp);
            wood.SetFloat("_BarkNoise", g.barkNoise);
            wood.SetFloat("_StemDetail", 1f);
            wood.SetFloat("_BumpStrength", Mathf.Lerp(0.03f, 0.09f, g.stemMaterial) * g.barkRelief);
            float sm = g.stemMaterial;
            float smooth = sm < 0.33f ? Mathf.Lerp(0.1f, 0.4f, sm / 0.33f)
                         : sm < 0.66f ? Mathf.Lerp(0.4f, 0.28f, (sm - 0.33f) / 0.33f)
                         : Mathf.Lerp(0.28f, 0.12f, (sm - 0.66f) / 0.34f);
            wood.SetFloat("_Smoothness", smooth); wood.SetFloat("_SpecStrength", Mathf.Lerp(0.15f, 0.55f, smooth));
            wood.SetFloat("_LeafDetail", 0f);
            wood.SetFloat("_Glow", 0f);

            leaf.SetColor("_BaseColor", g.pigment); leaf.SetColor("_AccentColor", g.accent);
            leaf.SetColor("_Underside", g.underside);
            leaf.SetFloat("_PlantHeight", Mathf.Max(g.heightM, 0.2f)); leaf.SetFloat("_Understorey", 0.5f);
            leaf.SetFloat("_Translucency", g.leafTranslucency);
            leaf.SetFloat("_LeafDetail", 1f);
            leaf.SetFloat("_LeafStyle", (int)g.leafStyle);
            leaf.SetFloat("_LeafBump", 0.015f + g.leafJitter * 0.025f + g.organRelief * 0.015f);
            leaf.SetFloat("_Glow", g.glow);

            flower.SetColor("_BaseColor", g.flowerColor); flower.SetColor("_AccentColor", g.accent);
            flower.SetColor("_Underside", g.flowerColor * 0.8f);
            flower.SetFloat("_LeafBump", 0.02f + g.organRelief * 0.04f);
            flower.SetFloat("_LeafDetail", 1f);
            flower.SetFloat("_LeafStyle", (int)g.organTexture);
            flower.SetFloat("_Glow", g.glow * 1.3f);
        }

        public static Color StemColor(float mat, Color pigment, Color wood)
        {
            Color spongy = new Color(0.62f, 0.66f, 0.52f);
            Color plant = Color.Lerp(pigment, new Color(0.32f, 0.5f, 0.26f), 0.5f);
            Color fibrous = new Color(0.66f, 0.56f, 0.4f);
            if (mat < 0.33f) return Color.Lerp(spongy, plant, mat / 0.33f);
            if (mat < 0.66f) return Color.Lerp(plant, fibrous, (mat - 0.33f) / 0.33f);
            return Color.Lerp(fibrous, wood, (mat - 0.66f) / 0.34f);
        }
    }
}
