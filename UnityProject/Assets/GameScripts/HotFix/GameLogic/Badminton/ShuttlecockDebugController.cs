using System;
using System.Collections.Generic;
using GameShared.Badminton;
using GameShared.Badminton.Config;
using GameShared.FrameSync.Determinism;
using UnityEngine;

namespace GameLogic
{
    [ExecuteAlways]
    public sealed class ShuttlecockDebugController : MonoBehaviour
    {
        private const int DefaultPreviewFrameCount = 180;

        [SerializeField] private ShuttlecockShotType shotType = ShuttlecockShotType.Clear;
        [SerializeField] private Vector2 launchOriginXZ = new Vector2(0.0f, -3.0f);
        [SerializeField] private float launchOriginY = 1.8f;
        [SerializeField] private Vector2 launchDirectionXZ = Vector2.up;
        [SerializeField] private bool autoLaunchOnPlay;
        [SerializeField] private Transform shuttlecockVisual;
        [SerializeField] private TrailRenderer shuttlecockTrail;
        [SerializeField] private LineRenderer trajectoryLine;
        [SerializeField] private LineRenderer courtLine;
        [SerializeField] private float visualScale = 0.08f;
        [SerializeField] private int previewFrameCount = DefaultPreviewFrameCount;

        private readonly List<Vector3> _trajectoryPoints = new List<Vector3>(DefaultPreviewFrameCount + 1);
        private float _accumulator;
        private ShuttlecockEntity _entity;

        public uint CurrentFrame { get; private set; }
        public float CurrentFlightTimeSeconds { get; private set; }
        public ShuttlecockFlightPhase CurrentPhase => _entity?.State.Phase ?? ShuttlecockFlightPhase.Idle;
        public IReadOnlyList<Vector3> TrajectoryPoints => _trajectoryPoints;
        public ShuttlecockEntity Entity => _entity;
        public ShuttlecockShotType ShotType
        {
            get => shotType;
            set => shotType = value;
        }

        public Vector2 LaunchOriginXZ
        {
            get => launchOriginXZ;
            set => launchOriginXZ = value;
        }

        public float LaunchOriginY
        {
            get => launchOriginY;
            set => launchOriginY = value;
        }

        public Vector2 LaunchDirectionXZ
        {
            get => launchDirectionXZ;
            set => launchDirectionXZ = value;
        }

        public int PreviewFrameCount
        {
            get => previewFrameCount;
            set => previewFrameCount = Mathf.Max(1, value);
        }

        private void OnEnable()
        {
            EnsureVisualReferences();
            InitializeEntity();
            RefreshCourtLine();
            RefreshVisual();
            if (Application.isPlaying && autoLaunchOnPlay)
            {
                LaunchDefaultShot();
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                RefreshVisual();
                return;
            }

            EnsureEntity();
            _accumulator += Time.deltaTime;
            while (_accumulator >= DeterminismRules.FixedDeltaTime)
            {
                _accumulator -= DeterminismRules.FixedDeltaTime;
                CurrentFrame++;
                _entity.Tick(CurrentFrame, DeterminismRules.FixedDeltaTime);
                if (_entity.State.Phase == ShuttlecockFlightPhase.Flying)
                {
                    CurrentFlightTimeSeconds += DeterminismRules.FixedDeltaTime;
                }

                AppendTrajectoryPoint(ToWorldPosition(_entity.State.XZ, _entity.State.Y));
                RefreshVisual();
            }
        }

        private void OnValidate()
        {
            previewFrameCount = Mathf.Max(1, previewFrameCount);
            visualScale = Mathf.Max(0.01f, visualScale);
            EnsureVisualReferences();
            RefreshCourtLine();
            RefreshVisual();
        }

        public void LaunchDefaultShot()
        {
            Launch(shotType, launchOriginXZ, launchOriginY, launchDirectionXZ);
        }

        public void Launch(ShuttlecockShotType targetShotType, Vector2 originXZ, float originY, Vector2 directionXZ)
        {
            EnsureEntity();
            ShuttlecockLaunchCommand command = new ShuttlecockLaunchCommand
            {
                TargetFrame = CurrentFrame + 1,
                ShotType = targetShotType,
                OriginXZ = originXZ,
                OriginY = originY,
                DirectionXZ = directionXZ
            };

            _entity.EnqueueLaunch(command);
        }

        public void ResetSimulation()
        {
            _accumulator = 0.0f;
            CurrentFrame = 0;
            CurrentFlightTimeSeconds = 0.0f;
            _trajectoryPoints.Clear();
            InitializeEntity();
            ClearTrail();
            RefreshTrajectoryLine();
            RefreshVisual();
        }

        public void PreviewShot()
        {
            PreviewShot(shotType, launchOriginXZ, launchOriginY, launchDirectionXZ, previewFrameCount);
        }

        public void PreviewShot(
            ShuttlecockShotType targetShotType,
            Vector2 originXZ,
            float originY,
            Vector2 directionXZ,
            int maxFrames)
        {
            List<Vector3> previewPoints = SimulateTrajectory(targetShotType, originXZ, originY, directionXZ, maxFrames);
            _trajectoryPoints.Clear();
            _trajectoryPoints.AddRange(previewPoints);
            RefreshTrajectoryLine();

            if (_trajectoryPoints.Count > 0 && shuttlecockVisual != null)
            {
                shuttlecockVisual.position = _trajectoryPoints[0];
                shuttlecockVisual.localScale = Vector3.one * visualScale;
            }
        }

        public List<Vector3> SimulateTrajectory(
            ShuttlecockShotType targetShotType,
            Vector2 originXZ,
            float originY,
            Vector2 directionXZ,
            int maxFrames)
        {
            ShuttlecockEntity previewEntity = new ShuttlecockEntity(ShuttlecockShotRuntimeConfigProvider.Instance);
            previewEntity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 1,
                ShotType = targetShotType,
                OriginXZ = originXZ,
                OriginY = originY,
                DirectionXZ = directionXZ
            });

            List<Vector3> points = new List<Vector3>(Mathf.Max(2, maxFrames + 1))
            {
                ToWorldPosition(originXZ, originY)
            };
            int cappedFrames = Mathf.Max(1, maxFrames);
            for (uint frame = 1; frame <= cappedFrames; frame++)
            {
                previewEntity.Tick(frame, DeterminismRules.FixedDeltaTime);
                points.Add(ToWorldPosition(previewEntity.State.XZ, previewEntity.State.Y));
                if (previewEntity.State.IsTerminal)
                {
                    break;
                }
            }

            return points;
        }

        public void RefreshCourtLine()
        {
            if (courtLine == null)
            {
                return;
            }

            courtLine.useWorldSpace = false;
            courtLine.loop = false;
            courtLine.positionCount = 11;
            courtLine.widthMultiplier = 0.025f;
            courtLine.SetPositions(new[]
            {
                ToLocalGround(CourtConstants.NearLeftCorner),
                ToLocalGround(CourtConstants.NearRightCorner),
                ToLocalGround(CourtConstants.FarRightCorner),
                ToLocalGround(CourtConstants.FarLeftCorner),
                ToLocalGround(CourtConstants.NearLeftCorner),
                ToLocalGround(new Vector2(-CourtConstants.HalfSinglesWidth, 0.0f)),
                ToLocalGround(new Vector2(CourtConstants.HalfSinglesWidth, 0.0f)),
                ToLocalGround(new Vector2(0.0f, 0.0f)),
                ToLocalGround(new Vector2(0.0f, CourtConstants.HalfLength)),
                ToLocalGround(new Vector2(0.0f, 0.0f)),
                ToLocalGround(new Vector2(0.0f, -CourtConstants.HalfLength))
            });
        }

        public string DescribeState()
        {
            if (_entity == null)
            {
                return "未初始化";
            }

            ShuttlecockState state = _entity.State;
            return
                $"Frame={CurrentFrame}, Phase={state.Phase}, Pos=({state.XZ.x:F2}, {state.Y:F2}, {state.XZ.y:F2}), " +
                $"Vel=({state.Vxz.x:F2}, {state.Vy:F2}, {state.Vxz.y:F2}), Flight={CurrentFlightTimeSeconds:F2}s";
        }

        private void InitializeEntity()
        {
            _entity = new ShuttlecockEntity(ShuttlecockShotRuntimeConfigProvider.Instance);
        }

        private void EnsureEntity()
        {
            if (_entity == null)
            {
                InitializeEntity();
            }
        }

        private void EnsureVisualReferences()
        {
            if (shuttlecockVisual == null)
            {
                Transform candidate = transform.Find("ShuttlecockVisual");
                if (candidate != null)
                {
                    shuttlecockVisual = candidate;
                }
            }

            if (shuttlecockTrail == null && shuttlecockVisual != null)
            {
                shuttlecockTrail = shuttlecockVisual.GetComponent<TrailRenderer>();
            }
        }

        private void AppendTrajectoryPoint(Vector3 point)
        {
            if (_trajectoryPoints.Count > 0 && Vector3.Distance(_trajectoryPoints[_trajectoryPoints.Count - 1], point) <= 0.001f)
            {
                return;
            }

            _trajectoryPoints.Add(point);
            RefreshTrajectoryLine();
        }

        private void RefreshTrajectoryLine()
        {
            if (trajectoryLine == null)
            {
                return;
            }

            trajectoryLine.useWorldSpace = true;
            trajectoryLine.loop = false;
            trajectoryLine.widthMultiplier = 0.035f;
            trajectoryLine.positionCount = _trajectoryPoints.Count;
            if (_trajectoryPoints.Count > 0)
            {
                trajectoryLine.SetPositions(_trajectoryPoints.ToArray());
            }
        }

        private void RefreshVisual()
        {
            if (shuttlecockVisual == null)
            {
                return;
            }

            EnsureEntity();
            shuttlecockVisual.position = ToWorldPosition(_entity.State.XZ, _entity.State.Y);
            shuttlecockVisual.localScale = Vector3.one * visualScale;
        }

        private void ClearTrail()
        {
            if (shuttlecockTrail != null)
            {
                shuttlecockTrail.Clear();
            }
        }

        private static Vector3 ToWorldPosition(Vector2 xz, float y)
        {
            return new Vector3(xz.x, y, xz.y);
        }

        private static Vector3 ToLocalGround(Vector2 xz)
        {
            return new Vector3(xz.x, 0.0f, xz.y);
        }
    }
}
