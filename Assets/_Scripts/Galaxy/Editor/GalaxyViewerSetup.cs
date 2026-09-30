using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CLAY.Galaxy.EditorTools
{
    /// <summary>Creates the empty-space scene with a SystemViewer on it, ready to Play.</summary>
    public static class GalaxyViewerSetup
    {
        const string ScenePath = "Assets/Scenes/GalaxyViewer.unity";

        [MenuItem("CLAY/Create Galaxy Viewer Scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("SystemViewer");
            go.AddComponent<SystemViewer>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = go;
            Debug.Log($"[Galaxy] Created {ScenePath}. Press Play to view — 'New system' rolls a fresh one.");
        }
    }
}
