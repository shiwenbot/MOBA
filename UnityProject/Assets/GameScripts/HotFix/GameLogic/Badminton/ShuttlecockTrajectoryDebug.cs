using System.Collections.Generic;
using FixedMathSharp;
using GameShared.Badminton;
using UnityEngine;

namespace GameLogic
{
    [ExecuteAlways]
    public sealed class ShuttlecockTrajectoryDebug : MonoBehaviour
    {
        [SerializeField] private ShuttlecockDebugController controller;
        [SerializeField] private Color courtColor = new Color(0.18f, 0.72f, 0.25f, 1.0f);
        [SerializeField] private Color trajectoryColor = new Color(0.95f, 0.82f, 0.16f, 1.0f);
        [SerializeField] private Color landingColor = new Color(0.98f, 0.28f, 0.18f, 1.0f);

        private void Reset()
        {
            controller = GetComponent<ShuttlecockDebugController>();
        }

        private void OnDrawGizmos()
        {
            if (controller == null)
            {
                controller = GetComponent<ShuttlecockDebugController>();
            }

            DrawCourt();
            DrawTrajectory();
        }

        private void DrawCourt()
        {
            Gizmos.color = courtColor;
            DrawGroundLine(CourtConstants.NearLeftCorner, CourtConstants.NearRightCorner);
            DrawGroundLine(CourtConstants.NearRightCorner, CourtConstants.FarRightCorner);
            DrawGroundLine(CourtConstants.FarRightCorner, CourtConstants.FarLeftCorner);
            DrawGroundLine(CourtConstants.FarLeftCorner, CourtConstants.NearLeftCorner);
            DrawGroundLine(new Vector2d(-CourtConstants.HalfSinglesWidth, Fixed64.Zero), new Vector2d(CourtConstants.HalfSinglesWidth, Fixed64.Zero));
        }

        private void DrawTrajectory()
        {
            if (controller == null)
            {
                return;
            }

            IReadOnlyList<Vector3> points = controller.TrajectoryPoints;
            if (points == null || points.Count == 0)
            {
                return;
            }

            Gizmos.color = trajectoryColor;
            for (int i = 1; i < points.Count; i++)
            {
                Gizmos.DrawLine(points[i - 1], points[i]);
            }

            Gizmos.color = landingColor;
            Vector3 lastPoint = points[points.Count - 1];
            Gizmos.DrawSphere(lastPoint, 0.06f);
        }

        private static void DrawGroundLine(Vector2d from, Vector2d to)
        {
            Gizmos.DrawLine(
                new Vector3((float)from.x, 0.0f, (float)from.y),
                new Vector3((float)to.x, 0.0f, (float)to.y));
        }
    }
}
