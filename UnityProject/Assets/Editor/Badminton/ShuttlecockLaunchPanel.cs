using GameLogic;
using GameShared.Badminton.Config;
using UnityEditor;
using UnityEngine;

public sealed class ShuttlecockLaunchPanel : EditorWindow
{
    private ShuttlecockDebugController _controller;
    private ShuttlecockShotType _shotType = ShuttlecockShotType.Clear;
    private Vector2 _originXZ = new Vector2(0.0f, -3.0f);
    private float _originY = 1.8f;
    private Vector2 _directionXZ = Vector2.up;
    private int _previewFrames = 180;

    [MenuItem("TEngine/Badminton/Shuttlecock Launch Panel", priority = 211)]
    private static void Open()
    {
        GetWindow<ShuttlecockLaunchPanel>("Shuttlecock Launch");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("场景控制器", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _controller = (ShuttlecockDebugController)EditorGUILayout.ObjectField(_controller, typeof(ShuttlecockDebugController), true);
            if (GUILayout.Button("查找", GUILayout.Width(80)))
            {
                _controller = FindObjectOfType<ShuttlecockDebugController>();
            }
        }

        if (_controller == null)
        {
            EditorGUILayout.HelpBox("当前场景还没有 ShuttlecockDebugController。可以先创建测试场景。", MessageType.Info);
        }

        if (GUILayout.Button("创建或打开测试场景"))
        {
            _controller = BadmintonPhysicsTestSceneBuilder.CreateOrOpenScene();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("击发参数", EditorStyles.boldLabel);
        _shotType = (ShuttlecockShotType)EditorGUILayout.EnumPopup("球路", _shotType);
        _originXZ = EditorGUILayout.Vector2Field("起点 XZ", _originXZ);
        _originY = EditorGUILayout.FloatField("起点 Y", _originY);
        _directionXZ = EditorGUILayout.Vector2Field("方向 XZ", _directionXZ);
        _previewFrames = Mathf.Max(1, EditorGUILayout.IntField("预览帧数", _previewFrames));

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = _controller != null;
            if (GUILayout.Button("预览轨迹"))
            {
                _controller.PreviewShot(_shotType, _originXZ, _originY, _directionXZ, _previewFrames);
                SceneView.RepaintAll();
            }

            GUI.enabled = _controller != null && Application.isPlaying;
            if (GUILayout.Button("下一帧击发"))
            {
                _controller.Launch(_shotType, _originXZ, _originY, _directionXZ);
            }

            GUI.enabled = _controller != null;
            if (GUILayout.Button("重置仿真"))
            {
                _controller.ResetSimulation();
                SceneView.RepaintAll();
            }

            GUI.enabled = true;
        }

        EditorGUILayout.Space();
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("预览轨迹可在编辑态使用；实际按帧击发需要进入 Play Mode。", MessageType.None);
        }

        if (_controller != null)
        {
            EditorGUILayout.LabelField("实时状态", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_controller.DescribeState(), MessageType.None);
        }
    }
}
