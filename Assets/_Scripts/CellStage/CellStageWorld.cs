using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using CLAY.CellStage.Genetics;
using CLAY.CellStage.Stage1;

namespace CLAY.CellStage
{
    /// <summary>
    /// The cell stage's runtime host. Entered from the planet surface (click a tide pool) or standalone. It builds a
    /// fresh runtime scene with its own orthographic camera on CellLayer, SUSPENDS everything else (deactivates the
    /// other scenes' root objects — only OnDestroy does teardown there, so deactivation is safe) and restores it all
    /// on exit, so leaving drops you back exactly where you stood.
    ///
    /// Owns the lineage's progress, the species registry and the planet ledger, and runs one IStageMode at a time,
    /// swapping when the progress graduates a sub-stage. Esc leaves · F3 dev overlay.
    /// </summary>
    public sealed class CellStageWorld : MonoBehaviour
    {
        public const int CellLayer = 30;

        public static CellStageWorld Current { get; private set; }
        public static bool Active => Current != null;

        public CellStageContext ctx;
        public readonly CellStageProgress progress = new();
        public readonly SpeciesRegistry species = new();
        public readonly PlanetHistoryLedger ledger = new();
        public Camera cam;
        /// A mode sets this while Esc means something local (e.g. leaving the seabed view) instead of leaving the stage.
        public bool captureEscape;

        // ── banners: a big title that fades in and out when something momentous happens ──
        string bannerTitle, bannerSub; float bannerT = -1f;
        const float BannerLen = 6f;
        public void Banner(string title, string sub) { bannerTitle = title; bannerSub = sub; bannerT = 0f; }
        void StageBanner(SubStage s)
        {
            switch (s)
            {
                case SubStage.S1_Replicator: Banner("LIFE", "your lineage carries itself forward — a living metabolism stirs"); break;
                case SubStage.S2_Prokaryote: Banner("LUCA", "membrane, metabolism and memory, joined at last — the first true cell"); break;
                case SubStage.S3_Endosymbiosis: Banner("A NEW HUNGER", "something inside you can now reach out and take"); break;
                case SubStage.S4_Eukaryote: Banner("COMPLEXITY", "a cell within a cell — the great merger"); break;
                case SubStage.S5_Colony: Banner("TOGETHER", "cells begin to stay together"); break;
                case SubStage.Graduated: Banner("BEYOND THE POOL", "a body made of many"); break;
            }
        }
        void DrawBanner()
        {
            if (bannerT < 0f) return;
            bannerT += Time.unscaledDeltaTime;
            if (bannerT > BannerLen) { bannerT = -1f; return; }
            float a = Mathf.Clamp01(bannerT / 1.2f) * Mathf.Clamp01((BannerLen - bannerT) / 1.6f);
            var big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.09f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            var small = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.024f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
            float y = Screen.height * 0.3f;
            var prev = GUI.color;
            // letter-spaced title with a soft shadow
            string spaced = bannerTitle.Length > 14 ? bannerTitle : string.Join(" ", bannerTitle.ToCharArray());
            float tw = big.CalcSize(new GUIContent(spaced)).x;
            if (tw > Screen.width * 0.9f) big.fontSize = Mathf.Max(12, Mathf.FloorToInt(big.fontSize * Screen.width * 0.9f / tw));   // always fits
            GUI.color = new Color(0f, 0f, 0f, a * 0.5f);
            GUI.Label(new Rect(3, y + 3, Screen.width, Screen.height * 0.12f), spaced, big);
            GUI.color = new Color(1f, 0.93f, 0.75f, a);
            GUI.Label(new Rect(0, y, Screen.width, Screen.height * 0.12f), spaced, big);
            GUI.color = new Color(0.95f, 0.95f, 0.92f, a * 0.85f);
            GUI.Label(new Rect(0, y + Screen.height * 0.12f, Screen.width, Screen.height * 0.05f), bannerSub, small);
            GUI.color = prev;
        }
        public Transform root;

        IStageMode mode;
        Scene scene, previousActive;
        readonly List<GameObject> suspended = new();
        Action onExit;
        CursorLockMode prevLock; bool prevVisible;
        bool showDebug;
        int illum;
        GUIStyle dbg;

        /// Enter the cell stage. `onExit` runs after everything is restored.
        public static void Enter(CellStageContext c, Action onExit = null)
        {
            if (Current != null) return;
            var prev = SceneManager.GetActiveScene();
            var sc = SceneManager.CreateScene("CellStage");
            // suspend every other scene
            var sus = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s == sc || !s.isLoaded) continue;
                foreach (var go in s.GetRootGameObjects()) if (go.activeSelf) { go.SetActive(false); sus.Add(go); }
            }
            SceneManager.SetActiveScene(sc);
            var host = new GameObject("CellStageWorld");
            SceneManager.MoveGameObjectToScene(host, sc);
            var w = host.AddComponent<CellStageWorld>();
            w.ctx = c; w.scene = sc; w.previousActive = prev; w.onExit = onExit;
            w.suspended.AddRange(sus);
            Current = w;
            // the WATER ENVIRONMENT is the hand-tuned SampleScene (background, physical caustics, particles, organic
            // matter, currents, distortion): load it underneath, strip its prototype gameplay, borrow its camera
            var op = SceneManager.LoadSceneAsync(EnvironmentScene, LoadSceneMode.Additive);
            if (op == null) { Debug.LogWarning($"[CellStage] couldn't load {EnvironmentScene}; using a bare camera"); w.Build(); return; }
            op.completed += _ => { if (w) w.AdoptEnvironment(); };
        }

        public const string EnvironmentScene = "SampleScene";

        // ── renderer features: some full-screen passes of the 2D renderer don't suit the pool (the DynamicLighting
        //    pass draws Voronoi caustics → a fine grid). Switched off while the cell stage runs, restored on exit.
        //    F7 (dev) cycles: each feature off in turn, to find whichever one is drawing something unwanted. ──
        static readonly string[] FeaturesOff = { "DynamicLighting" };
        readonly List<(ScriptableRendererFeature f, bool was)> featureState = new();
        int featureProbe = -1;

        static List<ScriptableRendererFeature> AllFeatures()
        {
            var res = new List<ScriptableRendererFeature>();
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return res;
            var fi = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fi?.GetValue(asset) is ScriptableRendererData[] list)
                foreach (var data in list) if (data != null) foreach (var f in data.rendererFeatures) if (f != null) res.Add(f);
            return res;
        }

        void ConfigureFeatures()
        {
            foreach (var f in AllFeatures())
            {
                featureState.Add((f, f.isActive));
                foreach (var n in FeaturesOff) if (f.name.Contains(n)) f.SetActive(false);
            }
        }

        void RestoreFeatures()
        {
            foreach (var (f, was) in featureState) if (f) f.SetActive(was);
            featureState.Clear();
        }

        // stopping Play (or any teardown) without Exit() must not leave the project's renderer features switched off
        void OnDestroy() { RestoreFeatures(); if (Current == this) Current = null; }

        void ProbeNextFeature()
        {
            if (featureState.Count == 0) return;
            // restore the configured state, then switch off the next one
            foreach (var (f, was) in featureState)
            {
                bool configuredOn = was; foreach (var n in FeaturesOff) if (f.name.Contains(n)) configuredOn = false;
                f.SetActive(configuredOn);
            }
            featureProbe++;
            if (featureProbe >= featureState.Count) { featureProbe = -1; Debug.Log("[CellStage] F7: all renderer features as configured"); return; }
            var (pf, _) = featureState[featureProbe];
            pf.SetActive(false);
            Debug.Log($"[CellStage] F7: renderer feature OFF → {pf.name}");
        }
        Scene envScene;

        void AdoptEnvironment()
        {
            ConfigureFeatures();
            envScene = SceneManager.GetSceneByName(EnvironmentScene);
            if (envScene.IsValid())
            {
                SceneManager.SetActiveScene(envScene);
                foreach (var go in envScene.GetRootGameObjects())
                {
                    if (go.name == "GameplaySystems") { go.SetActive(false); continue; }
                    // the legacy environment (superseded by WorldBackground/WaterCaustics): its light shafts draw vertical
                    // streaks over the pool and its caustics double up — off in the cell stage
                    if (go.name == "Environment") { go.SetActive(false); continue; }
                    // tiled parallax backdrop layers: some sort ABOVE the pool and read as a scrolling grid — off
                    if (go.name == "ParalaxLayers") { go.SetActive(false); continue; }
                    // its organic-matter particles render STRETCHED by velocity → thin amber streaks in the currents; the pool
                    // has its own suspended matter, so it's off here
                    if (go.name == "OrganicMatter") { go.SetActive(false); continue; }
                    // prototype player/AI cells and the camera's follow script
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        string tn = mb.GetType().Name;
                        if (tn == "CellBiomass" || tn == "JellyMovement") { mb.gameObject.SetActive(false); break; }
                        if (tn == "CameraFollow") mb.enabled = false;
                    }
                    var c = go.GetComponent<Camera>();
                    if (c != null && go.name == "Main Camera") envCam = c;
                    // the screen-space water distortion is tuned for the big prototype cells; protocells are a fraction
                    // of that size, so the same warp reads as blur — calm it down for Stage 0
                    foreach (var wd in go.GetComponentsInChildren<WaterDistortionCamera>(true))
                    { wd.distortionStrength *= 0.75f; wd.chromaticAberration *= 0.65f; wd.maxFlowBoost *= 0.4f; }
                    // caustics: the broad blob pass glares and clashes with the fine web at this scale — mostly drop it,
                    // soften the fine one (these fields are read in Start, which hasn't run yet)
                    foreach (var wc in go.GetComponentsInChildren<WaterCaustics>(true))
                    { wc.broadIntensity = 0f; wc.fineIntensity = 0f; }   // the pool's own physical caustics replace these
                    // post: much less film grain, no depth-of-field blur
                    foreach (var ue in go.GetComponentsInChildren<UnderwaterEnvironment>(true))
                    { ue.grainScale = 0f; ue.depthOfFieldOn = false; }   // URP film grain tiles a small texture (a fine grid) — the pool shader grains instead
                }
            }
            Build();
        }
        Camera envCam;

        void Build()
        {
            Current = this;
            if (root != null) return;
            prevLock = Cursor.lockState; prevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            RenderSettings.fog = false;

            root = new GameObject("CellStageRoot").transform;
            if (envCam != null)
            {
                cam = envCam;
                cam.cullingMask |= 1 << CellLayer;   // the environment's own layers + ours
            }
            else
            {
                var camGo = new GameObject("CellStageCamera") { tag = "MainCamera" };
                camGo.transform.position = new Vector3(0, 0, -10f);
                cam = camGo.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = 8f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
                cam.cullingMask = 1 << CellLayer; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
                var ucd = cam.GetUniversalAdditionalCameraData();
                ucd.renderPostProcessing = false; ucd.renderShadows = false;
            }

            // microscope illumination for this world (CellStage visual identity per planet)
            illum = 3;   // DIC ("emboss") — the cell stage's signature microscope look (F6 cycles bright/dark/phase/DIC)
            Shader.SetGlobalFloat("_CellIllum", illum);

            progress.OnStageChanged += (from, to) => { Debug.Log($"[CellStage] {from} → {to}"); StageBanner(to); SwapMode(to); };
            SwapMode(progress.stage);
        }

        void SwapMode(SubStage s)
        {
            IStageMode next = s switch
            {
                SubStage.S0_Protocell => mode as Stage0.Stage0Replication ?? new Stage0.Stage0Replication(),
                // S1: FounderCollapse in Stage0Replication sets progress.founderGenomes before this fires.
                SubStage.S1_Replicator => mode as Stage0.Stage0Replication ?? (IStageMode)new Stage1Replicator(),   // S1 lives in the same pool as S0
                SubStage.S2_Prokaryote => new Stage2Prokaryote(),
                SubStage.S3_Endosymbiosis => new Stage3Endosymbiosis(),
                SubStage.S4_Eukaryote => new Stage4Eukaryote(),
                SubStage.S5_Colony => new Stage5Colony(),
                SubStage.Virus => mode as Stage0.Stage0Replication ?? (IStageMode)new VirusMode(),
                _ => null,
            };
            if (ReferenceEquals(next, mode)) return;
            mode?.End();
            mode = next;
            mode?.Begin(this, root);
        }

        void Update()
        {
            if (root == null) return;   // still loading the environment
            if (Input.GetKeyDown(KeyCode.Escape) && !captureEscape) { Exit(); return; }
            if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
            if (Input.GetKeyDown(KeyCode.F10) && mode is Stage0.Stage0Replication)
            {
                // DEV: rebuild the pool as the next biome
                Stage0.TidePoolBiome.Forced = (Stage0.TidePoolBiome.Forced + 1) % Stage0.TidePoolBiome.KindCount;
                mode.End();
                mode = new Stage0.Stage0Replication();
                mode.Begin(this, root);
            }
            if (Input.GetKeyDown(KeyCode.F7)) ProbeNextFeature();
            if (Input.GetKeyDown(KeyCode.F8) && cam != null)
            {
                var ucd = cam.GetUniversalAdditionalCameraData();
                ucd.renderPostProcessing = !ucd.renderPostProcessing;
                Debug.Log($"[CellStage] F8: camera post-processing {(ucd.renderPostProcessing ? "ON" : "OFF")}");
            }
            if (Input.GetKeyDown(KeyCode.F9) && root != null)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Soup") { t.gameObject.SetActive(!t.gameObject.activeSelf); Debug.Log($"[CellStage] F9: pool water layer {(t.gameObject.activeSelf ? "ON" : "OFF")}"); }
            }
            if (Input.GetKeyDown(KeyCode.F6)) { illum = (illum + 1) % 4; Shader.SetGlobalFloat("_CellIllum", illum); Debug.Log($"[CellStage] illumination: {(illum == 0 ? "brightfield" : illum == 1 ? "darkfield" : illum == 2 ? "phase contrast" : "DIC (emboss)")}"); }
            try { mode?.Tick(Mathf.Min(Time.deltaTime, 0.05f)); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void OnGUI()
        {
            DrawBanner();
            if (mode is IStageGUI g) g.StageGUI();
            if (mode is StubMode && !showDebug)
            {
                dbg ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 80), $"<b>{progress.stage}</b>\n<i>this sub-stage isn't built yet — Esc to leave</i>", dbg);
                return;
            }
            if (!showDebug) return;
            dbg ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20), GUI.skin.box);
            GUILayout.Label($"<b>CELL STAGE (dev)</b> · {progress.stage} · solvent {ctx.solvent} · {ctx.tempC:0} °C · rate ×{ctx.ReactionRate:0.00}", dbg);
            mode?.DebugGUI();
            GUILayout.EndArea();
        }

        public void Exit()
        {
            mode?.End(); mode = null;
            Current = null;
            RestoreFeatures();
            Cursor.lockState = prevLock; Cursor.visible = prevVisible;
            foreach (var go in suspended) if (go) go.SetActive(true);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            var cb = onExit;
            if (envScene.IsValid() && envScene.isLoaded) SceneManager.UnloadSceneAsync(envScene);
            SceneManager.UnloadSceneAsync(scene);
            cb?.Invoke();
        }
    }
}
