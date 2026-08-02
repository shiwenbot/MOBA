using GameShared.FrameSync.Battle;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// P1 俯视角 Gameplay 状态同步沙盒的运行时表现层。
    /// 房间表现只负责渲染，移动状态仍由共享帧同步模拟驱动。
    /// </summary>
    public sealed class GameplaySandboxView : MonoBehaviour
    {
        private const float FloorThickness = 0.2f;
        private const float WallThickness = 0.25f;
        private const float WallHeight = 0.5f;
        private static readonly Color FloorColor = new Color(0.08f, 0.11f, 0.15f);
        private static readonly Color WallColor = new Color(0.88f, 0.47f, 0.18f);
        private static readonly Color BackgroundColor = new Color(0.025f, 0.035f, 0.05f);

        // The view is created after the frame-sync client has finished initializing.
        private bool _isInitialized;
        private Camera _camera;

        public static GameplaySandboxView Create()
        {
            GameplaySandboxView existing = FindObjectOfType<GameplaySandboxView>();
            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject("GameplaySandbox");
            GameplaySandboxView view = root.AddComponent<GameplaySandboxView>();
            view.Initialize();
            return view;
        }

        public void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            CreateRoomVisuals();
            ConfigureCamera();
            CreateLighting();

            BattleClientController controller = gameObject.AddComponent<BattleClientController>();
            controller.Initialize();

            if (BattleAutomationConfig.Current.Enabled)
            {
                Log.Warning(
                    $"[Automation] GameplaySandbox enabling automation controller. client={BattleAutomationConfig.Current.ClientId}");
                BattleAutomationController automationController = gameObject.AddComponent<BattleAutomationController>();
                automationController.Initialize(controller);
            }
        }

        private void CreateRoomVisuals()
        {
            Transform roomRoot = new GameObject("GameplayRoom").transform;
            roomRoot.SetParent(transform, false);

            float roomWidth = (float)(GameplayRoomSettings.RoomHalfWidth * FixedMathSharp.Fixed64.Two);
            float roomHeight = (float)(GameplayRoomSettings.RoomHalfHeight * FixedMathSharp.Fixed64.Two);
            CreateCube(
                roomRoot,
                "RoomFloor",
                new Vector3(0.0f, -FloorThickness * 0.5f, 0.0f),
                new Vector3(roomWidth, FloorThickness, roomHeight),
                FloorColor);
            CreateCube(
                roomRoot,
                "RoomWallNorth",
                new Vector3(0.0f, WallHeight * 0.5f, roomHeight * 0.5f),
                new Vector3(roomWidth + WallThickness, WallHeight, WallThickness),
                WallColor);
            CreateCube(
                roomRoot,
                "RoomWallSouth",
                new Vector3(0.0f, WallHeight * 0.5f, -roomHeight * 0.5f),
                new Vector3(roomWidth + WallThickness, WallHeight, WallThickness),
                WallColor);
            CreateCube(
                roomRoot,
                "RoomWallEast",
                new Vector3(roomWidth * 0.5f, WallHeight * 0.5f, 0.0f),
                new Vector3(WallThickness, WallHeight, roomHeight),
                WallColor);
            CreateCube(
                roomRoot,
                "RoomWallWest",
                new Vector3(-roomWidth * 0.5f, WallHeight * 0.5f, 0.0f),
                new Vector3(WallThickness, WallHeight, roomHeight),
                WallColor);
        }

        private void ConfigureCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject cameraObject = new GameObject("GameplayCamera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.SetParent(transform, false);
                _camera = cameraObject.AddComponent<Camera>();
            }

            _camera.transform.position = new Vector3(0.0f, 20.0f, 0.0f);
            _camera.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            _camera.orthographic = true;
            _camera.orthographicSize = CalculateOrthographicSize(_camera.aspect);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100.0f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = BackgroundColor;
        }

        private void CreateLighting()
        {
            GameObject lightObject = new GameObject("GameplayKeyLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48.0f, -32.0f, 0.0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = Color.white;
        }

        private static GameObject CreateCube(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;

            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }

            return cube;
        }

        private static float CalculateOrthographicSize(float aspect)
        {
            float safeAspect = Mathf.Max(0.1f, aspect);
            float verticalMargin = 1.0f;
            float horizontalMargin = 1.0f;
            float halfHeight = (float)GameplayRoomSettings.RoomHalfHeight + verticalMargin;
            float halfWidth = ((float)GameplayRoomSettings.RoomHalfWidth + horizontalMargin) / safeAspect;
            return Mathf.Max(halfHeight, halfWidth);
        }
    }
}
