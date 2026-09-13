using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Rendering;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteShieldUISystem : UISystemBase
    {
        private const string BindingGroup = "AdvancedRoadNaming";
        private const int MaxVisibleShields = 256;
        private const float MinCameraDistance = 45f;
        private const float MaxCameraDistance = 5000f;
        private const float LowAngleCameraDistance = 600f;
        private const float FullDistanceCameraAngle = 60f;
        private const float ShieldElevation = 4f;
        private const int MaxOcclusionChecksPerFrame = 32;
        private const float OcclusionCacheSeconds = 0.5f;
        private const float CameraSettleSeconds = 0.58f;
        private const float ZoomScaleStartDistance = 300f;
        private const float ZoomScaleEndDistance = 1200f;

        private readonly List<RouteShieldAnchor> _anchors = new List<RouteShieldAnchor>(256);
        private readonly List<VisibleRouteShield> _visible = new List<VisibleRouteShield>(128);
        private readonly List<Bezier4x3> _scratchCurves = new List<Bezier4x3>(256);
        private readonly List<float3> _scratchNodes = new List<float3>(32);
        private readonly List<OcclusionCandidate> _occlusionCandidates = new List<OcclusionCandidate>(256);
        private readonly List<int> _pendingOcclusionAnchors = new List<int>(MaxOcclusionChecksPerFrame);
        private Dictionary<int, OcclusionCacheEntry> _occlusionCache = new Dictionary<int, OcclusionCacheEntry>(256);
        private Dictionary<int, OcclusionCacheEntry> _retainedOcclusion = new Dictionary<int, OcclusionCacheEntry>();
        private readonly Dictionary<(long, int), RouteShieldAnchor> _previousAnchors = new Dictionary<(long, int), RouteShieldAnchor>();
        private readonly RouteGeometryCache _activeGeometry = new RouteGeometryCache();
        private readonly RouteCurveSampler _sampler = new RouteCurveSampler();
        private readonly List<float3> _anchorPositions = new List<float3>();
        private readonly List<VisibleRouteShield> _published = new List<VisibleRouteShield>();
        private RoadNetworkRevisionSystem _network;
        private long _lastNetworkRevision = -1;
        private (bool, long, RouteShieldStyle, string, string, long) _lastActiveRouteState;
        private int _nextAnchorId;
        private int _geometryGeneration;
        private int _pendingGeometryGeneration = -1;
        private bool _projectionDirty = true;
        private Matrix4x4 _lastViewMatrix;
        private Matrix4x4 _lastProjectionMatrix;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private float _lastCameraZoom;
        private RouteShieldSizePreset _lastSizePreset;
        private bool _lastOcclusionEnabled;
        private int _lastOcclusionAngle;
        private float _nextProjectionRefresh;
        private object _raycastContext = new object();

        private SegmentMetadataSystem _metadataSystem;
        private RoadRouteToolSystem _toolSystem;
        private RawValueBinding _overlayBinding;
        private RaycastSystem _raycastSystem;
        private CameraUpdateSystem _cameraUpdateSystem;
        private int _lastRouteDatabaseVersion = -1;
        private int _lastImportCatalogVersion = -1;
        private RouteShieldSpacingPreset _lastSpacingPreset = RouteShieldSpacingPreset.Moderate;
        private bool _lastEnabled;
        private int _raycastSubmittedFrame = -1;
        private int _cameraGeneration;
        private int _pendingCameraGeneration = -1;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private float _lastCameraMovementTime;
        private bool _hasCameraState;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<RoadRouteToolSystem>();
            _network = World.GetOrCreateSystemManaged<RoadNetworkRevisionSystem>();
            _raycastSystem = World.GetOrCreateSystemManaged<RaycastSystem>();
            _cameraUpdateSystem = World.GetExistingSystemManaged<CameraUpdateSystem>();
            AddBinding(_overlayBinding = new RawValueBinding(BindingGroup, "routeShieldOverlays", WriteOverlays));
        }

        protected override void OnUpdate()
        {
            ConsumeOcclusionResults();
            var enabled = Mod.Settings?.EnableRouteShields == true && _overlayBinding.active;
            if (!enabled)
            {
                if (_lastEnabled)
                {
                    _anchors.Clear();
                    _visible.Clear();
                    _occlusionCache.Clear();
                    _geometryGeneration++;
                    _activeGeometry.Clear();
                    PublishIfChanged();
                }
                _lastEnabled = false;
                return;
            }

            var spacingPreset = Mod.Settings.RouteShieldSpacingPreset;
            var databaseVersion = _metadataSystem.RouteDatabase.Version;
            var importVersion = RouteShieldImportCatalog.Version;
            var active = IsActiveRouteVisible();
            if (active)
                _activeGeometry.Update(EntityManager, _toolSystem.SelectedSegments, _toolSystem.Waypoints, _network.Revision);
            var activeState = (active, active ? _activeGeometry.Version : 0L, _toolSystem.RouteShieldStyle,
                _toolSystem.RouteShieldImportId ?? string.Empty, _toolSystem.InputText ?? string.Empty,
                _toolSystem.SavedRouteManipulateMode ? _toolSystem.SelectedSavedRouteId : 0L);
            if (!_lastEnabled || databaseVersion != _lastRouteDatabaseVersion || importVersion != _lastImportCatalogVersion
                || spacingPreset != _lastSpacingPreset || _network.Revision != _lastNetworkRevision
                || !_lastActiveRouteState.Equals(activeState))
            {
                RebuildAnchors(spacingPreset);
                _lastRouteDatabaseVersion = databaseVersion;
                _lastImportCatalogVersion = importVersion;
                _lastSpacingPreset = spacingPreset;
                _lastNetworkRevision = _network.Revision;
                _lastActiveRouteState = activeState;
                _lastEnabled = true;
                _projectionDirty = true;
            }
            ProjectVisibleAnchors();
            PublishIfChanged();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            _anchors.Clear();
            _visible.Clear();
            _activeGeometry.Clear();
            _occlusionCache.Clear();
            _retainedOcclusion.Clear();
            _previousAnchors.Clear();
            _pendingOcclusionAnchors.Clear();
            _raycastContext = new object();
            _raycastSubmittedFrame = -1;
            _pendingGeometryGeneration = -1;
            _geometryGeneration++;
            _lastEnabled = false;
            _hasCameraState = false;
            _projectionDirty = true;
            PublishIfChanged();
        }

        private bool IsActiveRouteVisible() => _toolSystem != null && _toolSystem.IsRunning
            && _toolSystem.Mode == RoadRouteToolMode.AssignMajorRouteNumber && !_toolSystem.SavedRoutesViewActive
            && _toolSystem.SelectedSegments.Count > 0
            && IsSupportedStyle(_toolSystem.RouteShieldStyle, _toolSystem.RouteShieldImportId);

        private void PublishIfChanged()
        {
            var changed = _published.Count != _visible.Count;
            for (var i = 0; !changed && i < _visible.Count; i++)
                changed = !_visible[i].Equals(_published[i]);
            if (!changed) return;
            _published.Clear();
            _published.AddRange(_visible);
            _overlayBinding.Update();
        }

        private void RebuildAnchors(RouteShieldSpacingPreset spacingPreset)
        {
            _previousAnchors.Clear();
            foreach (var anchor in _anchors)
                _previousAnchors[(anchor.RouteId, anchor.Ordinal)] = anchor;
            _anchors.Clear();
            _retainedOcclusion.Clear();
            var spacing = ResolveSpacing(spacingPreset);
            var active = IsActiveRouteVisible();
            // Prefer the edit preview, and do not also draw its saved copy.
            if (active)
            {
                var routeId = _toolSystem.SavedRouteManipulateMode ? _toolSystem.SelectedSavedRouteId : 0L;
                AddRouteAnchors(routeId, _toolSystem.RouteShieldStyle, _toolSystem.RouteShieldImportId,
                    BuildShieldLabel(_toolSystem.RouteShieldStyle, _toolSystem.InputText), spacing, _activeGeometry.Curves);
            }
            foreach (var route in _metadataSystem.RouteDatabase.Routes)
            {
                if (route == null || route.IsDeleted || route.Mode != RoadRouteToolMode.AssignMajorRouteNumber
                    || !IsSupportedStyle(route.RouteShieldStyle, route.RouteShieldImportId) || route.OrderedSegmentIds.Count == 0
                    || (active && _toolSystem.SavedRouteManipulateMode && route.RouteId == _toolSystem.SelectedSavedRouteId))
                    continue;
                RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, route.OrderedSegmentIds, route.Waypoints, _scratchCurves, _scratchNodes);
                AddRouteAnchors(route.RouteId, route.RouteShieldStyle, route.RouteShieldImportId,
                    BuildShieldLabel(route.RouteShieldStyle, route.RouteCode ?? route.BaseInputValue), spacing, _scratchCurves);
            }
            var oldCache = _occlusionCache;
            _occlusionCache = _retainedOcclusion;
            _retainedOcclusion = oldCache;
            _retainedOcclusion.Clear();
            _previousAnchors.Clear();
            _geometryGeneration++;
        }

        private void AddRouteAnchors(long routeId, RouteShieldStyle style, string importId, string label,
            float spacing, IReadOnlyList<Bezier4x3> curves)
        {
            if (string.IsNullOrWhiteSpace(label) || curves.Count == 0) return;
            _sampler.Rebuild(curves);
            _sampler.Place(spacing, _anchorPositions);
            for (var i = 0; i < _anchorPositions.Count; i++)
            {
                var position = _anchorPositions[i];
                position.y += ShieldElevation;
                var id = _previousAnchors.TryGetValue((routeId, i), out var previous) ? previous.Id : ++_nextAnchorId;
                if (math.distancesq(previous.Position, position) < 0.000001f
                    && _occlusionCache.TryGetValue(id, out var cached))
                    _retainedOcclusion[id] = cached;
                _anchors.Add(new RouteShieldAnchor(id, routeId, i, NormalizeStyle(style), importId, label, position));
            }
        }

        private void ProjectVisibleAnchors()
        {
            var camera = Camera.main;
            if (camera == null || _anchors.Count == 0)
            {
                _visible.Clear();
                return;
            }

            var cameraPosition = camera.transform.position;
            var cameraZoom = _cameraUpdateSystem?.activeCameraController?.zoom
                ?? _cameraUpdateSystem?.zoom
                ?? cameraPosition.y;
            if (cameraZoom <= 0f)
                cameraZoom = cameraPosition.y;
            var cameraAngle = math.degrees(math.asin(math.clamp(math.abs(camera.transform.forward.y), 0f, 1f)));
            var maximumDistance = ResolveMaximumDistance(cameraAngle);
            var cameraStable = UpdateCameraStability(camera);
            var occlusionEnabled = Mod.Settings?.EnableRouteShieldOcclusion == true;
            var angleThreshold = math.clamp(Mod.Settings?.RouteShieldOcclusionAngle ?? 30, 5, 60);
            var shouldCheckOcclusion = occlusionEnabled && cameraStable && cameraAngle <= angleThreshold;

            var screenHeight = Screen.height;
            var screenWidth = Screen.width;
            var sizePreset = Mod.Settings.RouteShieldSizePreset;
            var viewMatrix = camera.worldToCameraMatrix;
            var projectionMatrix = camera.projectionMatrix;
            var now = UnityEngine.Time.unscaledTime;
            if (!_projectionDirty && _lastViewMatrix.Equals(viewMatrix) && _lastProjectionMatrix.Equals(projectionMatrix)
                && _lastScreenWidth == screenWidth && _lastScreenHeight == screenHeight && _lastCameraZoom == cameraZoom
                && _lastSizePreset == sizePreset && _lastOcclusionEnabled == occlusionEnabled && _lastOcclusionAngle == angleThreshold
                && now < _nextProjectionRefresh)
                return;
            _projectionDirty = false;
            _lastViewMatrix = viewMatrix;
            _lastProjectionMatrix = projectionMatrix;
            _lastScreenWidth = screenWidth;
            _lastScreenHeight = screenHeight;
            _lastCameraZoom = cameraZoom;
            _lastSizePreset = sizePreset;
            _lastOcclusionEnabled = occlusionEnabled;
            _lastOcclusionAngle = angleThreshold;
            _nextProjectionRefresh = occlusionEnabled && cameraAngle <= angleThreshold
                ? (cameraStable ? now + OcclusionCacheSeconds : _lastCameraMovementTime + CameraSettleSeconds)
                : float.PositiveInfinity;
            _visible.Clear();
            _occlusionCandidates.Clear();
            for (var i = 0; i < _anchors.Count && _visible.Count < MaxVisibleShields; i++)
            {
                var anchor = _anchors[i];
                var worldPosition = new Vector3(anchor.Position.x, anchor.Position.y, anchor.Position.z);
                var distance = Vector3.Distance(cameraPosition, worldPosition);
                if (distance < MinCameraDistance || distance > maximumDistance)
                    continue;

                var point = camera.WorldToScreenPoint(worldPosition);
                if (point.z <= 0f || point.x < -80f || point.x > screenWidth + 80f || point.y < -80f || point.y > screenHeight + 80f)
                    continue;

                var hasCachedResult = _occlusionCache.TryGetValue(anchor.Id, out var cached)
                    && cached.CameraGeneration == _cameraGeneration;
                var occluded = occlusionEnabled && cameraAngle <= angleThreshold && (!hasCachedResult || cached.Occluded);
                _visible.Add(new VisibleRouteShield(
                    anchor.Id,
                    BuildShieldSelection(anchor.Style, anchor.ImportId),
                    anchor.Label,
                    point.x,
                    screenHeight - point.y,
                    ResolveSizeScale(Mod.Settings?.RouteShieldSizePreset ?? RouteShieldSizePreset.Medium, cameraZoom),
                    occluded));

                if (shouldCheckOcclusion
                    && (!hasCachedResult || UnityEngine.Time.unscaledTime - cached.CheckedAt >= OcclusionCacheSeconds))
                {
                    _occlusionCandidates.Add(new OcclusionCandidate(i, distance));
                }
            }

            if (shouldCheckOcclusion)
                SubmitOcclusionChecks(cameraPosition);
        }

        private void WriteOverlays(IJsonWriter writer)
        {
            writer.ArrayBegin(_visible.Count);
            for (var i = 0; i < _visible.Count; i++)
            {
                var item = _visible[i];
                writer.TypeBegin("AdvancedRoadNaming.RouteShieldOverlay");
                writer.PropertyName("id");
                writer.Write(item.Id);
                writer.PropertyName("style");
                writer.Write(item.Style);
                writer.PropertyName("label");
                writer.Write(item.Label);
                writer.PropertyName("left");
                writer.Write(item.Left);
                writer.PropertyName("top");
                writer.Write(item.Top);
                writer.PropertyName("scale");
                writer.Write(item.Scale);
                writer.PropertyName("occluded");
                writer.Write(item.Occluded);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static bool IsSupportedStyle(RouteShieldStyle style, string importId)
        {
            return (style == RouteShieldStyle.Imported && RouteShieldImportCatalog.Contains(importId))
                || style == RouteShieldStyle.AustralianMRectangle
                || style == RouteShieldStyle.AustralianARectangle
                || style == RouteShieldStyle.AustralianBRectangle
                || style == RouteShieldStyle.AustralianCRectangle
                || style == RouteShieldStyle.AustralianNationalShield
                || style == RouteShieldStyle.BlueHighwayShield
                || style == RouteShieldStyle.BlackWhiteShield
                || (style >= RouteShieldStyle.USInterstate && style <= RouteShieldStyle.NewZealandStateHighway)
                || style == RouteShieldStyle.GermanyFederalRoad
                || style == RouteShieldStyle.ThailandHighway
                || style == RouteShieldStyle.ThailandMotorwayBlue
                || style == RouteShieldStyle.ThailandMotorwayGreen;
        }

        private static RouteShieldStyle NormalizeStyle(RouteShieldStyle style)
        {
            return style == RouteShieldStyle.AustralianMRectangle
                || style == RouteShieldStyle.AustralianBRectangle
                || style == RouteShieldStyle.AustralianCRectangle
                ? RouteShieldStyle.AustralianARectangle
                : style;
        }

        private static string BuildShieldLabel(RouteShieldStyle style, string code)
        {
            return (code ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string BuildShieldSelection(RouteShieldStyle style, string importId)
        {
            return style == RouteShieldStyle.Imported
                ? RouteShieldImportCatalog.SelectionValue(importId)
                : style.ToString();
        }

        private static float ResolveSpacing(RouteShieldSpacingPreset preset)
        {
            switch (preset)
            {
                case RouteShieldSpacingPreset.Frequent:
                    return 150f;
                case RouteShieldSpacingPreset.Sparse:
                    return 600f;
                default:
                    return 300f;
            }
        }

        private static float ResolveSizeScale(RouteShieldSizePreset preset, float cameraZoom)
        {
            if (preset == RouteShieldSizePreset.VeryLarge)
                return 1.5f;

            float baseScale;
            switch (preset)
            {
                case RouteShieldSizePreset.Small:
                    baseScale = 0.8f;
                    break;
                case RouteShieldSizePreset.Large:
                    baseScale = 1.25f;
                    break;
                default:
                    baseScale = 1f;
                    break;
            }

            var zoomProgress = math.saturate((cameraZoom - ZoomScaleStartDistance) / (ZoomScaleEndDistance - ZoomScaleStartDistance));
            zoomProgress = zoomProgress * zoomProgress * (3f - 2f * zoomProgress);
            return baseScale * math.lerp(1f, 0.6f, zoomProgress);
        }

        private static float ResolveMaximumDistance(float cameraAngle)
        {
            var progress = math.saturate(cameraAngle / FullDistanceCameraAngle);
            progress = progress * progress * (3f - 2f * progress);
            return math.lerp(LowAngleCameraDistance, MaxCameraDistance, progress);
        }

        private bool UpdateCameraStability(Camera camera)
        {
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            if (!_hasCameraState
                || Vector3.SqrMagnitude(position - _lastCameraPosition) > 0.0025f
                || Quaternion.Angle(rotation, _lastCameraRotation) > 0.05f)
            {
                _lastCameraMovementTime = UnityEngine.Time.unscaledTime;
                _lastCameraPosition = position;
                _lastCameraRotation = rotation;
                _hasCameraState = true;
                _cameraGeneration++;
            }

            return UnityEngine.Time.unscaledTime - _lastCameraMovementTime >= CameraSettleSeconds;
        }

        private void SubmitOcclusionChecks(Vector3 cameraPosition)
        {
            if (_raycastSystem == null || _pendingOcclusionAnchors.Count != 0 || _occlusionCandidates.Count == 0)
                return;

            _occlusionCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var count = math.min(MaxOcclusionChecksPerFrame, _occlusionCandidates.Count);
            for (var i = 0; i < count; i++)
            {
                var anchorIndex = _occlusionCandidates[i].AnchorIndex;
                var anchor = _anchors[anchorIndex];
                _pendingOcclusionAnchors.Add(anchorIndex);
                _raycastSystem.AddInput(_raycastContext, new RaycastInput
                {
                    m_Line = new Line3.Segment((float3)cameraPosition, anchor.Position),
                    m_Owner = Entity.Null,
                    m_TypeMask = TypeMask.StaticObjects,
                    m_CollisionMask = CollisionMask.OnGround | CollisionMask.Overground,
                });
            }

            _pendingCameraGeneration = _cameraGeneration;
            _pendingGeometryGeneration = _geometryGeneration;
            _raycastSubmittedFrame = UnityEngine.Time.frameCount;
        }

        private void ConsumeOcclusionResults()
        {
            if (_raycastSystem == null || _pendingOcclusionAnchors.Count == 0 || UnityEngine.Time.frameCount <= _raycastSubmittedFrame)
                return;

            NativeArray<RaycastResult> results = _raycastSystem.GetResult(_raycastContext);
            if (!results.IsCreated || results.Length != _pendingOcclusionAnchors.Count)
            {
                // Results are per-frame, not a persistent request queue. Recover if a load
                // or skipped raycast pass dropped this batch rather than stalling forever.
                if (UnityEngine.Time.frameCount - _raycastSubmittedFrame > 8)
                {
                    _pendingOcclusionAnchors.Clear();
                    _raycastSubmittedFrame = -1;
                    _projectionDirty = true;
                }
                return;
            }

            var acceptResults = _pendingCameraGeneration == _cameraGeneration && _pendingGeometryGeneration == _geometryGeneration;
            for (var i = 0; i < results.Length; i++)
            {
                if (!acceptResults)
                    continue;

                var result = results[i];
                var hitEntity = result.m_Hit.m_HitEntity;
                var reportedOwner = result.m_Owner;
                var hasHit = (hitEntity != Entity.Null || reportedOwner != Entity.Null)
                    && result.m_Hit.m_NormalizedDistance < 0.995f;
                var occluded = hasHit && IsBuilding(hitEntity, reportedOwner);
                _projectionDirty = true;
                _occlusionCache[_anchors[_pendingOcclusionAnchors[i]].Id] = new OcclusionCacheEntry(occluded, UnityEngine.Time.unscaledTime, _cameraGeneration);
            }

            _pendingOcclusionAnchors.Clear();
            _projectionDirty = true;
            _raycastSubmittedFrame = -1;
            _pendingCameraGeneration = -1;
        }

        private bool IsBuilding(Entity hitEntity, Entity reportedOwner)
        {
            return IsBuildingOrOwnedByBuilding(reportedOwner) || IsBuildingOrOwnedByBuilding(hitEntity);
        }

        private bool IsBuildingOrOwnedByBuilding(Entity entity)
        {
            for (var depth = 0; depth < 4 && entity != Entity.Null; depth++)
            {
                if (EntityManager.HasComponent<Building>(entity) || EntityManager.HasComponent<Extension>(entity))
                    return true;
                if (!EntityManager.HasComponent<Owner>(entity))
                    break;
                var owner = EntityManager.GetComponentData<Owner>(entity);
                if (owner.m_Owner == entity)
                    break;
                entity = owner.m_Owner;
            }

            return false;
        }

        private readonly struct RouteShieldAnchor
        {
            public RouteShieldAnchor(int id, long routeId, int ordinal, RouteShieldStyle style, string importId, string label, float3 position)
            {
                Id = id;
                RouteId = routeId;
                Ordinal = ordinal;
                Style = style;
                ImportId = importId ?? string.Empty;
                Label = label;
                Position = position;
            }

            public int Id { get; }
            public long RouteId { get; }
            public int Ordinal { get; }
            public RouteShieldStyle Style { get; }
            public string ImportId { get; }
            public string Label { get; }
            public float3 Position { get; }
        }

        private readonly struct VisibleRouteShield : IEquatable<VisibleRouteShield>
        {
            public VisibleRouteShield(int id, string style, string label, float left, float top, float scale, bool occluded)
            {
                Id = id;
                Style = style;
                Label = label;
                Left = left;
                Top = top;
                Scale = scale;
                Occluded = occluded;
            }

            public int Id { get; }
            public string Style { get; }
            public string Label { get; }
            public float Left { get; }
            public float Top { get; }
            public float Scale { get; }
            public bool Occluded { get; }
            public bool Equals(VisibleRouteShield other) => Id == other.Id && Style == other.Style && Label == other.Label
                && Left == other.Left && Top == other.Top && Scale == other.Scale && Occluded == other.Occluded;
        }

        private readonly struct OcclusionCandidate
        {
            public OcclusionCandidate(int anchorIndex, float distance)
            {
                AnchorIndex = anchorIndex;
                Distance = distance;
            }

            public int AnchorIndex { get; }
            public float Distance { get; }
        }

        private readonly struct OcclusionCacheEntry
        {
            public OcclusionCacheEntry(bool occluded, float checkedAt, int cameraGeneration)
            {
                Occluded = occluded;
                CheckedAt = checkedAt;
                CameraGeneration = cameraGeneration;
            }

            public bool Occluded { get; }
            public float CheckedAt { get; }
            public int CameraGeneration { get; }
        }
    }
}
