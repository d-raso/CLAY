using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CLAY.Galaxy
{
    /// <summary>
    /// Procedural planet surface. Samples 3D noise on each sphere-surface DIRECTION (so terrain is genuinely
    /// coordinate-mapped — height is a function of lat/long) and composes real geology: redistributed
    /// continents vs. ocean basins, masked mountain ranges, Worley impact craters, tectonic rift valleys,
    /// and rare "exotic" mineral/erosion overlays inspired by real processes (banded-iron rust, sulfur
    /// crusts, evaporite salt flats, verdigris/phosphate staining, carbon plains). Land is gently displaced
    /// so it rises above the ocean shell; fine relief is carried in the shading normal. Colour is delivered
    /// in UV channel 1 (reliable under URP 2D). The vertex heights ARE the extractable terrain map.
    /// </summary>
    public static class PlanetTexture
    {
        /// <summary>Live-tunable global multipliers for the terrain look (driven by the viewer's knob panel).</summary>
        [System.Serializable]
        public class PlanetTuning
        {
            public float contFreq = 1f;   // continent scale
            public float warp = 1f;       // domain-warp strength (coastline organicness)
            public float mountains = 1f;  // ridged mountain amplitude
            public float detail = 1f;     // fine high-frequency surface roughness
            public float relief = 1f;     // vertical displacement scale
            public float seaLevel = 0f;   // additive sea-level offset
            public float craters = 1f;    // crater amplitude (airless worlds)
        }
        public static PlanetTuning Tuning = new PlanetTuning();

        // ── value noise ───────────────────────────────────────────────────────────────────────────
        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }
        static Vector3 Hash3(int x, int y, int z)
        {
            return new Vector3(Hash(x, y, z), Hash(x + 91, y + 47, z + 13), Hash(x + 29, y + 83, z + 51));
        }
        static float VNoise(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float xf = p.x - xi, yf = p.y - yi, zf = p.z - zi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
            float c000 = Hash(xi, yi, zi),     c100 = Hash(xi + 1, yi, zi);
            float c010 = Hash(xi, yi + 1, zi), c110 = Hash(xi + 1, yi + 1, zi);
            float c001 = Hash(xi, yi, zi + 1),     c101 = Hash(xi + 1, yi, zi + 1);
            float c011 = Hash(xi, yi + 1, zi + 1), c111 = Hash(xi + 1, yi + 1, zi + 1);
            float x00 = Mathf.Lerp(c000, c100, u), x10 = Mathf.Lerp(c010, c110, u);
            float x01 = Mathf.Lerp(c001, c101, u), x11 = Mathf.Lerp(c011, c111, u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
        }
        static float Fbm(Vector3 p, int oct)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < oct; i++) { sum += VNoise(p * freq) * amp; norm += amp; freq *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }
        static float Ridged(Vector3 p, int oct)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < oct; i++) { float n = 1f - Mathf.Abs(VNoise(p * freq) * 2f - 1f); sum += n * n * amp; norm += amp; freq *= 2.1f; amp *= 0.5f; }
            return sum / norm;
        }
        // Cellular / Worley: nearest (f1) and second-nearest (f2) feature-point distances + nearest cell id.
        static void Worley(Vector3 p, out float f1, out float f2, out float cellRand)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            f1 = f2 = 9e9f; cellRand = 0f;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                Vector3 feat = new Vector3(cx, cy, cz) + Hash3(cx, cy, cz);
                float d = (feat - p).magnitude;
                if (d < f1) { f2 = f1; f1 = d; cellRand = Hash(cx + 7, cy + 3, cz + 5); }
                else if (d < f2) f2 = d;
            }
        }

        // ── chemical themes ──────────────────────────────────────────────────────────────────────
        // A planet's dominant surface chemistry, chosen from temperature + composition, drives ALL of its
        // colours (liquid, land ramp, ice, accents, atmosphere) — so worlds break out of the Earth/Mars loop.
        public enum ChemTheme { Silicate, Ferrous, Sulfuric, Carbonaceous, Methanic, AmmoniaIce, SalineIce, Biosphere,
                                Lava, Basaltic, Cupric, Halide, Evaporite, Tholin,
                                Metallic, Snowball, Corundum, Pelagic }

        // Weighted pick of an archetype (spreads worlds across many looks instead of clustering).
        static ChemTheme WPick(ref DetRng r, params (ChemTheme t, float w)[] opts)
        {
            float tot = 0f; for (int i = 0; i < opts.Length; i++) tot += Mathf.Max(0f, opts[i].w);
            float x = r.Value * tot;
            for (int i = 0; i < opts.Length; i++) { x -= Mathf.Max(0f, opts[i].w); if (x <= 0f) return opts[i].t; }
            return opts[0].t;
        }

        static Color WPickCol(ref DetRng r, params (Color c, float w)[] opts)
        {
            float tot = 0f; for (int i = 0; i < opts.Length; i++) tot += Mathf.Max(0f, opts[i].w);
            float x = r.Value * tot;
            for (int i = 0; i < opts.Length; i++) { x -= Mathf.Max(0f, opts[i].w); if (x <= 0f) return opts[i].c; }
            return opts[0].c;
        }

        // Dominant photosynthetic pigment tuned to the HOST STAR'S SPECTRUM. Photosynthesis absorbs where the
        // star is bright and reflects the rest — so the plant colour we SEE is the light left over. Hot blue-white
        // stars flood the surface with high-energy light → efficient green/blue-green cover. Cooler K/M dwarfs
        // emit mostly red/infrared, so life reaches for longer wavelengths and appears yellow→orange→red, then
        // (on dim red dwarfs) deep red, retinal purple, or near-black to harvest what little light there is.
        static Color VegPigment(float starTempK, ref DetRng r)
        {
            if (starTempK <= 0f) starTempK = 5800f;   // fallback (e.g. externally-loaded systems) = Sun-like
            Color green = new Color(0.24f, 0.46f, 0.24f), teal = new Color(0.12f, 0.42f, 0.40f), olive = new Color(0.30f, 0.42f, 0.14f),
                  yellow = new Color(0.60f, 0.55f, 0.16f), orange = new Color(0.60f, 0.38f, 0.14f), red = new Color(0.52f, 0.17f, 0.15f),
                  darkred = new Color(0.32f, 0.10f, 0.10f), purple = new Color(0.34f, 0.16f, 0.42f), black = new Color(0.09f, 0.10f, 0.12f);
            if (starTempK > 6500f) return WPickCol(ref r, (green, 3f), (teal, 1.5f), (olive, 1f));                       // A/F: blue-rich → green/teal
            if (starTempK > 5300f) return WPickCol(ref r, (green, 3f), (olive, 1.2f), (teal, 1f), (yellow, 0.6f));       // G (Sun-like): green
            if (starTempK > 3900f) return WPickCol(ref r, (olive, 2f), (yellow, 1.5f), (orange, 1.5f), (red, 1f), (green, 0.8f)); // K: yellows/oranges
            return WPickCol(ref r, (red, 2f), (darkred, 2f), (purple, 1.8f), (black, 1.5f), (orange, 1f));              // M dwarf: red/purple/black
        }

        // Shift a colour in HSV: rotate hue, scale saturation & value. Used to spread each composition across its
        // real mineral gamut so two same-theme worlds still look distinct.
        static Color ShiftHSV(Color c, float dH, float sMul, float vMul)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + dH, 1f);
            var o = Color.HSVToRGB(h, Mathf.Clamp01(s * sMul), Mathf.Clamp01(v * vMul));
            o.a = c.a; return o;
        }

        // How far a composition's colour may plausibly drift in hue (0–1 wheel). Copper minerals span a wide arc
        // (verdigris teal → malachite green → azurite blue → cuprite red-brown); iron oxides a narrower ochre↔rust
        // arc; carbon/metal almost none. This is what breaks the "copper is always the same green" monotony.
        static float ThemeHueRange(ChemTheme t) => t switch
        {
            ChemTheme.Cupric => 0.10f, ChemTheme.Halide => 0.08f, ChemTheme.AmmoniaIce => 0.07f,
            ChemTheme.Silicate => 0.06f, ChemTheme.Ferrous => 0.05f, ChemTheme.Sulfuric => 0.045f,
            ChemTheme.Tholin => 0.05f, ChemTheme.Methanic => 0.05f, ChemTheme.Basaltic => 0.05f,
            ChemTheme.Corundum => 0.05f, ChemTheme.Evaporite => 0.05f, ChemTheme.Snowball => 0.05f,
            ChemTheme.Pelagic => 0.04f, ChemTheme.Carbonaceous => 0.03f, ChemTheme.Metallic => 0.03f,
            _ => 0.05f,
        };

        public struct ChemPalette
        {
            public ChemTheme theme;
            public bool hasLiquid, liquidShell, hasLineae;
            public Color oceanDeep, oceanShallow;     // liquid (water / methane / lava / ammonia)
            public Color landLow, landMid, landHigh;  // surface elevation ramp
            public Color ice, accent, atm, veg;
            public float accentAmt, vegAmt, dune;
        }

        /// <summary>Deterministic per-planet chemistry palette (same seed → same colours as the bake).</summary>
        public static ChemPalette Chem(PlanetData p, ulong seed)
        {
            var r = new DetRng(DetRng.Hash(seed, 0xC4E37A11UL));
            float tc = p.meanTempC, vol = p.volcanism, water = p.waterCoverage, mineral = p.mineralDiversity;
            bool hab = p.habClass == HabClass.Habitable;
            bool dry = water < 0.05f;
            var cp = new ChemPalette();
            Color V(Color c, float a) => new Color(Mathf.Clamp01(c.r * (1f + r.Range(-a, a))),
                                                    Mathf.Clamp01(c.g * (1f + r.Range(-a, a))),
                                                    Mathf.Clamp01(c.b * (1f + r.Range(-a, a))), 1f);
            Color C(float rr, float g, float b) => new Color(rr, g, b);

            // ── Archetype from physical CAUSES: temperature bands set what's molten/liquid/frozen; composition
            //    (volcanism, mineral/redox, water, dryness) spreads warm worlds across many looks. ──
            bool airless = !HasAtmosphere(p);
            if (hab) cp.theme = ChemTheme.Biosphere;
            else if (tc > 700f) cp.theme = ChemTheme.Lava;                                  // molten, close-in
            else if (tc > 320f) cp.theme = WPick(ref r, (ChemTheme.Sulfuric, 1f), (ChemTheme.Basaltic, 1f), (ChemTheme.Lava, 0.6f),
                (ChemTheme.Ferrous, 0.8f), (ChemTheme.Metallic, 0.6f + (airless ? 0.8f : 0f)), (ChemTheme.Corundum, 0.35f));
            // SalineIce = a Europa-style subsurface ocean under an ice shell → only where there's an internal HEAT
            // source to keep it liquid (tidal heating / volcanism). Otherwise cold water freezes solid = Snowball.
            else if (tc < -160f) cp.theme = WPick(ref r, (ChemTheme.Methanic, water > 0.05f ? 1.2f : 0.4f), (ChemTheme.Tholin, 1f),
                (ChemTheme.AmmoniaIce, 1f), (ChemTheme.Snowball, water > 0.25f ? 1.3f : 0f), (ChemTheme.SalineIce, water > 0.1f ? 2f * vol : 0f));
            else if (tc < -20f) cp.theme = WPick(ref r, (ChemTheme.Snowball, water > 0.25f ? 1.8f : 0f), (ChemTheme.SalineIce, water > 0.1f ? 2f * vol : 0f),
                (ChemTheme.AmmoniaIce, 0.8f), (ChemTheme.Silicate, 0.8f), (ChemTheme.Carbonaceous, 0.7f), (ChemTheme.Ferrous, 0.6f));
            else cp.theme = WPick(ref r,                                                    // temperate/warm — widest spread
                (ChemTheme.Pelagic, water > 0.75f ? 2.5f : 0f),                             // near-global ocean
                (ChemTheme.Silicate, 1.0f),
                (ChemTheme.Ferrous, 1.0f),
                (ChemTheme.Carbonaceous, 0.8f),
                (ChemTheme.Basaltic, 0.6f + 1.6f * vol),
                (ChemTheme.Sulfuric, 0.2f + 1.6f * Mathf.Clamp01(vol - 0.4f)),
                (ChemTheme.Cupric, 0.4f + 1.3f * mineral),
                (ChemTheme.Halide, 0.4f + 1.1f * mineral),
                (ChemTheme.Evaporite, dry ? 1.5f : 0.2f),
                (ChemTheme.Metallic, airless && mineral > 0.5f ? 1.0f : 0.15f),             // iron/Mercury-type
                (ChemTheme.Corundum, 0.15f + 0.5f * mineral),                               // Al-rich ruby world
                (ChemTheme.Tholin, 0.5f));

            switch (cp.theme)
            {
                case ChemTheme.Biosphere:
                    cp.hasLiquid = cp.liquidShell = true;
                    cp.oceanDeep = WaterDeep(p.waterChemistry); cp.oceanShallow = WaterShallow(p.waterChemistry);
                    // Pigment is tuned to the host star's spectrum (green under Sun-like/hotter stars, shifting to
                    // red / purple / black under cool red dwarfs), with per-world jitter for variety.
                    cp.veg = V(VegPigment(p.hostStarTempK, ref r), 0.12f); cp.vegAmt = 1f;
                    cp.landLow = V(C(0.62f,0.55f,0.42f), 0.12f); cp.landMid = V(C(0.45f,0.40f,0.32f), 0.12f); cp.landHigh = V(C(0.60f,0.57f,0.52f), 0.1f);
                    cp.ice = C(0.92f,0.94f,0.98f); cp.atm = C(0.42f,0.62f,1f);
                    break;
                case ChemTheme.Lava:
                    cp.hasLiquid = true; cp.liquidShell = false;
                    cp.oceanDeep = C(0.35f,0.05f,0.0f); cp.oceanShallow = C(1.0f,0.5f,0.08f);       // glowing molten seas
                    cp.landLow = V(C(0.07f,0.05f,0.05f), 0.2f); cp.landMid = V(C(0.16f,0.10f,0.09f), 0.2f); cp.landHigh = V(C(0.35f,0.20f,0.15f), 0.2f);
                    cp.accent = C(1.0f,0.45f,0.1f); cp.accentAmt = 0.6f;
                    cp.ice = C(0.3f,0.2f,0.2f); cp.atm = C(0.5f,0.15f,0.08f);
                    break;
                case ChemTheme.Basaltic:
                    cp.hasLiquid = cp.liquidShell = water > 0.05f && tc > -20f && tc < 90f;
                    cp.oceanDeep = C(0.05f,0.09f,0.14f); cp.oceanShallow = C(0.12f,0.22f,0.30f);
                    cp.landLow = V(C(0.10f,0.10f,0.11f), 0.25f); cp.landMid = V(C(0.20f,0.20f,0.22f), 0.2f); cp.landHigh = V(C(0.34f,0.34f,0.37f), 0.2f);
                    cp.accent = V(C(0.30f,0.28f,0.26f), 0.3f); cp.accentAmt = 0.3f;
                    cp.ice = C(0.85f,0.87f,0.9f); cp.atm = C(0.35f,0.38f,0.42f);
                    break;
                case ChemTheme.Cupric:   // oxidised copper minerals → striking teal/green (NOT chlorophyll)
                    cp.hasLiquid = cp.liquidShell = water > 0.04f && tc > -30f && tc < 90f;
                    cp.oceanDeep = C(0.03f,0.20f,0.20f); cp.oceanShallow = C(0.10f,0.45f,0.42f);
                    cp.landLow = V(C(0.16f,0.34f,0.30f), 0.15f); cp.landMid = V(C(0.24f,0.52f,0.44f), 0.15f); cp.landHigh = V(C(0.55f,0.72f,0.60f), 0.12f);
                    cp.accent = C(0.20f,0.85f,0.60f); cp.accentAmt = 0.5f;
                    cp.ice = C(0.90f,0.96f,0.94f); cp.atm = C(0.5f,0.75f,0.68f);
                    break;
                case ChemTheme.Halide:   // halogen/salt crusts → pale yellow-green
                    cp.hasLiquid = false;
                    cp.landLow = V(C(0.62f,0.66f,0.42f), 0.15f); cp.landMid = V(C(0.78f,0.80f,0.52f), 0.12f); cp.landHigh = V(C(0.90f,0.92f,0.72f), 0.1f);
                    cp.accent = C(0.55f,0.80f,0.30f); cp.accentAmt = 0.4f;
                    cp.ice = C(0.92f,0.95f,0.85f); cp.atm = C(0.75f,0.82f,0.55f);
                    break;
                case ChemTheme.Evaporite:   // dried salt flats → white/cream with coloured brine patches
                    cp.hasLiquid = false;
                    cp.landLow = V(C(0.80f,0.76f,0.66f), 0.1f); cp.landMid = V(C(0.90f,0.87f,0.78f), 0.08f); cp.landHigh = V(C(0.97f,0.96f,0.92f), 0.06f);
                    cp.accent = C(0.80f,0.45f,0.35f); cp.accentAmt = 0.4f; cp.dune = 0.4f;
                    cp.ice = C(0.97f,0.97f,0.95f); cp.atm = C(0.80f,0.78f,0.70f);
                    break;
                case ChemTheme.Tholin:   // organic haze/deposits → deep red-orange (Titan/Pluto)
                    cp.hasLiquid = cp.liquidShell = water > 0.04f && tc > -180f;
                    cp.oceanDeep = C(0.05f,0.03f,0.03f); cp.oceanShallow = C(0.20f,0.10f,0.06f);
                    cp.landLow = V(C(0.35f,0.16f,0.10f), 0.15f); cp.landMid = V(C(0.60f,0.30f,0.16f), 0.15f); cp.landHigh = V(C(0.82f,0.52f,0.30f), 0.12f);
                    cp.accent = C(0.70f,0.30f,0.15f); cp.accentAmt = 0.45f; cp.dune = 0.5f;
                    cp.ice = C(0.85f,0.78f,0.68f); cp.atm = C(0.80f,0.45f,0.25f);
                    break;
                case ChemTheme.Ferrous:   // iron oxide — but VARY between ochre / blood-red / dark rust
                    cp.hasLiquid = cp.liquidShell = water > 0.03f && tc > -40f && tc < 60f;
                    cp.oceanDeep = C(0.30f,0.12f,0.08f); cp.oceanShallow = C(0.55f,0.28f,0.18f);
                    float rf = r.Value;
                    Color fLow = rf < 0.33f ? C(0.38f,0.18f,0.10f) : rf < 0.66f ? C(0.48f,0.20f,0.10f) : C(0.26f,0.12f,0.09f);
                    cp.landLow = V(fLow, 0.1f); cp.landMid = V(C(0.62f,0.34f,0.20f), 0.14f); cp.landHigh = V(C(0.85f,0.62f,0.42f), 0.12f);
                    cp.accent = C(0.45f,0.16f,0.10f); cp.accentAmt = 0.4f;
                    cp.ice = C(0.93f,0.90f,0.90f); cp.atm = C(0.80f,0.60f,0.42f);
                    break;
                case ChemTheme.Sulfuric:
                    cp.hasLiquid = true; cp.liquidShell = false;
                    cp.oceanDeep = C(0.06f,0.03f,0.02f); cp.oceanShallow = C(0.75f,0.22f,0.05f);
                    cp.landLow = V(C(0.55f,0.45f,0.10f), 0.12f); cp.landMid = V(C(0.86f,0.76f,0.20f), 0.12f); cp.landHigh = V(C(0.96f,0.92f,0.62f), 0.1f);
                    cp.accent = C(0.50f,0.82f,0.22f); cp.accentAmt = 0.5f;
                    cp.ice = C(0.90f,0.85f,0.50f); cp.atm = C(0.85f,0.80f,0.35f);
                    break;
                case ChemTheme.Carbonaceous:   // graphite/tar → near-black
                    cp.hasLiquid = cp.liquidShell = water > 0.03f && tc > -60f && tc < 60f;
                    cp.oceanDeep = C(0.05f,0.05f,0.06f); cp.oceanShallow = C(0.20f,0.18f,0.14f);
                    cp.landLow = V(C(0.06f,0.06f,0.07f), 0.3f); cp.landMid = V(C(0.16f,0.16f,0.18f), 0.25f); cp.landHigh = V(C(0.38f,0.38f,0.42f), 0.2f);
                    cp.accent = C(0.56f,0.57f,0.62f); cp.accentAmt = 0.3f;
                    cp.ice = C(0.70f,0.72f,0.76f); cp.atm = C(0.30f,0.30f,0.34f);
                    break;
                case ChemTheme.Methanic:   // Titan-like — orange dunes + dark methane seas
                    cp.hasLiquid = cp.liquidShell = water > 0.04f;
                    cp.oceanDeep = C(0.02f,0.02f,0.03f); cp.oceanShallow = C(0.11f,0.08f,0.04f);
                    cp.landLow = V(C(0.30f,0.18f,0.06f), 0.15f); cp.landMid = V(C(0.60f,0.42f,0.16f), 0.12f); cp.landHigh = V(C(0.80f,0.62f,0.30f), 0.1f);
                    cp.accent = C(0.40f,0.24f,0.08f); cp.accentAmt = 0.35f; cp.dune = 0.6f;
                    cp.ice = C(0.85f,0.82f,0.75f); cp.atm = C(0.85f,0.55f,0.20f);
                    break;
                case ChemTheme.AmmoniaIce:
                    cp.hasLiquid = cp.liquidShell = water > 0.04f;
                    cp.oceanDeep = C(0.30f,0.22f,0.50f); cp.oceanShallow = C(0.40f,0.70f,0.72f);
                    cp.landLow = V(C(0.80f,0.82f,0.90f), 0.06f); cp.landMid = V(C(0.70f,0.74f,0.85f), 0.06f); cp.landHigh = C(0.92f,0.94f,0.98f);
                    cp.accent = C(0.60f,0.50f,0.72f); cp.accentAmt = 0.3f;
                    cp.ice = C(0.94f,0.96f,1f); cp.atm = C(0.60f,0.70f,0.85f);
                    break;
                case ChemTheme.SalineIce:   // Europa-like — white ice + reddish fractures (subsurface ocean)
                    cp.hasLiquid = false;
                    cp.landLow = V(C(0.78f,0.80f,0.83f), 0.05f); cp.landMid = C(0.83f,0.85f,0.88f); cp.landHigh = C(0.89f,0.91f,0.94f);
                    cp.accent = C(0.60f,0.30f,0.22f); cp.accentAmt = 0.55f; cp.hasLineae = true;
                    cp.ice = C(0.89f,0.91f,0.94f); cp.atm = C(0.70f,0.75f,0.85f);
                    break;
                case ChemTheme.Metallic:   // iron world / Mercury — bare metallic grey, dark maria, heavy craters
                    cp.hasLiquid = false;
                    cp.landLow = V(C(0.20f,0.19f,0.19f), 0.15f); cp.landMid = V(C(0.42f,0.41f,0.40f), 0.12f); cp.landHigh = V(C(0.70f,0.68f,0.65f), 0.1f);
                    cp.accent = C(0.30f,0.26f,0.22f); cp.accentAmt = 0.3f;   // dark iron maria
                    cp.ice = C(0.85f,0.86f,0.9f); cp.atm = C(0.4f,0.4f,0.42f);
                    break;
                case ChemTheme.Snowball:   // globally frozen ocean — white/blue ice with fractures
                    cp.hasLiquid = false; cp.hasLineae = true;
                    cp.landLow = V(C(0.50f,0.62f,0.74f), 0.06f); cp.landMid = V(C(0.64f,0.74f,0.82f), 0.05f); cp.landHigh = C(0.82f,0.87f,0.92f);
                    cp.accent = C(0.40f,0.52f,0.64f); cp.accentAmt = 0.3f;
                    cp.ice = C(0.82f,0.87f,0.92f); cp.atm = C(0.7f,0.8f,0.92f);
                    break;
                case ChemTheme.Corundum:   // Al-rich ruby/sapphire world — deep translucent red gem terrain
                    cp.hasLiquid = false;
                    cp.landLow = V(C(0.32f,0.05f,0.08f), 0.15f); cp.landMid = V(C(0.60f,0.12f,0.16f), 0.15f); cp.landHigh = V(C(0.88f,0.42f,0.44f), 0.12f);
                    cp.accent = C(0.95f,0.30f,0.35f); cp.accentAmt = 0.45f;
                    cp.ice = C(0.90f,0.86f,0.9f); cp.atm = C(0.7f,0.35f,0.4f);
                    break;
                case ChemTheme.Pelagic:    // near-global ocean world — deep blue, rare land
                    cp.hasLiquid = cp.liquidShell = true;
                    cp.oceanDeep = C(0.02f,0.09f,0.24f); cp.oceanShallow = C(0.10f,0.38f,0.58f);
                    cp.landLow = V(C(0.30f,0.34f,0.32f), 0.1f); cp.landMid = V(C(0.45f,0.44f,0.40f), 0.1f); cp.landHigh = V(C(0.66f,0.64f,0.60f), 0.1f);
                    cp.accent = C(0.35f,0.45f,0.42f); cp.accentAmt = 0.2f;
                    cp.ice = C(0.92f,0.95f,0.99f); cp.atm = C(0.4f,0.6f,0.95f);
                    break;
                default: // Silicate — grey / tan / pinkish rock (varied), water if temperate
                    cp.hasLiquid = cp.liquidShell = water > 0.03f && tc > -30f && tc < 80f;
                    cp.oceanDeep = C(0.06f,0.16f,0.34f); cp.oceanShallow = C(0.16f,0.42f,0.60f);
                    float sg = r.Value;
                    Color sMid = sg < 0.4f ? C(0.55f,0.50f,0.44f) : sg < 0.7f ? C(0.48f,0.48f,0.50f) : C(0.58f,0.48f,0.48f);
                    cp.landLow = V(C(0.34f,0.31f,0.28f), 0.14f); cp.landMid = V(sMid, 0.14f); cp.landHigh = V(C(0.74f,0.70f,0.64f), 0.1f);
                    cp.accent = C(0.50f,0.45f,0.38f); cp.accentAmt = 0.25f;
                    cp.ice = C(0.92f,0.94f,0.98f); cp.atm = C(0.55f,0.62f,0.78f);
                    break;
            }
            // ── Per-world colour gamut spread WITHIN the composition ──
            // A single bounded hue rotation (plus saturation/value drift keyed to mineral richness) applied
            // coherently across the whole palette, so the elevation ramp stays consistent but the world's overall
            // tint differs from its same-composition siblings. Fresh, low-mineral crust is muted; rich crust vivid.
            if (cp.theme != ChemTheme.Biosphere && cp.theme != ChemTheme.Lava)
            {
                float hr = ThemeHueRange(cp.theme);
                float dH = r.Range(-hr, hr);
                float sMul = Mathf.Clamp(1f + r.Range(-0.28f, 0.16f) + (mineral - 0.5f) * 0.35f, 0.35f, 1.5f);
                float vMul = 1f + r.Range(-0.12f, 0.12f);
                cp.landLow = ShiftHSV(cp.landLow, dH, sMul, vMul);
                cp.landMid = ShiftHSV(cp.landMid, dH, sMul, vMul);
                cp.landHigh = ShiftHSV(cp.landHigh, dH, sMul, vMul);
                cp.accent = ShiftHSV(cp.accent, dH * 1.3f, sMul, vMul);
                if (cp.hasLiquid) { cp.oceanDeep = ShiftHSV(cp.oceanDeep, dH, sMul, vMul); cp.oceanShallow = ShiftHSV(cp.oceanShallow, dH, sMul, vMul); }
            }

            // Surface liquid needs atmospheric PRESSURE to be stable — an airless world can't hold oceans (its
            // liquid would boil/sublimate away). Molten rock (Lava/Sulfuric) is exempt: it needs no atmosphere.
            if (airless && cp.theme != ChemTheme.Lava && cp.theme != ChemTheme.Sulfuric)
            { cp.hasLiquid = false; cp.liquidShell = false; }

            // Dry, temperate/warm rock worlds are wind-swept deserts → dune fields.
            if (dry && cp.hasLiquid == false && tc > -20f && tc < 140f
                && (cp.theme == ChemTheme.Silicate || cp.theme == ChemTheme.Ferrous || cp.theme == ChemTheme.Basaltic))
                cp.dune = Mathf.Max(cp.dune, 0.4f);
            return cp;
        }

        // ── per-planet parameters ─────────────────────────────────────────────────────────────────
        internal enum Exotic { None, Sulfur, IronOxide, Salt, Carbon, Verdigris }

        internal struct PP
        {
            public Vector3 o0, o1, o2, o3, o4, o5;
            public Vector3 dichAxis;               // hemispheric-dichotomy axis (old vs young/resurfaced hemisphere)
            public float dichAmt;                  // 0 = uniform world … 1 = strong two-faced dichotomy
            public float contFreq, contPower, seaLevel, capLat, snowStart, moistFreq;
            public float relief, mtnAmp, warp, detail, craterAmp, craterFreq, volcAmp, bump;
            public float volcanism, mineral, waterCov, tempC;
            public int rockStyle;
            public bool locked;                    // tidally locked → eyeball thermal pattern (substellar hot, night frozen)
            public TectonicMode tect;              // crustal regime → mountains / craters / resurfacing
            public float oxidation;                // 0 reducing (grey basalt) → 1 oxidizing (red rust)
            public float bombardment, tidalHeat, rotationHours;   // cross-body/history causes for surface features
            public bool hasAtmo;                   // atmosphere present → wind streaks, precipitation erosion
            public PlanetType type; public bool hab; public WaterChemistry chem;
            public Exotic exotic; public float exoticAmt; public Color exoticCol;
            public ChemPalette cp;
        }

        // Crustal regime — a [NEW] causal factor. Drives whether a world shows linear mountain belts and young,
        // uncratered crust (mobile-lid plate tectonics), old heavily-cratered plains (stagnant-lid / dead), or
        // volcanic resurfacing (heat-pipe, Io-like). Inferred from mass, internal heat (volcanism proxies youth
        // + tidal heating) and water (which lubricates subduction).
        // Gas/ice-giant subtypes (roadmap §K) — emerge from mass, temperature (orbit distance) and a deterministic
        // minority roll. Drive both the look (bands/turbulence/storms/haze/palette) and the display name.
        public enum GiantType { HotJupiter, ClassicJovian, IceGiant, SubNeptune, HeliumGiant, Superstorm }
        public static GiantType GiantSubtype(PlanetData p, ulong seed)
        {
            if (p.mass < 25f) return GiantType.SubNeptune;            // gas dwarf — small, hazy, featureless (low-mass ice giants)
            if (p.type == PlanetType.IceGiant) return GiantType.IceGiant;
            if (p.meanTempC > 400f) return GiantType.HotJupiter;      // close-in, puffed, scorched
            var r = new DetRng(seed ^ 0x611A27E5UL);
            float roll = r.Value;
            if (roll < 0.14f) return GiantType.HeliumGiant;          // H rained out → pale, near-featureless
            if (roll < 0.34f) return GiantType.Superstorm;           // storm-dominated
            return GiantType.ClassicJovian;
        }

        public enum TectonicMode { HeatPipe, MobileLid, StagnantLid, Dead }
        public static TectonicMode Tectonics(PlanetData p)
        {
            if (!PlanetData.IsRocky(p.type)) return TectonicMode.Dead;
            float heat = p.volcanism;                                   // youth / tidal-heat proxy
            if (heat > 0.72f) return TectonicMode.HeatPipe;             // Io-like, resurfaced faster than it craters
            bool wet = p.waterCoverage > 0.18f;
            bool massOk = p.mass > 0.55f && p.mass < 5f;               // too small = no convection, too big = stagnant lid
            if (wet && massOk && heat > 0.30f) return TectonicMode.MobileLid;
            if (heat < 0.18f && p.mass < 1.2f) return TectonicMode.Dead;
            return TectonicMode.StagnantLid;
        }

        static PP MakeParams(PlanetData p, ulong seed)
        {
            var r = new DetRng(seed);
            Vector3 O() => new Vector3(r.Range(-120f, 120f), r.Range(-120f, 120f), r.Range(-120f, 120f));
            float warmth = Mathf.InverseLerp(-18f, 30f, p.meanTempC);
            bool rocky = PlanetData.IsRocky(p.type);
            bool airless = rocky && !HasAtmosphere(p);   // only airless worlds keep craters (no erosion)

            var t = Tuning;
            var pp = new PP
            {
                o0 = O(), o1 = O(), o2 = O(), o3 = O(), o4 = O(), o5 = O(),
                contFreq = r.Range(0.9f, 2.2f) * t.contFreq,          // some worlds few big continents, some many
                contPower = r.Range(1.0f, 2.4f),                      // archipelago (low) ↔ pangaea (high)
                moistFreq = r.Range(1.8f, 3.2f),
                seaLevel = Mathf.Clamp01(Mathf.Lerp(0.32f, 0.72f, p.waterCoverage) + t.seaLevel),
                // Cap latitude varies per world: some have big caps, many modest, some none (offset > ~0.25).
                // High axial tilt (obliquity) → extreme seasons melt the caps back, so they retreat or vanish.
                capLat = Mathf.Clamp(Mathf.Lerp(0.55f, 0.92f, warmth) + r.Range(-0.03f, 0.35f) + p.axialTiltDeg / 90f * 0.30f, 0.5f, 1.35f),
                rockStyle = r.RangeInt(0, 5),              // varied rock palette (not always Mars-red)
                snowStart = Mathf.Lerp(0.5f, 1.25f, warmth),
                mtnAmp = Mathf.Lerp(0.3f, 1.3f, p.volcanism) * r.Range(0.6f, 1.5f) * t.mountains,
                warp = Mathf.Lerp(0.3f, 0.9f, r.Value) * t.warp,     // per-world coastline character
                detail = r.Range(0.6f, 1.5f) * t.detail,
                craterAmp = (airless ? r.Range(0.4f, 1f) : 0f) * t.craters,
                craterFreq = r.Range(6f, 12f),
                volcAmp = (rocky && p.volcanism > 0.45f) ? p.volcanism * r.Range(0.5f, 1f) : 0f,
                bump = 1.6f,
                volcanism = p.volcanism, mineral = p.mineralDiversity, waterCov = p.waterCoverage,
                tempC = p.meanTempC, type = p.type, hab = p.habClass == HabClass.Habitable, chem = p.waterChemistry,
                locked = p.tidallyLocked,
                bombardment = p.bombardment, tidalHeat = p.tidalHeat, rotationHours = p.rotationHours,
                hasAtmo = HasAtmosphere(p),
                cp = Chem(p, seed),
            };

            // ── Tectonic regime → relief/crater/volcanism signature ([NEW] causal factor) ──
            pp.tect = Tectonics(p);
            switch (pp.tect)
            {
                case TectonicMode.HeatPipe:      // resurfaced volcanic plains: no craters, strong volcanism, low ranges
                    pp.craterAmp = 0f; pp.volcAmp = Mathf.Max(pp.volcAmp, 0.7f) * r.Range(0.9f, 1.3f); pp.mtnAmp *= 0.7f; break;
                case TectonicMode.MobileLid:     // plate tectonics: linear belts, young crust erases most craters
                    pp.mtnAmp *= 1.55f; pp.craterAmp *= 0.2f; break;
                case TectonicMode.StagnantLid:   // one thick plate: old, cratered where dry, few new ranges
                    pp.mtnAmp *= 0.7f;
                    if (p.waterCoverage < 0.15f) pp.craterAmp = Mathf.Max(pp.craterAmp, r.Range(0.5f, 0.9f) * t.craters);
                    break;
                case TectonicMode.Dead:          // no resurfacing: saturation cratering, almost no relief
                    pp.mtnAmp *= 0.45f;
                    pp.craterAmp = Mathf.Max(pp.craterAmp, r.Range(0.8f, 1.2f) * t.craters); break;
            }

            // Heavy bombardment (near a debris belt / young system) craters the surface whatever its tectonics.
            pp.craterAmp = Mathf.Max(pp.craterAmp, p.bombardment * 0.9f * t.craters);

            // ── Feature ARRANGEMENT: a hemispheric dichotomy axis so features organise into an old, cratered face
            // and a young, resurfaced one (Mars/Moon-style), rather than spreading uniformly. Strength grows with
            // bombardment and a giant-impact roll; a mobile-lid (constantly recycled) crust rarely keeps one. ──
            pp.dichAxis = new Vector3(r.Range(-1f, 1f), r.Range(-1f, 1f), r.Range(-1f, 1f)).normalized;
            float dich = 0.25f + p.bombardment * 0.5f + (r.Value < 0.3f ? r.Range(0.2f, 0.5f) : 0f);
            if (pp.tect == TectonicMode.MobileLid) dich *= 0.4f;    // plate recycling erases a two-faced crust
            pp.dichAmt = Mathf.Clamp01(dich);

            // ── Redox / oxygenation state ([NEW] causal factor): fresh volcanic rock is reducing (grey basalt);
            // wet, living, and old surfaces oxidize toward rust. Drives a grey↔rust hue shift + banded-iron look.
            pp.oxidation = Mathf.Clamp01(0.12f + p.waterCoverage * 0.5f + (pp.hab ? 0.25f : 0f) + (1f - p.volcanism) * 0.30f);

            pp.relief = Mathf.Lerp(0.035f, 0.075f, Mathf.Clamp01(pp.mtnAmp / 1.1f)) * t.relief;

            // Rare "this planet is weird" overlay — a mineral/erosion signature, biased by water chemistry.
            pp.exotic = Exotic.None;
            float roll = r.Value;
            if (roll < 0.42f)
            {
                pp.exotic = p.waterChemistry switch
                {
                    WaterChemistry.Sulfide => r.Value < 0.6f ? Exotic.Sulfur : Exotic.Salt,
                    WaterChemistry.Iron => Exotic.IronOxide,
                    WaterChemistry.Phosphate => r.Value < 0.6f ? Exotic.Verdigris : Exotic.Salt,
                    _ => r.Value < 0.5f ? Exotic.Salt : Exotic.Carbon,
                };
                pp.exoticAmt = r.Range(0.25f, 0.7f);
            }
            pp.exoticCol = pp.exotic switch
            {
                Exotic.Sulfur => new Color(0.86f, 0.76f, 0.20f),
                Exotic.IronOxide => new Color(0.55f, 0.18f, 0.12f),
                Exotic.Salt => new Color(0.90f, 0.88f, 0.82f),
                Exotic.Carbon => new Color(0.10f, 0.10f, 0.12f),
                Exotic.Verdigris => new Color(0.20f, 0.62f, 0.55f),
                _ => Color.gray,
            };
            return pp;
        }

        // "Age" of the crust at this point: 1 = old, cratered, primordial highland; 0 = young, resurfaced maria.
        // Built from the hemispheric dichotomy (one face older than the other) times a few large low-frequency
        // provinces, so cratering, volcanism and colour all cluster into regions instead of blanketing the globe.
        static float OldTerrane(Vector3 dir, in PP pp)
        {
            float hemi = Mathf.Clamp01(Vector3.Dot(dir, pp.dichAxis) * 0.5f + 0.5f);
            float hemiOld = Mathf.Lerp(1f, hemi, pp.dichAmt);                          // dichAmt 0 → uniform
            float patch = Astrophysics.Smoothstep(0.34f, 0.66f, Fbm(dir * 1.4f + pp.o5, 3));
            return Mathf.Clamp01(hemiOld * (0.35f + 0.65f * patch));
        }

        // ── heightmap ─────────────────────────────────────────────────────────────────────────────
        // Returns 0..1 elevation; sea level lives at pp.seaLevel.
        static float Height(Vector3 dir, in PP pp)
        {
            Vector3 p = dir * pp.contFreq + pp.o0;
            // domain warp for organic coastlines (warp a second field so it isn't self-referential)
            Vector3 w = new Vector3(Fbm(p + pp.o1, 3), Fbm(p + pp.o2, 3), Fbm(p + pp.o3, 3)) - Vector3.one * 0.5f;
            float cont = Fbm(p + w * pp.warp, 6);
            // redistribute per-world: low power → island archipelagos, high power → big consolidated landmasses
            float h = Mathf.Pow(Mathf.Clamp01(cont), pp.contPower) * 1.15f;

            // mountain belts — ridged fBm, masked to plate-collision zones and to land only
            float belt = Astrophysics.Smoothstep(0.5f, 0.8f, Fbm(dir * (pp.contFreq * 0.55f) + pp.o2, 3));
            float landMask = Astrophysics.Smoothstep(pp.seaLevel - 0.02f, pp.seaLevel + 0.12f, cont);
            float mtn = Ridged(dir * (pp.contFreq * 2.0f) + pp.o1, 5);
            h += mtn * belt * landMask * 0.3f * pp.mtnAmp;

            // fine high-frequency roughness so surfaces read as terrain, not smooth plastic (no big cells)
            h += (Fbm(dir * (pp.contFreq * 7f) + pp.o4, 4) - 0.5f) * 0.05f * pp.detail;

            // impact craters — airless worlds; three overlaid scales give a real size/depth distribution
            if (pp.craterAmp > 0.02f)
            {
                float c = CraterLayer(dir, pp.craterFreq * 0.5f, pp.o3)                       // rare, large, deep
                        + CraterLayer(dir, pp.craterFreq * 1.4f, pp.o3 + new Vector3(60f, 0f, 0f)) * 0.55f
                        + CraterLayer(dir, pp.craterFreq * 3.2f, pp.o3 + new Vector3(0f, 60f, 0f)) * 0.25f; // many small
                // Cratering concentrates on OLD terrane — the young/resurfaced hemisphere and volcanic maria stay
                // smooth, giving the world a two-faced highland/lowland arrangement.
                float old = Mathf.Lerp(1f, OldTerrane(dir, pp), 0.85f);
                h += c * pp.craterAmp * 0.4f * old;
            }

            // volcanoes — sparse large cones with a summit caldera, on volcanic worlds
            if (pp.volcAmp > 0.02f)
            {
                Worley(dir * 4.5f + pp.o1, out float vf1, out float _, out float vr);
                if (vr > 0.72f && vf1 < 0.16f)
                {
                    float u = vf1 / 0.16f;
                    float cone = (1f - u) * (1f - u);
                    float caldera = -Mathf.Exp(-70f * u * u) * 0.5f;
                    h += (cone + caldera) * pp.volcAmp * 0.35f;
                }
            }
            return Mathf.Clamp01(h);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        // One Worley cell = at most one crater. Size and depth vary per cell so no two look alike; the rim is
        // a thin sharp ring (not a puffed-out donut) and big craters get flatter floors.
        static float CraterLayer(Vector3 dir, float freq, Vector3 off)
        {
            Worley(dir * freq + off, out float cf1, out float _, out float cr);
            if (cr < 0.35f) return 0f;                              // ~a third of cells are cratered
            float radius = Mathf.Lerp(0.15f, 0.5f, Frac(cr * 7.13f));
            if (cf1 >= radius) return 0f;
            float u = cf1 / radius;                                 // 0 centre → 1 edge
            float depth = Mathf.Lerp(0.25f, 1f, Frac(cr * 23.7f));  // shallow ↔ deep
            float floorFlat = Mathf.Lerp(2f, 1.2f, depth);          // big/deep ones read flatter-floored
            float bowl = -(1f - Mathf.Pow(u, floorFlat)) * depth;
            float rim = Mathf.Exp(-55f * (u - 0.92f) * (u - 0.92f)) * 0.12f * depth;
            return bowl + rim;
        }

        // ── colour ────────────────────────────────────────────────────────────────────────────────
        static Color Surface(in PP pp, Vector3 dir, float h, float slope)
        {
            float latAbs = Mathf.Abs(dir.y);

            if (pp.type == PlanetType.GasGiant || pp.type == PlanetType.IceGiant)
            {
                bool gas = pp.type == PlanetType.GasGiant;
                float freq = gas ? 11f : 6.5f;

                // Differential-rotation swirl: warp the band latitude by flow fields so bands wave, shear and
                // form festoons, then break them with turbulence — Jupiter-like, not flat stripes.
                float flow = Fbm(dir * 2.3f + pp.o1, 5) - 0.5f;
                float swirl = Fbm(dir * 1.3f + pp.o2, 4) - 0.5f;
                float latB = dir.y + flow * 0.5f + swirl * 0.3f;
                float bands = Mathf.Sin(latB * Mathf.PI * freq) * 0.5f + 0.5f;
                float turb = Fbm(dir * 9f + pp.o3, 5) - 0.5f;
                bands = Mathf.Clamp01(bands + turb * 0.35f);
                bands = Astrophysics.Smoothstep(0.2f, 0.8f, bands);    // crisper belt/zone contrast

                Color g = gas ? RampGas(bands) : RampIce(bands);

                // Overlay a second, coarser band system in a slightly shifted hue for richness.
                float band2 = Astrophysics.Smoothstep(0.4f, 0.7f, Fbm(dir * 3.2f + pp.o4, 4));
                g = Color.Lerp(g, g * (gas ? new Color(0.7f, 0.55f, 0.45f) : new Color(0.7f, 0.85f, 1f)), band2 * 0.35f);

                // Vortex storms — oval spots that punch through the banding, biased to mid-latitudes.
                float spot = Astrophysics.Smoothstep(0.72f, 0.92f, Fbm(dir * 5f + pp.o4, 4));
                spot *= Astrophysics.Smoothstep(0.05f, 0.3f, latAbs) * (1f - Astrophysics.Smoothstep(0.55f, 0.82f, latAbs));
                Color stormCol = pp.exotic != Exotic.None ? pp.exoticCol
                               : gas ? new Color(0.86f, 0.45f, 0.28f) : new Color(0.55f, 0.75f, 0.9f);
                g = Color.Lerp(g, stormCol, spot * 0.7f);
                return g;
            }

            var cp = pp.cp;
            bool water = cp.hasLiquid && pp.waterCov > 0.02f && h < pp.seaLevel;
            Color col;
            if (water)
            {
                float depth = Mathf.Clamp01((pp.seaLevel - h) / Mathf.Max(0.02f, pp.seaLevel));
                col = Color.Lerp(cp.oceanShallow, cp.oceanDeep, depth);   // liquid tint (methane/lava/water/ammonia)
            }
            else
            {
                float e = cp.hasLiquid ? Mathf.InverseLerp(pp.seaLevel, 1f, h) : h;            // elevation above sea
                float moist = Fbm(dir * pp.moistFreq + pp.o2, 4);                              // wet/dry biome variety
                // Climate: normally by latitude, but a TIDALLY-LOCKED world is an "eyeball" — warmth is set by the
                // angle from the substellar point (+Z faces the star), hot on the day side, frozen on the night side.
                float heat = Mathf.Clamp01(dir.z * 0.5f + 0.5f);
                float warm = pp.locked
                    ? Mathf.InverseLerp(-15f, 30f, pp.tempC) * Mathf.Clamp01(heat * 1.35f - 0.15f)
                    : Mathf.InverseLerp(-15f, 30f, pp.tempC) * (1f - latAbs * 0.7f);

                // Elevation ramp from the chemical theme (rust / graphite / sulfur / tholin / ice / rock…).
                Color land = e < 0.4f ? Color.Lerp(cp.landLow, cp.landMid, e / 0.4f)
                                      : Color.Lerp(cp.landMid, cp.landHigh, (e - 0.4f) / 0.6f);

                // Redox: grey basalt/silicate crust rusts toward iron-oxide as the world oxidizes (weathering is
                // stronger on exposed highlands). Fresh volcanic worlds stay grey; wet/old/living worlds go red.
                if (cp.theme == ChemTheme.Silicate || cp.theme == ChemTheme.Basaltic)
                    land = Color.Lerp(land, new Color(0.52f, 0.27f, 0.17f), pp.oxidation * 0.5f * (0.6f + 0.4f * e));

                // ── Feature ARRANGEMENT: crust age organises the surface into provinces ──
                bool icyTheme = cp.theme == ChemTheme.Snowball || cp.theme == ChemTheme.SalineIce
                             || cp.theme == ChemTheme.AmmoniaIce || cp.theme == ChemTheme.Methanic;
                float old = OldTerrane(dir, pp);
                // Volcanic maria: dark smooth flood-basalt plains flooding the YOUNG/resurfaced provinces of a
                // volcanic world — a discrete region (like the lunar maria), not a global darkening.
                if (pp.volcanism > 0.25f && !icyTheme && !cp.hasLiquid)
                    land = Color.Lerp(land, new Color(0.11f, 0.10f, 0.10f),
                                      (1f - old) * Mathf.Clamp01(pp.volcanism * 1.4f) * 0.6f);
                // Old highland reads a touch brighter/dustier (accumulated regolith) — the other half of the dichotomy.
                if (pp.dichAmt > 0.15f && !icyTheme)
                    land *= 1f + (old - 0.5f) * 0.14f * pp.dichAmt;

                // Biosphere: vegetation in wet, warm lowlands (colour per world — green/purple/teal/dark).
                if (cp.vegAmt > 0.01f)
                {
                    float veg = Mathf.Clamp01(moist * 1.4f) * Mathf.Clamp01(warm + 0.2f)
                              * (1f - Astrophysics.Smoothstep(0.45f, 0.75f, e));
                    land = Color.Lerp(land, cp.veg, veg * cp.vegAmt);
                }

                // Steep + volcanic → dark basalt; flat + mineral-rich → lighter clay/accent shelves.
                float basalt = Mathf.Clamp01(slope * 1.7f) * pp.volcanism;
                land = Color.Lerp(land, new Color(0.13f, 0.11f, 0.10f), basalt * 0.65f);

                // Chemical accent staining (Fe oxide, sulfur, salt, verdigris) in patchy regions.
                if (cp.accentAmt > 0.01f)
                {
                    float stain = Astrophysics.Smoothstep(0.52f, 0.8f, Fbm(dir * (pp.moistFreq * 1.4f) + pp.o4, 4));
                    land = Color.Lerp(land, cp.accent, stain * cp.accentAmt);
                }

                // Europa-style lineae — long CURVED branching fractures. (Worley cell BOUNDARIES read as a
                // geometric cell mesh; ridged fBm on domain-warped coords gives natural crossing cracks instead.)
                if (cp.hasLineae)
                {
                    Vector3 w = dir + new Vector3(Fbm(dir * 2.2f + pp.o3, 3),
                                                   Fbm(dir * 2.2f + pp.o1, 3),
                                                   Fbm(dir * 2.2f + pp.o2, 3)) * 0.5f - Vector3.one * 0.25f;
                    float r1 = Ridged(w * 4f + pp.o3, 4);     // primary long fractures
                    float r2 = Ridged(w * 9f + pp.o1, 3);     // finer cross-fractures
                    float linea = Mathf.Max(Astrophysics.Smoothstep(0.88f, 1f, r1),
                                            Astrophysics.Smoothstep(0.92f, 1f, r2) * 0.6f);
                    land = Color.Lerp(land, cp.accent, linea * 0.85f);
                }

                // ── Geological features (colour-only, cause-driven) ──
                // Dunes: wind-rippled lowlands on dry worlds.
                if (cp.dune > 0.01f)
                {
                    float rip = Mathf.Sin(dir.y * 22f + Fbm(dir * 2.5f + pp.o1, 3) * 10f) * 0.5f + 0.5f;
                    land = Color.Lerp(land, land * (0.82f + 0.32f * rip), cp.dune * (1f - Astrophysics.Smoothstep(0.4f, 0.7f, e)));
                }
                // Canyons: dark eroded rifts where liquid ran or tectonics tore the crust.
                float canyonAmt = Mathf.Clamp01(pp.waterCov * 1.4f + pp.volcanism * 0.5f);
                if (canyonAmt > 0.05f)
                {
                    float rift = Astrophysics.Smoothstep(0.93f, 1f, Ridged(dir * 5f + pp.o4, 4));
                    land = Color.Lerp(land, land * 0.45f, rift * canyonAmt);
                }
                // Strata: fine sedimentary / banded-iron layering by elevation on dry mineral worlds.
                if (cp.theme == ChemTheme.Ferrous || cp.theme == ChemTheme.Evaporite
                    || cp.theme == ChemTheme.Silicate || cp.theme == ChemTheme.Basaltic)
                {
                    float strata = Mathf.Sin(e * 55f + Fbm(dir * 2f + pp.o2, 2) * 4f) * 0.5f + 0.5f;
                    // Banded-iron formation: an oxygenating world (mid oxidation + wet) lays down alternating
                    // rust/grey iron bands — the geological signature of the great-oxidation transition.
                    float bif = Mathf.Clamp01(1f - Mathf.Abs(pp.oxidation - 0.55f) / 0.35f) * Mathf.Clamp01(pp.waterCov * 2f);
                    if (bif > 0.02f)
                        land = Color.Lerp(land, Color.Lerp(new Color(0.50f, 0.24f, 0.15f), new Color(0.34f, 0.36f, 0.38f), strata), bif * 0.5f);
                    else
                        land = Color.Lerp(land, land * (0.88f + 0.24f * strata), 0.22f);
                }

                // ── Broken molten crust: a HOT and/or tidally-flexed world cracks into a glowing fissure network
                // (Io / 61 Vir style). A fast rotator shatters into a finer, denser crack pattern. ──
                float fast = pp.rotationHours > 0f ? Mathf.Clamp01((10f - pp.rotationHours) / 8f) : 0f;
                float moltenAmt = Mathf.Clamp01((pp.tempC - 200f) / 400f + pp.tidalHeat * 0.7f) * (0.55f + 0.75f * fast);
                if (moltenAmt > 0.12f && !icyTheme)
                {
                    float cf = 6f + 9f * fast;                              // fast spin → denser crack network
                    float crack = Ridged(dir * cf + pp.o3, 4);
                    float fiss = Astrophysics.Smoothstep(0.82f, 1f, crack) * moltenAmt;
                    Color glow = Color.Lerp(new Color(0.9f, 0.28f, 0.06f), new Color(1f, 0.78f, 0.28f), fiss);
                    land = Color.Lerp(land, glow, fiss);
                }

                // ── Impact basins: a few large, DISCRETE ringed basins — dark flooded floors ringed by bright
                // ejecta aprons — on heavily bombarded worlds, concentrated on the old terrane. ──
                if (pp.bombardment > 0.25f)
                {
                    Worley(dir * 2.3f + pp.o3, out float bf1, out float _, out float _);
                    float floorB = Astrophysics.Smoothstep(0.14f, 0.02f, bf1);              // 1 at basin centre
                    float apron = Astrophysics.Smoothstep(0.34f, 0.16f, bf1) * (1f - floorB); // bright ring outside
                    float amt = pp.bombardment * (0.4f + 0.6f * old);
                    land = Color.Lerp(land, land * 0.55f, floorB * amt * 0.8f);
                    land = Color.Lerp(land, land * 1.4f + new Color(0.06f, 0.06f, 0.07f), apron * amt * 0.5f);
                }

                // ── Impact ejecta speckle: fine bright fresh-crater blankets across the bombarded old terrane. ──
                if (pp.bombardment > 0.2f)
                {
                    float ej = Astrophysics.Smoothstep(0.62f, 0.95f, Fbm(dir * 9f + pp.o1, 4)) * pp.bombardment * old;
                    land = Color.Lerp(land, land * 1.35f + new Color(0.05f, 0.05f, 0.06f), ej * 0.5f);
                }

                // ── Wind streaks: on a DRY world WITH an atmosphere, wind smears long east-west albedo streaks
                // (Mars-like), finer and sharper on fast rotators — an atmosphere→surface signature. ──
                if (pp.hasAtmo && pp.waterCov < 0.15f && !cp.hasLiquid && !icyTheme)
                {
                    Vector3 ws = new Vector3(dir.x, dir.y * (3f + 5f * fast), dir.z);   // thin in latitude → E–W streaks
                    float s2 = Fbm(ws * 3f + pp.o4, 4) - 0.5f;
                    land = Color.Lerp(land, land * (1f + Mathf.Clamp(s2 * 0.7f, -0.22f, 0.22f)), 0.6f);
                }

                float snow = Astrophysics.Smoothstep(pp.snowStart - 0.06f, pp.snowStart, e);
                col = Color.Lerp(land, cp.ice, snow);
            }

            // Polar caps: a ragged, terrain-following boundary (not a hard latitude line). The cap reaches
            // lower on highlands and where a noise field pushes it, and fades in over a wide band.
            // Sulfuric/methanic/saline worlds don't grow water-ice caps; others do.
            float capAmt = (cp.theme == ChemTheme.Sulfuric || cp.theme == ChemTheme.Lava
                         || cp.theme == ChemTheme.Halide || cp.theme == ChemTheme.Evaporite
                         || cp.theme == ChemTheme.Metallic || cp.theme == ChemTheme.Corundum) ? 0f : 1f;
            float capNoise = (Fbm(dir * 3.5f + pp.o2, 4) - 0.5f) * 0.22f;
            float ice;
            if (pp.locked)
            {
                // Eyeball night-side ice cap: freezes where the substellar heat drops (far from the day side).
                float heat = Mathf.Clamp01(dir.z * 0.5f + 0.5f);
                float iceEdge = 0.46f + capNoise - 0.12f * h;
                ice = Astrophysics.Smoothstep(iceEdge + 0.10f, iceEdge - 0.08f, heat) * capAmt;
            }
            else
            {
                float capEdge = pp.capLat + capNoise - 0.2f * h;
                ice = Astrophysics.Smoothstep(capEdge - 0.13f, capEdge + 0.09f, latAbs) * capAmt;
            }
            return Color.Lerp(col, cp.ice, ice);
        }

        // colour ramps
        static Color L(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));
        // Rock worlds come in several palettes so they're not all Mars-red: rust, grey basalt, tan desert,
        // dark carbonaceous, and pale ochre/sulfur. Each is a low→high elevation ramp.
        static Color RampRock(float e, int style)
        {
            switch (style)
            {
                case 1:  // grey basalt / lunar
                    return e < 0.5f ? L(new Color(0.16f, 0.16f, 0.17f), new Color(0.4f, 0.4f, 0.42f), e / 0.5f)
                                    : L(new Color(0.4f, 0.4f, 0.42f), new Color(0.66f, 0.66f, 0.68f), (e - 0.5f) / 0.5f);
                case 2:  // tan desert
                    return e < 0.5f ? L(new Color(0.45f, 0.34f, 0.2f), new Color(0.72f, 0.58f, 0.38f), e / 0.5f)
                                    : L(new Color(0.72f, 0.58f, 0.38f), new Color(0.9f, 0.82f, 0.65f), (e - 0.5f) / 0.5f);
                case 3:  // dark carbonaceous (kept dark, but not near-black, so it reads when lit)
                    return e < 0.5f ? L(new Color(0.17f, 0.16f, 0.17f), new Color(0.32f, 0.30f, 0.30f), e / 0.5f)
                                    : L(new Color(0.32f, 0.30f, 0.30f), new Color(0.5f, 0.47f, 0.44f), (e - 0.5f) / 0.5f);
                case 4:  // pale ochre / sulfur
                    return e < 0.5f ? L(new Color(0.5f, 0.42f, 0.2f), new Color(0.78f, 0.7f, 0.42f), e / 0.5f)
                                    : L(new Color(0.78f, 0.7f, 0.42f), new Color(0.92f, 0.88f, 0.68f), (e - 0.5f) / 0.5f);
                default: // rust / Mars-red
                    return e < 0.4f ? L(new Color(0.29f, 0.14f, 0.1f), new Color(0.55f, 0.28f, 0.18f), e / 0.4f)
                         : e < 0.75f ? L(new Color(0.55f, 0.28f, 0.18f), new Color(0.74f, 0.48f, 0.31f), (e - 0.4f) / 0.35f)
                         : L(new Color(0.74f, 0.48f, 0.31f), new Color(0.88f, 0.74f, 0.58f), (e - 0.75f) / 0.25f);
            }
        }
        static Color RampFrozen(float e) =>
            e < 0.5f ? L(new Color(0.40f, 0.46f, 0.54f), new Color(0.62f, 0.69f, 0.76f), e / 0.5f)
          : L(new Color(0.62f, 0.69f, 0.76f), new Color(0.85f, 0.90f, 0.95f), (e - 0.5f) / 0.5f);
        static Color RampGas(float t) =>
            t < 0.33f ? L(new Color(0.34f, 0.22f, 0.14f), new Color(0.62f, 0.42f, 0.26f), t / 0.33f)       // dark belt
          : t < 0.66f ? L(new Color(0.62f, 0.42f, 0.26f), new Color(0.85f, 0.7f, 0.5f), (t - 0.33f) / 0.33f) // tan zone
          : L(new Color(0.85f, 0.7f, 0.5f), new Color(0.97f, 0.93f, 0.86f), (t - 0.66f) / 0.34f);            // bright zone
        static Color RampIce(float t) => L(new Color(0.29f, 0.51f, 0.66f), new Color(0.74f, 0.86f, 0.91f), t);

        static Color WaterDeep(WaterChemistry c) => c switch
        {
            WaterChemistry.Iron => new Color(0.08f, 0.22f, 0.15f),
            WaterChemistry.Sulfide => new Color(0.38f, 0.37f, 0.19f),
            WaterChemistry.Phosphate => new Color(0.06f, 0.33f, 0.36f),
            _ => new Color(0.05f, 0.18f, 0.36f),
        };
        static Color WaterShallow(WaterChemistry c) => c switch
        {
            WaterChemistry.Iron => new Color(0.16f, 0.40f, 0.28f),
            WaterChemistry.Sulfide => new Color(0.70f, 0.66f, 0.40f),
            WaterChemistry.Phosphate => new Color(0.16f, 0.66f, 0.62f),
            _ => new Color(0.14f, 0.45f, 0.64f),
        };

        // ── public surface descriptors (for the viewer's ocean / atmosphere shells) ─────────────────
        // A reflective liquid shell is only made for water-like liquids (not lava); needs the chem theme.
        public static bool HasOcean(PlanetData p, ulong seed)
            => PlanetData.IsRocky(p.type) && p.waterCoverage > 0.02f && Chem(p, seed).liquidShell;

        public static Color OceanDeep(PlanetData p, ulong seed) => Chem(p, seed).oceanDeep;
        public static Color OceanShallow(PlanetData p, ulong seed) => Chem(p, seed).oceanShallow;
        // Sea level in the baked height channel (water texels store A = seaLevel, land stores A > seaLevel) — lets
        // the ocean shell clip itself to real basins instead of z-fighting the displaced coastline.
        public static float SeaLevel(PlanetData p, ulong seed) => MakeParams(p, seed).seaLevel;

        // ── Surface sampler: the SAME fields that paint the planet from orbit, exposed to the on-foot surface scene so
        // the ground you walk on is the ground you saw from space. Pure math, thread-safe (terrain builds off-thread).
        public sealed class SurfaceSampler
        {
            readonly PP pp;
            internal SurfaceSampler(PP p) { pp = p; }
            public float SeaLevel => pp.seaLevel;
            public float Relief => pp.relief;
            public float MountainAmp => pp.mtnAmp;
            public bool HasLiquid => pp.cp.hasLiquid && pp.waterCov > 0.02f;
            public ChemPalette Palette => pp.cp;
            /// 0–1 elevation at a unit direction (sea level at <see cref="SeaLevel"/>).
            public float Height01(Vector3 dir) => Height(dir, pp);
            /// The orbital albedo at a direction (slope 0–1 drives basalt/clay tinting).
            public Color Albedo(Vector3 dir, float h01, float slope) => Surface(pp, dir, h01, slope);
            /// The same moisture field the orbital biosphere colouring uses (0–1).
            public float Moisture(Vector3 dir) => Fbm(dir * pp.moistFreq + pp.o2, 4);
            public static float Noise(Vector3 p, int oct) => Fbm(p, oct);
            public static float RidgedNoise(Vector3 p, int oct) => Ridged(p, oct);
            public static void WorleyNoise(Vector3 p, out float f1, out float f2, out float cellRand) => Worley(p, out f1, out f2, out cellRand);
            public float Volcanism => pp.volcanism;
            public float Mineral => pp.mineral;
            public float WaterCov => pp.waterCov;
            public float CraterAmp => pp.craterAmp;
            public bool HasAtmo => pp.hasAtmo;
        }
        public static SurfaceSampler Sampler(PlanetData p, ulong seed) => new SurfaceSampler(MakeParams(p, seed));

        // Atmosphere retention: a rocky world keeps an atmosphere only if its gravity (∝ mass) can hold gas against
        // thermal escape (which rises with temperature) AND it has volatiles to outgas. Small and/or hot worlds are
        // airless. So many planets have NO atmosphere — only sufficiently massive ones do.
        // A display name that matches what's actually RENDERED (the archetype), not the raw generation PlanetType —
        // so a frozen ocean-type world reads "Ice World", not "Ocean".
        public static string DisplayType(PlanetData p, ulong seed)
        {
            if (p.type == PlanetType.GasGiant || p.type == PlanetType.IceGiant)
                return GiantSubtype(p, seed) switch
                {
                    GiantType.HotJupiter => "Hot Jupiter",
                    GiantType.SubNeptune => "Sub-Neptune",
                    GiantType.HeliumGiant => "Helium Giant",
                    GiantType.Superstorm => "Superstorm Giant",
                    GiantType.IceGiant => "Ice Giant",
                    _ => "Gas Giant",
                };
            string s = Chem(p, seed).theme switch
            {
                ChemTheme.Biosphere => "Living World",
                ChemTheme.Pelagic => "Ocean World",
                ChemTheme.Snowball => "Ice World",
                ChemTheme.SalineIce => "Ice World · subsurface ocean",
                ChemTheme.Lava => "Lava World",
                ChemTheme.Sulfuric => "Sulfur World",
                ChemTheme.Basaltic => "Basaltic World",
                ChemTheme.Carbonaceous => "Carbon World",
                ChemTheme.Cupric => "Copper World",
                ChemTheme.Halide => "Salt-Crust World",
                ChemTheme.Evaporite => "Salt-Flat World",
                ChemTheme.Tholin => "Tholin World",
                ChemTheme.Methanic => "Methane World",
                ChemTheme.AmmoniaIce => "Ammonia-Ice World",
                ChemTheme.Ferrous => "Iron-Oxide World",
                ChemTheme.Metallic => "Metallic World",
                ChemTheme.Corundum => "Corundum World",
                _ => "Rocky World",
            };
            if (p.tidallyLocked) return s + " · tidally locked";
            // Rotational character (only when notable) so the spin class reads in the header.
            if (p.axialTiltDeg > 90f) s += " · retrograde";
            else if (p.axialTiltDeg > 45f) s += " · high obliquity";
            if (p.rotationHours < 6f) s += " · fast rotator";
            else if (p.rotationHours > 250f) s += " · slow rotator";
            return s;
        }

        public static bool HasAtmosphere(PlanetData p)
        {
            if (p.type == PlanetType.GasGiant || p.type == PlanetType.IceGiant) return true;
            if (!PlanetData.IsRocky(p.type)) return false;
            // Escape vs thermal escape: the mass needed to hold gas rises steeply with temperature. Cold worlds
            // retain air at very low mass (Titan, 0.022 M⊕ at −179 °C, keeps a thick N₂ atmosphere); warm worlds
            // need Earth-ish mass; hot ones need a super-Earth. Exponential in temperature (calibrated to Titan,
            // Mars, Earth, Mercury) so small COLD moons can be atmospheric while small hot rocks stay airless.
            float minMass = Mathf.Clamp(0.02f * Mathf.Exp((p.meanTempC + 180f) / 95f), 0.015f, 3f);
            bool retains = p.mass >= minMass;
            bool volatiles = p.waterCoverage > 0.05f || p.habClass == HabClass.Habitable
                           || p.type == PlanetType.Ocean || p.volcanism > 0.5f || p.meanTempC > 60f;
            return retains && volatiles;
        }

        // Optical thickness of the atmosphere (0 thin haze … ~1.5 thick/opaque) — more massive & hotter (runaway
        // greenhouse) → thicker.
        public static float AtmosphereDensity(PlanetData p)
        {
            if (p.type == PlanetType.GasGiant || p.type == PlanetType.IceGiant) return 1f;
            float d = 0.25f + 0.7f * Mathf.Clamp01((p.mass - 0.35f) / 3f);
            if (p.meanTempC > 120f) d += 0.6f;                          // Venusian runaway → thick
            if (p.meanTempC < -80f) d += 0.15f;                         // cold, retained
            return Mathf.Clamp(d, 0.12f, 1.6f);
        }

        // Atmosphere colour by COMPOSITION (from temperature/chemistry/biology), not a single tint:
        // hot Venusian pale, cold methane orange (Titan), living worlds blue (Rayleigh), sulfurous volcanic yellow.
        public static Color AtmosphereColor(PlanetData p, ulong seed)
        {
            switch (p.type)
            {
                case PlanetType.GasGiant: return new Color(0.85f, 0.72f, 0.55f);
                case PlanetType.IceGiant: return new Color(0.45f, 0.68f, 0.95f);
            }
            float t = p.meanTempC;
            var rng = new DetRng(DetRng.Hash(seed, 0xA7307u));
            if (t > 200f)  return new Color(0.92f, 0.87f, 0.72f) * rng.Range(0.9f, 1.05f);   // hot CO₂ / sulfuric haze
            if (t < -90f)  return new Color(0.86f, 0.55f, 0.30f);                            // methane/tholin (Titan)
            if (p.habClass == HabClass.Habitable) return new Color(0.45f, 0.62f, 1.0f);      // N₂/O₂ Rayleigh blue
            if (p.volcanism > 0.6f) return new Color(0.82f, 0.78f, 0.40f);                   // sulfur/SO₂ yellow
            if (p.waterCoverage > 0.4f) return new Color(0.55f, 0.68f, 0.85f);               // damp, hazy blue-grey
            return Chem(p, seed).atm;                                                        // chemistry-tinted default
        }

        // ── mesh ──────────────────────────────────────────────────────────────────────────────────
        // Gently displaced sphere: land rises above sea level (so it pokes through the ocean shell), basins
        // dip below it. Colour/relief are handled per-pixel by the baked texture, so the mesh only needs
        // enough subdivision for a smooth silhouette — not for surface detail.
        public static Mesh BuildMesh(PlanetData planet, ulong seed, int subdivisions)
        {
            Icosphere.Build(subdivisions, out var dirs, out var tris);
            var pp = MakeParams(planet, seed);
            bool giant = pp.type == PlanetType.GasGiant || pp.type == PlanetType.IceGiant;
            bool hasSea = !giant && Chem(planet, seed).hasLiquid;

            var verts = new Vector3[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector3 d = dirs[i];
                float h = Height(d, pp);
                if (hasSea && h < pp.seaLevel) h = pp.seaLevel;   // flat sea surface (no bumpy seabed under the ocean)
                float disp = giant ? 0f : pp.relief * (h - pp.seaLevel);
                verts[i] = d * (0.5f + 0.5f * disp);   // 0.5 = Unity primitive-sphere radius convention
            }

            var m = new Mesh { name = $"Planet_{planet.name}_L{subdivisions}" };
            if (dirs.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Result of the CPU-side (thread-safe) half of a bake: pixels ready to upload.</summary>
        public struct SurfacePixels { public Color[] px; public int width, height; }

        /// <summary>
        /// Computes the equirectangular surface pixels (RGB = albedo, A = height). Pure math — safe to run on
        /// a background thread so the hi-res bake never freezes the main thread. Upload via <see cref="ToTexture"/>.
        /// </summary>
        public static SurfacePixels ComputeSurface(PlanetData planet, ulong seed, int width)
        {
            int h = width / 2;
            var pp = MakeParams(planet, seed);

            // Pass 1: heights (also used for the slope estimate that drives basalt/clay colouring).
            var hgt = new float[width * h];
            var dirs = new Vector3[width * h];
            for (int y = 0; y < h; y++)
            {
                float lat = Mathf.PI * 0.5f - (y + 0.5f) / h * Mathf.PI;         // +90°→-90°
                float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                for (int x = 0; x < width; x++)
                {
                    float lon = (x + 0.5f) / width * Mathf.PI * 2f - Mathf.PI;
                    Vector3 d = new Vector3(cl * Mathf.Cos(lon), sl, cl * Mathf.Sin(lon));
                    int idx = y * width + x;
                    dirs[idx] = d;
                    hgt[idx] = Height(d, pp);
                }
            }

            // Pass 2: colour from height + neighbour slope. Water gets a FLAT height (A = seaLevel) so the
            // per-pixel bump ignores the seabed — oceans read smooth, not lumpy.
            var px = new Color[width * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int xl = (x - 1 + width) % width, xr = (x + 1) % width;      // wrap longitude
                int yd = Mathf.Max(0, y - 1), yu = Mathf.Min(h - 1, y + 1);  // clamp latitude
                float gLon = (hgt[y * width + xr] - hgt[y * width + xl]) * 0.5f;
                float gLat = (hgt[yu * width + x] - hgt[yd * width + x]) * 0.5f;
                float slope = Mathf.Clamp01(Mathf.Sqrt(gLon * gLon + gLat * gLat) / 0.006f * 0.6f);

                Color c = Surface(pp, dirs[idx], hgt[idx], slope);
                bool water = pp.waterCov > 0.02f && hgt[idx] < pp.seaLevel;
                px[idx] = new Color(c.r, c.g, c.b, water ? pp.seaLevel : hgt[idx]);
            }
            return new SurfacePixels { px = px, width = width, height = h };
        }

        /// <summary>Uploads computed pixels to a texture (main thread only).</summary>
        public static Texture2D ToTexture(SurfacePixels s, string name)
        {
            var tex = new Texture2D(s.width, s.height, TextureFormat.RGBA32, true)
            { name = name, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels(s.px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Synchronous convenience bake (compute + upload on the calling thread).</summary>
        public static Texture2D BakeSurface(PlanetData planet, ulong seed, int width)
            => ToTexture(ComputeSurface(planet, seed, width), $"PlanetTex_{planet.name}");
    }
}
