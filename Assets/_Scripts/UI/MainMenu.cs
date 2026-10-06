using UnityEngine;
using CLAY.GalaxyMap;

namespace CLAY.UI
{
    /// <summary>
    /// The default start screen: ENTER GALAXY or PLANET EDITOR (or quit). Created automatically when a scene with a
    /// GalaxyBootstrap loads, so no scene edits are needed. The galaxy renders behind the menu as a backdrop; its fly
    /// camera and star picking are paused while the menu (or the planet editor) is open. Escape in free galaxy flight
    /// returns here.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        static MainMenu _inst;
        public static bool Showing => _inst != null && _inst.showing;
        /// True while the menu or the planet editor owns the screen — other galaxy input must stand down.
        public static bool Blocking => Showing || PlanetEditor.Active;

        GalaxyBootstrap galaxy;
        GalaxyCameraController fly;
        StarSystemEntry entry;
        bool showing = true;
        float t;
        GUIStyle titleS, subS, btnS, hintS;
        Texture2D dim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_inst != null || Object.FindObjectOfType<GalaxyBootstrap>() == null) return;
            var go = new GameObject("~MainMenu");
            _inst = go.AddComponent<MainMenu>();
        }

        void Start()
        {
            galaxy = FindObjectOfType<GalaxyBootstrap>();
            var cam = Camera.main;
            if (cam) { fly = cam.GetComponent<GalaxyCameraController>(); entry = cam.GetComponent<StarSystemEntry>(); }
            if (entry == null) entry = FindObjectOfType<StarSystemEntry>();
            dim = new Texture2D(1, 1); dim.SetPixel(0, 0, new Color(0.01f, 0.012f, 0.03f, 0.55f)); dim.Apply();
            Show();
        }

        public static void Show() { if (_inst) _inst.SetShowing(true); }

        void SetShowing(bool on)
        {
            showing = on; t = 0f;
            if (fly) fly.enabled = !on && !PlanetEditor.Active;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (showing && fly && fly.enabled) fly.enabled = false;
            // Escape in free galaxy flight (not inside a system, not in a sub-screen) → back to the menu
            if (!showing && !PlanetEditor.Active && Input.GetKeyDown(KeyCode.Escape)
                && (entry == null || !entry.InSystem)
                && !CLAY.Surface.SurfaceWorld.Active && !CLAY.Flora.FloraLab.Active && !CLAY.Surface.TerrainLab.Active)
                SetShowing(true);
        }

        void OnGUI()
        {
            if (!showing) return;
            if (titleS == null)
            {
                titleS = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                titleS.normal.textColor = new Color(0.93f, 0.95f, 1f);
                subS = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
                subS.normal.textColor = new Color(0.7f, 0.78f, 0.95f, 0.85f);
                btnS = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
                hintS = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                hintS.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            }
            float a = Mathf.Clamp01(t * 2f);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), dim);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.42f;
            GUI.Label(new Rect(cx - 400, cy - 150, 800, 90), "CLAY", titleS);
            GUI.Label(new Rect(cx - 400, cy - 70, 800, 30), "from a single cell to the stars", subS);

            float bw = 300, bh = 52, y = cy - 10;
            if (GUI.Button(new Rect(cx - bw / 2, y, bw, bh), "Enter Galaxy", btnS)) SetShowing(false);
            y += bh + 14;
            if (GUI.Button(new Rect(cx - bw / 2, y, bw, bh), "Planet Editor", btnS))
            {
                SetShowing(false);
                PlanetEditor.Open(galaxy);
            }
            y += bh + 14;
            if (GUI.Button(new Rect(cx - bw / 2, y, bw, bh), "Cell Stage (sandbox)", btnS))
            {
                // dev shortcut: a neutral Earth-like tide pool; returns to this menu on Esc
                SetShowing(false);
                CLAY.CellStage.CellStageWorld.Enter(CLAY.CellStage.CellStageContext.Default((ulong)Random.Range(1, int.MaxValue)), () => SetShowing(true));
            }
            y += bh + 14;
            if (GUI.Button(new Rect(cx - bw / 2, y, bw, 38), "Quit", btnS))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            GUI.Label(new Rect(cx - 400, Screen.height - 46, 800, 24),
                      "Galaxy: click a star to enter its system · Esc returns here      Planet editor: build any world, L to land", hintS);
            GUI.color = Color.white;
        }

        void OnDestroy() { if (dim) Destroy(dim); if (_inst == this) _inst = null; }
    }
}
