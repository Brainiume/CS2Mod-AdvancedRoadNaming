using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Unity.Entities;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteOverlayGeometrySystem : GameSystemBase
    {
        private const int MaxPooledManagedRouteGroups = 64;
        private const int MaxPooledGroupGeometryItems = 4096;

        private readonly List<Bezier4x3> _activeCurves = new List<Bezier4x3>();
        private readonly List<float3> _activeNodes = new List<float3>();
        private readonly List<Bezier4x3> _previewCurves = new List<Bezier4x3>();
        private readonly List<float3> _previewNodes = new List<float3>();
        private readonly List<Bezier4x3> _savedRouteCurves = new List<Bezier4x3>();
        private readonly List<float3> _savedRouteNodes = new List<float3>();
        private readonly List<Bezier4x3> _hoverCurves = new List<Bezier4x3>();
        private readonly List<RouteOverlayGeometryGroup> _managedRouteGroups = new List<RouteOverlayGeometryGroup>();
        private readonly List<RouteOverlayGeometryGroup> _managedRouteGroupPool = new List<RouteOverlayGeometryGroup>();

        private RoadRouteToolSystem _toolSystem;
        private SegmentMetadataSystem _metadataSystem;
        private int _lastManageOverlayVersion = -1;

        public IReadOnlyList<Bezier4x3> ActiveCurves => _activeCurves;
        public IReadOnlyList<float3> ActiveNodes => _activeNodes;
        public IReadOnlyList<Bezier4x3> PreviewCurves => _previewCurves;
        public IReadOnlyList<float3> PreviewNodes => _previewNodes;
        public IReadOnlyList<Bezier4x3> SavedRouteCurves => _savedRouteCurves;
        public IReadOnlyList<float3> SavedRouteNodes => _savedRouteNodes;
        public IReadOnlyList<Bezier4x3> HoverCurves => _hoverCurves;
        public IReadOnlyList<RouteOverlayGeometryGroup> ManagedRouteGroups => _managedRouteGroups;

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
        }

        protected override void OnUpdate()
        {
            if (_toolSystem == null || !_toolSystem.IsRunning)
            {
                ClearAll();
                return;
            }

            BuildManagedRouteGeometry();
            RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, _toolSystem.SavedRoutePreviewSegments, _toolSystem.SavedRoutePreviewWaypoints, _savedRouteCurves, _savedRouteNodes);
            RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, _toolSystem.SelectedSegments, _toolSystem.Waypoints, _activeCurves, _activeNodes);
            HideActiveWaypointBeingMoved();

            BuildPreviewGeometry();
            BuildHoverGeometry();
        }

        private void BuildManagedRouteGeometry()
        {
            var manageOverlayActive = _toolSystem != null && (_toolSystem.SavedRoutesViewActive || _toolSystem.SavedRouteManipulateMode);
            if (!manageOverlayActive || _metadataSystem == null)
            {
                if (_managedRouteGroups.Count > 0)
                    ClearManagedRouteGroups();
                ClearManagedRouteGroupPool();
                _lastManageOverlayVersion = -1;
                return;
            }

            if (_lastManageOverlayVersion == _toolSystem.ManageOverlayVersion)
                return;

            ClearManagedRouteGroups();
            _lastManageOverlayVersion = _toolSystem.ManageOverlayVersion;

            foreach (var route in _metadataSystem.RouteDatabase.Routes)
            {
                if (route == null || route.IsDeleted)
                    continue;

                if (_toolSystem.SavedRouteManipulateMode && route.RouteId == _toolSystem.SelectedSavedRouteId)
                    continue;

                var group = RentManagedRouteGroup(route.RouteId, !_toolSystem.SavedRouteManipulateMode && route.RouteId == _toolSystem.SelectedSavedRouteId);

                RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, route.OrderedSegmentIds, route.Waypoints, group.Curves, group.Nodes);
                if (group.Curves.Count == 0 && group.Nodes.Count == 0)
                {
                    ReturnManagedRouteGroup(group);
                    continue;
                }

                _managedRouteGroups.Add(group);
            }
        }

        private RouteOverlayGeometryGroup RentManagedRouteGroup(long routeId, bool selected)
        {
            RouteOverlayGeometryGroup group;
            if (_managedRouteGroupPool.Count == 0)
            {
                group = new RouteOverlayGeometryGroup();
            }
            else
            {
                var index = _managedRouteGroupPool.Count - 1;
                group = _managedRouteGroupPool[index];
                _managedRouteGroupPool.RemoveAt(index);
            }

            group.Clear();
            group.RouteId = routeId;
            group.Selected = selected;
            return group;
        }

        private void ReturnManagedRouteGroup(RouteOverlayGeometryGroup group)
        {
            if (group == null)
                return;

            if (_managedRouteGroupPool.Count >= MaxPooledManagedRouteGroups
                || group.Curves.Count > MaxPooledGroupGeometryItems
                || group.Nodes.Count > MaxPooledGroupGeometryItems)
            {
                group.Clear();
                return;
            }

            group.Clear();
            _managedRouteGroupPool.Add(group);
        }

        private void ClearManagedRouteGroups()
        {
            for (var i = 0; i < _managedRouteGroups.Count; i++)
                ReturnManagedRouteGroup(_managedRouteGroups[i]);

            _managedRouteGroups.Clear();
        }

        private void ClearManagedRouteGroupPool()
        {
            for (var i = 0; i < _managedRouteGroupPool.Count; i++)
                _managedRouteGroupPool[i]?.Clear();

            _managedRouteGroupPool.Clear();
        }

        private void HideActiveWaypointBeingMoved()
        {
            if (!_toolSystem.HasActiveMoveEdit)
                return;

            var activeEditIndex = _toolSystem.ActiveEditIndex;
            if (activeEditIndex < 0 || activeEditIndex >= _activeNodes.Count)
                return;

            _activeNodes.RemoveAt(activeEditIndex);
        }

        private void BuildPreviewGeometry()
        {
            _previewCurves.Clear();
            _previewNodes.Clear();

            var previewSegments = _toolSystem.PreviewSegments;
            if (previewSegments == null || previewSegments.Count == 0)
                return;

            RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, previewSegments, _toolSystem.PreviewWaypoints, _previewCurves, _previewNodes);
        }

        private void BuildHoverGeometry()
        {
            _hoverCurves.Clear();
            if (_toolSystem.HoveredSegment == Entity.Null)
                return;

            var previewSegments = _toolSystem.PreviewSegments;
            if (previewSegments != null && previewSegments.Count > 0)
                return;

            RouteOverlayGeometryBuilder.BuildHoverGeometry(EntityManager, _toolSystem.HoveredSegment, _hoverCurves);
        }

        private void ClearAll()
        {
            _activeCurves.Clear();
            _activeNodes.Clear();
            _previewCurves.Clear();
            _previewNodes.Clear();
            _savedRouteCurves.Clear();
            _savedRouteNodes.Clear();
            _hoverCurves.Clear();
            ClearManagedRouteGroups();
            ClearManagedRouteGroupPool();
            _lastManageOverlayVersion = -1;
        }
    }
}
