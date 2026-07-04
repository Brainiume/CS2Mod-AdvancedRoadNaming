using Colossal.Mathematics;
using Game;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteHighlightSystem : GameSystemBase
    {
        private static readonly float2 RoundedLine = new float2(0f, 1f);

        private const float RouteWidth = 4.8f;
        private const float SavedRouteWidth = 4.4f;
        private const float ManagedRouteWidth = 3.4f;
        private const float SelectedManagedRouteWidth = 5.2f;
        private const float PreviewWidth = 4.1f;
        private const float HoverWidth = 3.8f;
        private const float WaypointRadius = 8.6f;
        private const float SavedWaypointRadius = 7.8f;
        private const float WaypointHaloRadius = 13.8f;
        private const float SavedWaypointHaloRadius = 12.4f;

        private static readonly Color RouteColor = new Color(0.22f, 0.86f, 0.12f, 0.78f);
        private static readonly Color SavedRouteColor = new Color(0.22f, 0.86f, 0.12f, 0.64f);
        private static readonly Color ManagedRouteColor = new Color(0.22f, 0.86f, 0.12f, 0.26f);
        private static readonly Color SelectedManagedRouteColor = new Color(0.22f, 0.86f, 0.12f, 0.82f);
        private static readonly Color PreviewColor = new Color(0.32f, 0.9f, 0.18f, 0.46f);
        private static readonly Color HoverColor = new Color(0.42f, 0.94f, 0.28f, 0.28f);
        private static readonly Color WaypointColor = new Color(0.18f, 0.84f, 0.1f, 0.9f);
        private static readonly Color SavedWaypointColor = new Color(0.18f, 0.84f, 0.1f, 0.74f);
        private static readonly Color ManagedWaypointColor = new Color(0.18f, 0.84f, 0.1f, 0.34f);
        private static readonly Color SelectedManagedWaypointColor = new Color(0.18f, 0.84f, 0.1f, 0.94f);
        private static readonly Color ActiveWaypointColor = new Color(0.08f, 0.52f, 1f, 0.98f);
        private static readonly Color RemoveWaypointColor = new Color(1f, 0.12f, 0.08f, 0.98f);
        private static readonly Color WaypointHaloColor = new Color(0.92f, 1f, 0.9f, 0.24f);
        private static readonly Color SavedWaypointHaloColor = new Color(0.92f, 1f, 0.9f, 0.16f);
        private static readonly Color ManagedWaypointHaloColor = new Color(0.92f, 1f, 0.9f, 0.08f);
        private static readonly Color SelectedManagedWaypointHaloColor = new Color(0.92f, 1f, 0.9f, 0.24f);
        private static readonly Color ActiveWaypointHaloColor = new Color(0.55f, 0.8f, 1f, 0.38f);
        private static readonly Color RemoveWaypointHaloColor = new Color(1f, 0.65f, 0.6f, 0.4f);

        private RoadRouteToolSystem _toolSystem;
        private RoadRouteOverlayGeometrySystem _geometrySystem;
        private OverlayRenderSystem _overlayRenderSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _geometrySystem = World.GetOrCreateSystemManaged<RoadRouteOverlayGeometrySystem>();
            _overlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
        }

        protected override void OnUpdate()
        {
            if (_toolSystem == null || !_toolSystem.IsRunning || _overlayRenderSystem == null || _geometrySystem == null)
                return;

            var buffer = _overlayRenderSystem.GetBuffer(out var renderBufferJobHandle);
            renderBufferJobHandle.Complete();

            DrawManagedRoutes(buffer);
            DrawGeometry(buffer, _geometrySystem.SavedRouteCurves, SavedRouteColor, SavedRouteWidth);
            DrawNodes(buffer, _geometrySystem.SavedRouteNodes, SavedWaypointHaloColor, SavedWaypointHaloRadius);
            DrawNodes(buffer, _geometrySystem.SavedRouteNodes, SavedWaypointColor, SavedWaypointRadius);
            DrawGeometry(buffer, _geometrySystem.ActiveCurves, RouteColor, RouteWidth);
            DrawNodes(buffer, _geometrySystem.ActiveNodes, WaypointHaloColor, WaypointHaloRadius);
            DrawNodes(buffer, _geometrySystem.ActiveNodes, WaypointColor, WaypointRadius);
            DrawGeometry(buffer, _geometrySystem.PreviewCurves, PreviewColor, PreviewWidth);
            DrawNodes(buffer, _geometrySystem.PreviewNodes, WaypointHaloColor, WaypointHaloRadius);
            DrawNodes(buffer, _geometrySystem.PreviewNodes, WaypointColor, WaypointRadius);
            DrawGeometry(buffer, _geometrySystem.HoverCurves, HoverColor, HoverWidth);
            DrawWaypointInteractionState(buffer);
        }

        private void DrawWaypointInteractionState(OverlayRenderSystem.Buffer buffer)
        {
            var waypoints = _toolSystem.Waypoints;
            var hoveredIndex = _toolSystem.HoveredWaypointIndex;
            if (hoveredIndex >= 0 && waypoints != null && hoveredIndex < waypoints.Count)
            {
                var hoveredPosition = waypoints[hoveredIndex].Position;
                buffer.DrawCircle(ActiveWaypointHaloColor, hoveredPosition, WaypointHaloRadius);
                buffer.DrawCircle(ActiveWaypointColor, hoveredPosition, WaypointRadius);
            }

            var activeEditIndex = _toolSystem.ActiveEditIndex;
            if (_toolSystem.HasActiveMoveEdit && activeEditIndex >= 0)
            {
                var activeWaypoints = _toolSystem.PreviewWaypoints;
                if (activeWaypoints == null || activeEditIndex >= activeWaypoints.Count)
                    activeWaypoints = _toolSystem.Waypoints;

                if (activeWaypoints != null && activeEditIndex < activeWaypoints.Count)
                {
                    var position = activeWaypoints[activeEditIndex].Position;
                    buffer.DrawCircle(ActiveWaypointHaloColor, position, WaypointHaloRadius);
                    buffer.DrawCircle(ActiveWaypointColor, position, WaypointRadius);
                }
            }

            var removalIndex = hoveredIndex;
            if (!_toolSystem.IsWaypointRemovalArmed || removalIndex < 0 || waypoints == null || removalIndex >= waypoints.Count)
                return;

            var removalPosition = waypoints[removalIndex].Position;
            buffer.DrawCircle(RemoveWaypointHaloColor, removalPosition, WaypointHaloRadius);
            buffer.DrawCircle(RemoveWaypointColor, removalPosition, WaypointRadius);
        }

        private void DrawManagedRoutes(OverlayRenderSystem.Buffer buffer)
        {
            var groups = _geometrySystem.ManagedRouteGroups;
            if (groups == null || groups.Count == 0)
                return;

            for (var pass = 0; pass < 2; pass++)
            {
                var drawSelected = pass == 1;
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    if (group == null || group.Selected != drawSelected)
                        continue;

                    DrawGeometry(buffer, group.Curves, drawSelected ? SelectedManagedRouteColor : ManagedRouteColor, drawSelected ? SelectedManagedRouteWidth : ManagedRouteWidth);
                    DrawNodes(buffer, group.Nodes, drawSelected ? SelectedManagedWaypointHaloColor : ManagedWaypointHaloColor, drawSelected ? WaypointHaloRadius : SavedWaypointHaloRadius);
                    DrawNodes(buffer, group.Nodes, drawSelected ? SelectedManagedWaypointColor : ManagedWaypointColor, drawSelected ? WaypointRadius : SavedWaypointRadius);
                }
            }
        }

        private static void DrawGeometry(OverlayRenderSystem.Buffer buffer, System.Collections.Generic.IReadOnlyList<Bezier4x3> curves, Color lineColor, float lineWidth)
        {
            if (curves == null)
                return;

            for (var i = 0; i < curves.Count; i++)
                buffer.DrawCurve(lineColor, curves[i], lineWidth, RoundedLine);
        }

        private static void DrawNodes(OverlayRenderSystem.Buffer buffer, System.Collections.Generic.IReadOnlyList<float3> nodes, Color color, float radius)
        {
            if (nodes == null)
                return;

            for (var i = 0; i < nodes.Count; i++)
                buffer.DrawCircle(color, nodes[i], radius);
        }
    }
}
