using System.IO;
using GameLogic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BadmintonPhysicsTestSceneBuilder
{
    private const string SceneDirectory = "Assets/Scenes/Test";
    private const string ScenePath = SceneDirectory + "/BadmintonPhysicsTestScene.unity";

    [MenuItem("TEngine/Badminton/Create Or Open Physics Test Scene", priority = 212)]
    private static void CreateOrOpenSceneFromMenu()
    {
        CreateOrOpenScene();
    }

    public static void CreateSceneFromCommandLine()
    {
        CreateOrOpenScene(rebuild: true);
        EditorApplication.Exit(0);
    }

    public static ShuttlecockDebugController CreateOrOpenScene(bool rebuild = false)
    {
        if (!rebuild && File.Exists(ScenePath))
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return FindOrCreateController(scene, rebuild: false);
        }

        if (!AssetDatabase.IsValidFolder(SceneDirectory))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            AssetDatabase.CreateFolder("Assets/Scenes", "Test");
        }

        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        ShuttlecockDebugController controller = FindOrCreateController(newScene, rebuild: true);
        EditorSceneManager.SaveScene(newScene, ScenePath);
        AssetDatabase.Refresh();
        Selection.activeObject = controller.gameObject;
        return controller;
    }

    private static ShuttlecockDebugController FindOrCreateController(Scene scene, bool rebuild)
    {
        ShuttlecockDebugController controller = Object.FindObjectOfType<ShuttlecockDebugController>();
        if (controller != null && !rebuild)
        {
            return controller;
        }

        if (controller != null && rebuild)
        {
            Object.DestroyImmediate(controller.gameObject);
        }

        GameObject root = new GameObject("BadmintonPhysicsTestRoot");
        root.transform.position = Vector3.zero;
        controller = root.AddComponent<ShuttlecockDebugController>();
        root.AddComponent<ShuttlecockTrajectoryDebug>();

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "ShuttlecockVisual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.08f;
        TrailRenderer trailRenderer = visual.AddComponent<TrailRenderer>();
        trailRenderer.time = 2.0f;
        trailRenderer.startWidth = 0.045f;
        trailRenderer.endWidth = 0.01f;
        trailRenderer.minVertexDistance = 0.02f;

        GameObject court = new GameObject("CourtLineRenderer");
        court.transform.SetParent(root.transform, false);
        LineRenderer courtLine = court.AddComponent<LineRenderer>();
        courtLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        courtLine.receiveShadows = false;

        GameObject trajectory = new GameObject("TrajectoryLineRenderer");
        trajectory.transform.SetParent(root.transform, false);
        LineRenderer trajectoryLine = trajectory.AddComponent<LineRenderer>();
        trajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trajectoryLine.receiveShadows = false;

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("shuttlecockVisual").objectReferenceValue = visual.transform;
        serializedController.FindProperty("shuttlecockTrail").objectReferenceValue = trailRenderer;
        serializedController.FindProperty("trajectoryLine").objectReferenceValue = trajectoryLine;
        serializedController.FindProperty("courtLine").objectReferenceValue = courtLine;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        controller.ResetSimulation();
        controller.RefreshCourtLine();

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.transform.position = new Vector3(0.0f, 8.5f, -11.0f);
            mainCamera.transform.rotation = Quaternion.Euler(28.0f, 0.0f, 0.0f);
        }

        return controller;
    }
}
