using UnityEngine;
using CLAY.Galaxy;

namespace CLAY.Surface
{
    /// <summary>
    /// Weather on the planet surface. Each world has a climate-set TENDENCY (humidity, pressure, wind, volatiles) and a
    /// seeded, slowly evolving STATE on top of it (fronts roll through over minutes):
    ///  • cloud deck — coverage, density, base altitude and thickness → raymarched in the sky shader, with matching
    ///    ground shadows; drifts with the wind
    ///  • precipitation — rain, snow (below freezing), methane rain (Titan-like), volcanic ash — as particles around the
    ///    camera, falling at a speed set by the planet's gravity
    ///  • dust storms on dry, windy worlds with air — the air fills with the colour of the ground
    ///  • consequences: overcast dims and flattens the sun, rain soaks the ground (and it dries slowly after), snow
    ///    settles on upward-facing surfaces, fog thickens in storms
    /// </summary>
    public sealed class SurfaceWeather
    {
        public enum Precip { None, Rain, Snow, MethaneRain, Ash }

        readonly SurfaceGeo geo;
        readonly PlanetData planet;
        readonly float seedT, gravity, humidity, windBase;
        readonly bool air, volatiles, methane, dustWorld, ashWorld;
        readonly Vector2 windDir;
        Vector2 drift;
        float time, rainWet, snowCover;
        float groundUnder, groundSmooth = float.NaN;
        float windGust;                                                          // storm-ocean gales push particles harder
        public void SetGround(float h) { groundUnder = h; }
        ParticleSystem ps; ParticleSystemRenderer psr; Material pmat; Texture2D dot;
        Precip shownKind = (Precip)(-1);

        // current state (read by SurfaceWorld)
        public float Cover { get; private set; }
        public float PrecipAmt { get; private set; }
        public Precip Kind { get; private set; }
        public float Dust { get; private set; }
        public float SunFactor { get; private set; } = 1f;       // multiplies direct sunlight
        public float FogFactor { get; private set; } = 1f;       // multiplies fog density
        public Color DustColor { get; private set; }

        public SurfaceWeather(SurfaceGeo g, ulong seed, int layer)
        {
            geo = g; planet = g.planet;
            var cl = g.climate;
            air = cl.hasAtmosphere && cl.pressureBar > 0.01f;
            var r = new DetRng(DetRng.Hash(seed, 0x3EA7UL));
            seedT = r.Range(0f, 1000f);
            gravity = Mathf.Max(cl.gravity, 0.05f) * 9.81f;
            humidity = Mathf.Clamp01(cl.humidity);
            windBase = cl.windLoad;
            float wa = r.Range(0f, Mathf.PI * 2f);
            windDir = new Vector2(Mathf.Cos(wa), Mathf.Sin(wa));
            var theme = g.s.Palette.theme;
            methane = theme == PlanetTexture.ChemTheme.Methanic || theme == PlanetTexture.ChemTheme.Tholin;
            volatiles = g.s.WaterCov > 0.02f || methane || humidity > 0.25f;
            dustWorld = air && g.s.WaterCov < 0.08f && !methane;
            ashWorld = air && g.s.Volcanism > 0.8f;
            Color land = g.s.Palette.landMid;
            DustColor = Color.Lerp(land, new Color(0.7f, 0.6f, 0.5f), 0.3f);
            BuildParticles(layer);
        }

        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        float Wave(float period, float off) => Mathf.PerlinNoise((time + seedT * 37f) / period, off);   // 0..1, slow

        public void Update(float dt, Vector3 camPos, in LocalClimate here, float sunElev)
        {
            time += dt;
            if (!air) { Apply(0f, 0f, Precip.None, 0f, camPos); return; }

            // cloudiness: the climate's tendency, with fronts drifting through (several-minute periods)
            float tendency = Mathf.Clamp01(humidity * 1.15f + Mathf.Clamp01(geo.climate.pressureBar - 1f) * 0.15f - 0.05f);
            var cat = geo.category;
            bool deck = cat == PlanetCategory.VenusGreenhouse || cat == PlanetCategory.SulfuricCloud || cat == PlanetCategory.WaterVapor
                     || cat == PlanetCategory.PhotochemicalSmog || cat == PlanetCategory.Hycean || cat == PlanetCategory.Tholin;
            if (deck) tendency = 1f;                                             // a permanent, unbroken deck
            if (cat == PlanetCategory.StormOcean) tendency = Mathf.Max(tendency, 0.8f);
            float front = Wave(260f, 0.3f) * 0.7f + Wave(90f, 5.1f) * 0.3f;
            float cover = Mathf.Clamp01(tendency * 0.9f + (front - 0.5f) * 0.9f);
            if (methane) cover = Mathf.Max(cover, 0.35f);                          // perpetual photochemical haze decks
            if (deck) cover = Mathf.Max(cover, 0.95f);

            // precipitation: only from thick cloud, only if there's something to condense
            float precip = volatiles ? SS(0.62f, 0.9f, cover) * Mathf.Lerp(0.5f, 1f, humidity) : 0f;
            Precip kind = Precip.None;
            if (precip > 0.02f)
                kind = methane ? Precip.MethaneRain : here.tMeanC < 0.5f ? Precip.Snow : Precip.Rain;
            if (ashWorld && Wave(140f, 9.7f) > 0.72f) { kind = Precip.Ash; precip = Mathf.Max(precip, SS(0.72f, 0.85f, Wave(140f, 9.7f))); }

            // dust storms: dry, windy worlds; they come in bursts
            float dust = dustWorld ? SS(0.62f, 0.82f, Wave(180f, 13.3f)) * Mathf.Clamp01(windBase * 0.6f + 0.2f) : 0f;
            if (cat == PlanetCategory.Desert || cat == PlanetCategory.OchreIron || cat == PlanetCategory.RegolithDust)
                dust = Mathf.Max(dust, air ? SS(0.5f, 0.75f, Wave(150f, 21.7f)) * 0.8f : 0f);            // seasonal dust storms
            if (cat == PlanetCategory.StormOcean) precip = Mathf.Max(precip, SS(0.4f, 0.7f, front) * 0.9f);  // squall lines
            if (precip > 0.02f && kind == Precip.None) kind = methane ? Precip.MethaneRain : here.tMeanC < 0.5f ? Precip.Snow : Precip.Rain;
            Apply(cover * (1f - dust * 0.6f), precip, kind, dust, camPos);
        }

        void Apply(float cover, float precip, Precip kind, float dust, Vector3 camPos)
        {
            Cover = cover; PrecipAmt = precip; Kind = kind; Dust = dust;
            // cloud deck: thicker and lower when it's raining; base altitude from the planet's warmth & pressure
            // cloud base is a height ABOVE THE GROUND (convective lifting level), not above sea level — otherwise high
            // plateaus sit inside or above the deck. Follows the terrain under the camera, smoothed.
            float agl = Mathf.Lerp(2200f, 900f, precip) * Mathf.Clamp(1f / Mathf.Max(geo.climate.pressureBar, 0.2f), 0.6f, 2.5f);
            groundSmooth = float.IsNaN(groundSmooth) ? groundUnder : Mathf.Lerp(groundSmooth, groundUnder, Time.unscaledDeltaTime * 0.2f);
            float baseAlt = Mathf.Max(groundSmooth, 0f) + agl;
            float thick = Mathf.Lerp(900f, 3500f, Mathf.Max(precip, cover * cover));
            Shader.SetGlobalVector("_CloudShape", new Vector4(cover, 0.55f + precip * 0.8f, baseAlt, thick));
            float windSpd = Mathf.Clamp(4f + windBase * 6f, 2f, 45f);
            drift += windDir * windSpd * Time.unscaledDeltaTime;
            Shader.SetGlobalVector("_CloudOffset", new Vector4(drift.x, drift.y, Mathf.Lerp(5000f, 2600f, cover), 0.6f));

            SunFactor = Mathf.Lerp(1f, 0.25f, cover * cover * 0.9f + precip * 0.2f) * (1f - dust * 0.7f);
            FogFactor = 1f + precip * 5f + dust * 30f;
            // category atmospheres: a Venus surface is a dim orange twilight under 90 bar; steam worlds are fog banks
            switch (geo.category)
            {
                case PlanetCategory.VenusGreenhouse: SunFactor *= 0.18f; FogFactor *= 6f; break;
                case PlanetCategory.SulfuricCloud: case PlanetCategory.PhotochemicalSmog: case PlanetCategory.Tholin: SunFactor *= 0.45f; FogFactor *= 3f; break;
                case PlanetCategory.WaterVapor: SunFactor *= 0.35f; FogFactor *= 10f; break;
                case PlanetCategory.Hycean: FogFactor *= 2f; break;
                case PlanetCategory.StormOcean: windGust = 1f; break;
            }

            // the ground soaks up rain quickly and dries slowly; snow settles and melts slowly
            float dt = Time.unscaledDeltaTime;
            bool wetting = kind == Precip.Rain || kind == Precip.MethaneRain;
            rainWet = Mathf.MoveTowards(rainWet, wetting ? Mathf.Clamp01(precip * 1.5f) : 0f, dt * (wetting ? 0.08f : 0.012f));
            snowCover = Mathf.MoveTowards(snowCover, kind == Precip.Snow ? Mathf.Clamp01(precip * 1.3f) : 0f, dt * (kind == Precip.Snow ? 0.02f : 0.006f));
            Shader.SetGlobalFloat("_RainWet", rainWet);
            Shader.SetGlobalFloat("_SnowCover", snowCover);

            UpdateParticles(kind, precip, dust, camPos);
        }

        // ── precipitation particles (a box of falling drops / flakes / grit that follows the camera) ──
        void BuildParticles(int layer)
        {
            var go = new GameObject("Precipitation") { layer = layer };
            ps = go.AddComponent<ParticleSystem>();
            psr = go.GetComponent<ParticleSystemRenderer>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 12000; main.startLifetime = 3f; main.startSpeed = 0f; main.gravityModifier = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40f, 1f, 40f);
            shape.position = new Vector3(0f, 14f, 0f);
            var em = ps.emission; em.rateOverTime = 0f;
            // soft round sprite
            dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d); a *= a;
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            dot.Apply();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            pmat = new Material(sh);
            pmat.SetTexture("_BaseMap", dot);
            pmat.SetFloat("_Surface", 1f); pmat.SetFloat("_Blend", 0f);     // transparent, alpha blend
            pmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            pmat.SetOverrideTag("RenderType", "Transparent");
            pmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pmat.SetInt("_ZWrite", 0);
            pmat.renderQueue = 3000;
            psr.sharedMaterial = pmat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; psr.receiveShadows = false;
        }

        void UpdateParticles(Precip kind, float precip, float dust, Vector3 camPos)
        {
            if (ps == null) return;
            ps.transform.position = camPos;
            bool active = precip > 0.02f || dust > 0.45f;                           // wind itself is invisible; only a real storm shows grit
            var em = ps.emission;
            if (!active) { em.rateOverTime = 0f; return; }
            Precip k = dust > precip ? Precip.None : kind;   // None here = dust
            if (k != shownKind) ConfigureKind(k);
            float rate = dust > precip ? 900f * (dust - 0.45f) : k switch
            {
                Precip.Snow => 2600f * precip, Precip.Ash => 2000f * precip, _ => 7000f * precip,
            };
            em.rateOverTime = rate;
            // wind drives everything sideways a little (dust a lot)
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            float side = dust > precip ? 14f : k == Precip.Snow || k == Precip.Ash ? 1.6f : 2.5f;
            float wind = Mathf.Clamp(windBase, 0.1f, 3f) * side * (1f + windGust * 2f);
            vel.x = windDir.x * wind; vel.z = windDir.y * wind;
            if (!ps.isPlaying) ps.Play();
        }

        void ConfigureKind(Precip k)
        {
            shownKind = k;
            var main = ps.main;
            float g = Mathf.Sqrt(gravity / 9.81f);                                  // terminal velocity ∝ √g
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            switch (k)
            {
                case Precip.Rain:
                case Precip.MethaneRain:
                {
                    // methane drops on a Titan-like world are big and fall slowly (low g, dense air): ~1.6 m/s vs ~7 m/s
                    float fall = (k == Precip.MethaneRain ? 1.6f : 7f) * g;
                    vel.y = -fall;
                    main.startLifetime = 16f / fall;
                    main.startSize = k == Precip.MethaneRain ? 0.035f : 0.018f;
                    main.startColor = k == Precip.MethaneRain ? new Color(0.95f, 0.75f, 0.45f, 0.5f) : new Color(0.75f, 0.8f, 0.9f, 0.35f);
                    psr.renderMode = ParticleSystemRenderMode.Stretch; psr.velocityScale = 0.06f; psr.lengthScale = 1f;
                    break;
                }
                case Precip.Snow:
                case Precip.Ash:
                {
                    float fall = (k == Precip.Snow ? 1.0f : 1.6f) * g;
                    vel.y = -fall;
                    main.startLifetime = 16f / fall;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                    main.startColor = k == Precip.Snow ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.35f, 0.33f, 0.32f, 0.8f);
                    psr.renderMode = ParticleSystemRenderMode.Billboard;
                    var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f;   // flakes flutter
                    break;
                }
                default:   // dust storm: fine grit driven sideways
                {
                    vel.y = -0.4f;
                    main.startLifetime = 3.5f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
                    main.startColor = new Color(DustColor.r, DustColor.g, DustColor.b, 0.25f);
                    psr.renderMode = ParticleSystemRenderMode.Billboard;           // soft motes, not streaks
                    var noise = ps.noise; noise.enabled = true; noise.strength = 1.5f; noise.frequency = 0.3f;
                    break;
                }
            }
            if (k == Precip.Rain || k == Precip.MethaneRain) { var noise = ps.noise; noise.enabled = false; }
        }

        public string Describe()
        {
            if (!air) return "no weather (airless)";
            string sky = Cover < 0.15f ? "clear" : Cover < 0.45f ? "scattered clouds" : Cover < 0.75f ? "cloudy" : "overcast";
            string p = Dust > 0.1f && Dust > PrecipAmt ? $"dust storm ({Dust * 100f:0}%)"
                     : PrecipAmt > 0.02f ? $"{Kind switch { Precip.Rain => "rain", Precip.Snow => "snow", Precip.MethaneRain => "methane rain", Precip.Ash => "ash fall", _ => "" }} ({PrecipAmt * 100f:0}%)"
                     : "dry";
            return $"{sky}, {p}";
        }

        public void Dispose()
        {
            Shader.SetGlobalVector("_CloudShape", Vector4.zero);
            Shader.SetGlobalFloat("_RainWet", 0f); Shader.SetGlobalFloat("_SnowCover", 0f);
            if (ps) Object.Destroy(ps.gameObject);
            if (pmat) Object.Destroy(pmat);
            if (dot) Object.Destroy(dot);
        }
    }
}
