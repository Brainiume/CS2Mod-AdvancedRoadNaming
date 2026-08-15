using Colossal.Mathematics;
using Game;
using Game.Rendering;
using AdvancedRoadNaming.Domain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
        private const float SnappedWaypointRadius = 11.2f;
        private const float SnappedWaypointHaloRadius = 18.6f;

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
        private static readonly Color RenameRouteColor = new Color(0.66f, 0.33f, 0.97f, 0.78f);
        private static readonly Color RenameSavedRouteColor = new Color(0.66f, 0.33f, 0.97f, 0.64f);
        private static readonly Color RenameManagedRouteColor = new Color(0.66f, 0.33f, 0.97f, 0.26f);
        private static readonly Color RenameSelectedManagedRouteColor = new Color(0.66f, 0.33f, 0.97f, 0.82f);
        private static readonly Color RenamePreviewColor = new Color(0.74f, 0.48f, 0.98f, 0.46f);
        private static readonly Color RenameHoverColor = new Color(0.78f, 0.58f, 0.99f, 0.28f);
        private static readonly Color RenameWaypointColor = new Color(0.66f, 0.33f, 0.97f, 0.9f);
        private static readonly Color RenameSavedWaypointColor = new Color(0.66f, 0.33f, 0.97f, 0.74f);
        private static readonly Color RenameManagedWaypointColor = new Color(0.66f, 0.33f, 0.97f, 0.34f);
        private static readonly Color RenameSelectedManagedWaypointColor = new Color(0.66f, 0.33f, 0.97f, 0.94f);
        private static readonly Color RenameWaypointHaloColor = new Color(0.88f, 0.77f, 1f, 0.24f);
        private static readonly Color RenameSavedWaypointHaloColor = new Color(0.88f, 0.77f, 1f, 0.16f);
        private static readonly Color RenameManagedWaypointHaloColor = new Color(0.88f, 0.77f, 1f, 0.08f);
        private static readonly Color RenameSelectedManagedWaypointHaloColor = new Color(0.88f, 0.77f, 1f, 0.24f);

        private RoadRouteToolSystem _toolSystem;
        private RoadRouteOverlayGeometrySystem _geometrySystem;
        private OverlayRenderSystem _overlayRenderSystem;
        private NativeList<CurveDrawCommand> _curveCommands;
        private NativeList<CircleDrawCommand> _circleCommands;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _geometrySystem = World.GetOrCreateSystemManaged<RoadRouteOverlayGeometrySystem>();
            _overlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _curveCommands = new NativeList<CurveDrawCommand>(128, Allocator.Persistent);
            _circleCommands = new NativeList<CircleDrawCommand>(64, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            CompleteDependency();
            if (_curveCommands.IsCreated)
                _curveCommands.Dispose();
            if (_circleCommands.IsCreated)
                _circleCommands.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (_toolSystem == null || !_toolSystem.IsRunning || _overlayRenderSystem == null || _geometrySystem == null)
                return;

            _curveCommands.Clear();
            _circleCommands.Clear();

            var renameMode = _toolSystem.Mode == RoadRouteToolMode.RenameSelectedSegments;

            DrawManagedRoutes();
            DrawGeometry(_geometrySystem.SavedRouteCurves, renameMode ? RenameSavedRouteColor : SavedRouteColor, SavedRouteWidth);
            DrawNodes(_geometrySystem.SavedRouteNodes, renameMode ? RenameSavedWaypointHaloColor : SavedWaypointHaloColor, SavedWaypointHaloRadius);
            DrawNodes(_geometrySystem.SavedRouteNodes, renameMode ? RenameSavedWaypointColor : SavedWaypointColor, SavedWaypointRadius);
            DrawGeometry(_geometrySystem.ActiveCurves, renameMode ? RenameRouteColor : RouteColor, RouteWidth);
            DrawNodes(_geometrySystem.ActiveNodes, renameMode ? RenameWaypointHaloColor : WaypointHaloColor, WaypointHaloRadius);
            DrawNodes(_geometrySystem.ActiveNodes, renameMode ? RenameWaypointColor : WaypointColor, WaypointRadius);
            DrawGeometry(_geometrySystem.PreviewCurves, renameMode ? RenamePreviewColor : PreviewColor, PreviewWidth);
            DrawNodes(_geometrySystem.PreviewNodes, renameMode ? RenameWaypointHaloColor : WaypointHaloColor, WaypointHaloRadius);
            DrawNodes(_geometrySystem.PreviewNodes, renameMode ? RenameWaypointColor : WaypointColor, WaypointRadius);
            DrawGeometry(_geometrySystem.HoverCurves, renameMode ? RenameHoverColor : HoverColor, HoverWidth);
            DrawWaypointInteractionState();

            if (_curveCommands.Length == 0 && _circleCommands.Length == 0)
                return;

            var curveCommands = _curveCommands.ToArray(Allocator.TempJob);
            var circleCommands = _circleCommands.ToArray(Allocator.TempJob);
            var buffer = _overlayRenderSystem.GetBuffer(out var bufferDependency);
            var drawHandle = new FlushOverlayCommandsJob
            {
                Buffer = buffer,
                CurveCommands = curveCommands,
                CircleCommands = circleCommands
            }.Schedule(JobHandle.CombineDependencies(Dependency, bufferDependency));

            _overlayRenderSystem.AddBufferWriter(drawHandle);
            var disposeCurvesHandle = curveCommands.Dispose(drawHandle);
            var disposeCirclesHandle = circleCommands.Dispose(drawHandle);
            Dependency = JobHandle.CombineDependencies(disposeCurvesHandle, disposeCirclesHandle);
        }

        private void DrawWaypointInteractionState()
        {
            var waypoints = _toolSystem.Waypoints;
            var hoveredIndex = _toolSystem.HoveredWaypointIndex;
            if (hoveredIndex >= 0 && waypoints != null && hoveredIndex < waypoints.Count)
            {
                var hoveredPosition = waypoints[hoveredIndex].Position;
                AddCircle(ActiveWaypointHaloColor, hoveredPosition, SnappedWaypointHaloRadius);
                AddCircle(ActiveWaypointColor, hoveredPosition, SnappedWaypointRadius);
            }

            var activeEditIndex = _toolSystem.ActiveEditIndex;
            if (_toolSystem.HasActiveWaypointEdit && activeEditIndex >= 0)
            {
                var activeWaypoints = _toolSystem.PreviewWaypoints;
                if (activeWaypoints != null && activeEditIndex < activeWaypoints.Count)
                {
                    var position = activeWaypoints[activeEditIndex].Position;
                    AddCircle(ActiveWaypointHaloColor, position, SnappedWaypointHaloRadius);
                    AddCircle(ActiveWaypointColor, position, SnappedWaypointRadius);
                }
            }

            var removalIndex = hoveredIndex;
            if (!_toolSystem.IsWaypointRemovalArmed || removalIndex < 0 || waypoints == null || removalIndex >= waypoints.Count)
                return;

            var removalPosition = waypoints[removalIndex].Position;
            AddCircle(RemoveWaypointHaloColor, removalPosition, SnappedWaypointHaloRadius);
            AddCircle(RemoveWaypointColor, removalPosition, SnappedWaypointRadius);
        }

        private void DrawManagedRoutes()
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

                    var renameMode = group.Mode == RoadRouteToolMode.RenameSelectedSegments;
                    var routeColor = renameMode
                        ? drawSelected ? RenameSelectedManagedRouteColor : RenameManagedRouteColor
                        : drawSelected ? SelectedManagedRouteColor : ManagedRouteColor;
                    var haloColor = renameMode
                        ? drawSelected ? RenameSelectedManagedWaypointHaloColor : RenameManagedWaypointHaloColor
                        : drawSelected ? SelectedManagedWaypointHaloColor : ManagedWaypointHaloColor;
                    var waypointColor = renameMode
                        ? drawSelected ? RenameSelectedManagedWaypointColor : RenameManagedWaypointColor
                        : drawSelected ? SelectedManagedWaypointColor : ManagedWaypointColor;
                    DrawGeometry(group.Curves, routeColor, drawSelected ? SelectedManagedRouteWidth : ManagedRouteWidth);
                    DrawNodes(group.Nodes, haloColor, drawSelected ? WaypointHaloRadius : SavedWaypointHaloRadius);
                    DrawNodes(group.Nodes, waypointColor, drawSelected ? WaypointRadius : SavedWaypointRadius);
                }
            }
        }

        private void DrawGeometry(System.Collections.Generic.IReadOnlyList<Bezier4x3> curves, Color lineColor, float lineWidth)
        {
            if (curves == null)
                return;

            for (var i = 0; i < curves.Count; i++)
            {
                _curveCommands.Add(new CurveDrawCommand
                {
                    Curve = curves[i],
                    Color = lineColor,
                    Width = lineWidth,
                    Roundness = RoundedLine
                });
            }
        }

        private void DrawNodes(System.Collections.Generic.IReadOnlyList<float3> nodes, Color color, float radius)
        {
            if (nodes == null)
                return;

            for (var i = 0; i < nodes.Count; i++)
                AddCircle(color, nodes[i], radius);
        }

        private void AddCircle(Color color, float3 position, float diameter)
        {
            _circleCommands.Add(new CircleDrawCommand
            {
                Position = position,
                Color = color,
                Diameter = diameter
            });
        }

        private struct CurveDrawCommand
        {
            public Bezier4x3 Curve;
            public Color Color;
            public float Width;
            public float2 Roundness;
        }

        private struct CircleDrawCommand
        {
            public float3 Position;
            public Color Color;
            public float Diameter;
        }

        [BurstCompile]
        private struct FlushOverlayCommandsJob : IJob
        {
            public OverlayRenderSystem.Buffer Buffer;

            [ReadOnly]
            public NativeArray<CurveDrawCommand> CurveCommands;

            [ReadOnly]
            public NativeArray<CircleDrawCommand> CircleCommands;

            public void Execute()
            {
                for (var i = 0; i < CurveCommands.Length; i++)
                {
                    var command = CurveCommands[i];
                    Buffer.DrawCurve(command.Color, command.Curve, command.Width, command.Roundness);
                }

                for (var i = 0; i < CircleCommands.Length; i++)
                {
                    var command = CircleCommands[i];
                    Buffer.DrawCircle(command.Color, command.Position, command.Diameter);
                }
            }
        }
    }
}
