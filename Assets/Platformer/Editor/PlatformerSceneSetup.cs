using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Assets/Platformer/Scenes/PlatformerDemo.unity out of 3D primitives + 2D physics (no sprites):
/// ground, steps, a ledge, one-way planks, a moving platform, a wall, and the Player.
/// Runs once automatically after the scripts compile (if the scene does not exist yet);
/// re-run any time from the menu: Platformer > Create Demo Scene.
/// </summary>
[InitializeOnLoad]
public static class PlatformerSceneSetup
{
    const string Root = "Assets/Platformer";
    public const string ScenePath = Root + "/Scenes/PlatformerDemo.unity";
    const string MaterialsDir = Root + "/Materials";

    static PlatformerSceneSetup()
    {
        EditorApplication.delayCall += AutoCreate;
    }

    static void AutoCreate()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (File.Exists(ScenePath)) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;    // user cancelled: leave their scene alone
        CreateDemoScene();
        Debug.Log($"[Platformer] Created and opened {ScenePath}. Press Play: A/D or arrows to run, Space to jump (hold for higher), S + Space to drop through planks.");
    }

    [MenuItem("Platformer/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Platformer/Scenes"));
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Platformer/Materials"));
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);   // camera + light, pipeline-ready

        var camera = Camera.main;
        camera.orthographic = true;
        camera.orthographicSize = 6f;                          // 12 units visible vertically
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.49f, 0.78f, 0.94f);
        camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, -10f), Quaternion.identity);
        var follow = camera.gameObject.AddComponent<CameraFollow>();
        follow.offset = new Vector2(0f, 2f);
        follow.useBounds = true;
        follow.bounds = new Rect(-12f, -1f, 64f, 20f);

        var ground = Mat("Ground", new Color(0.36f, 0.62f, 0.33f));
        var wall = Mat("Wall", new Color(0.45f, 0.45f, 0.5f));
        var plank = Mat("Plank", new Color(0.76f, 0.55f, 0.30f));
        var mover = Mat("MovingPlatform", new Color(0.30f, 0.55f, 0.85f));
        var playerMat = Mat("Player", new Color(0.95f, 0.45f, 0.20f));

        var level = new GameObject("Level").transform;
        Block("Ground", 20f, -0.5f, 64f, 1f, ground, level);
        Block("Left Wall", -12.5f, 5f, 1f, 12f, wall, level);
        Block("Right Wall", 52.5f, 5f, 1f, 12f, wall, level);
        Block("Step 1", 6f, 0.5f, 2f, 1f, ground, level);
        Block("Step 2", 9.5f, 1f, 2f, 2f, ground, level);
        Block("Step 3", 13.5f, 1.5f, 2f, 3f, ground, level);
        Block("Ledge", 19f, 4f, 3f, 1f, ground, level);
        Block("Corner Test Ceiling", 22.3f, 3.5f, 1f, 1f, wall, level);   // jump straight up under its left edge: corner correction
        OneWay("Plank 1", 27f, 3f, 4f, plank, level);
        OneWay("Plank 2", 27f, 6f, 4f, plank, level);
        Moving("Moving Platform", 33f, 1.5f, 3f, 0.5f, new Vector2(6f, 4f), mover, level);
        Block("Tower", 45f, 3f, 2f, 6f, wall, level);                       // enable Wall Jump on the Player and try it

        var player = Player(0f, 0.45f, playerMat);
        follow.target = player.transform;

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();

        var view = SceneView.lastActiveSceneView;
        if (view != null) view.in2DMode = true;
        Selection.activeGameObject = player;
    }

    /// <summary>Headless entry point: Unity -batchmode -executeMethod PlatformerSceneSetup.CreateDemoSceneBatch -quit</summary>
    public static void CreateDemoSceneBatch() => CreateDemoScene();

    // ------------------------------------------------------------------ builders

    static Material Mat(string name, Color color)
    {
        string path = $"{MaterialsDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material mat, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());     // no 3D collider: the game uses 2D physics
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject Block(string name, float cx, float cy, float w, float h, Material mat, Transform parent)
    {
        var go = Primitive(PrimitiveType.Cube, name, new Vector3(cx, cy, 0f), new Vector3(w, h, 1f), mat, parent);
        go.AddComponent<BoxCollider2D>();                          // size (1,1) × scale = (w,h)
        return go;
    }

    static GameObject OneWay(string name, float cx, float topY, float w, Material mat, Transform parent)
    {
        var go = Primitive(PrimitiveType.Cube, name, new Vector3(cx, topY - 0.1f, 0f), new Vector3(w, 0.2f, 1f), mat, parent);
        go.AddComponent<BoxCollider2D>().usedByEffector = true;
        var effector = go.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;                                 // solid from above, passable from below
        effector.surfaceArc = 170f;
        return go;
    }

    static GameObject Moving(string name, float cx, float cy, float w, float h, Vector2 offset, Material mat, Transform parent)
    {
        var go = Block(name, cx, cy, w, h, mat, parent);
        go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var platform = go.AddComponent<MovingPlatform>();
        platform.offset = offset;
        platform.speed = 2f;
        return go;
    }

    static GameObject Player(float x, float y, Material mat)
    {
        var go = new GameObject("Player") { tag = "Player" };
        go.transform.position = new Vector3(x, y, 0f);
        // The visual is a child so the collider itself is never non-uniformly scaled.
        Primitive(PrimitiveType.Capsule, "Visual", go.transform.position, new Vector3(0.7f, 0.45f, 0.7f), mat, go.transform);

        go.AddComponent<Rigidbody2D>();
        var capsule = go.AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(0.7f, 0.9f);
        capsule.direction = CapsuleDirection2D.Vertical;
        go.AddComponent<PlayerController>();
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<InputActionsSource>();
#endif
        return go;
    }
}
