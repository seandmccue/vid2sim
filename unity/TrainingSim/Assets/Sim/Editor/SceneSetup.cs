using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vid2Sim.Editor
{
    /// <summary>
    /// Creates Assets/Scenes/Sim.unity with a camera at seated height and a Bootstrap object. Add the Meta
    /// XR "Camera Rig", "Hand Tracking" and "Hand Grab / Poke Interactor" building blocks after, then set
    /// Bootstrap.rigOrigin to the rig (or leave empty: it uses Camera.main's parent).
    /// </summary>
    public static class SceneSetup
    {
        const string ScenePath = "Assets/Scenes/Sim.unity";

        [MenuItem("Vid2Sim/Create Sim Scene")]
        public static void CreateSimScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            if (cam) { cam.transform.position = new Vector3(0f, 1.2f, 0f); cam.nearClipPlane = 0.05f; }
            var boot = new GameObject("Vid2Sim");
            boot.AddComponent<Bootstrap>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SceneSetup] saved " + ScenePath);
        }
    }
}
