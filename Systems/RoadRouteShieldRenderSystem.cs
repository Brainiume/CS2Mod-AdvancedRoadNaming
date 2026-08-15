using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Colossal.Mathematics;
using Colossal.UI.Binding;
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
        private const int GeometryRefreshIntervalFrames = 128;
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
        private readonly Dictionary<int, OcclusionCacheEntry> _occlusionCache = new Dictionary<int, OcclusionCacheEntry>(256);
        private readonly object _raycastContext = new object();

        private SegmentMetadataSystem _metadataSystem;
        private RoadRouteToolSystem _toolSystem;
        private RawValueBinding _overlayBinding;
        private RaycastSystem _raycastSystem;
        private CameraUpdateSystem _cameraUpdateSystem;
        private int _lastRouteDatabaseVersion = -1;
        private int _lastImportCatalogVersion = -1;
        private RouteShieldSpacingPreset _lastSpacingPreset = RouteShieldSpacingPreset.Moderate;
        private int _lastActiveRouteSignature;
        private bool _lastEnabled;
        private int _refreshCountdown;
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
            _raycastSystem = World.GetOrCreateSystemManaged<RaycastSystem>();
            _cameraUpdateSystem = World.GetExistingSystemManaged<CameraUpdateSystem>();
            AddBinding(_overlayBinding = new RawValueBinding(BindingGroup, "routeShieldOverlays", WriteOverlays));
        }

        protected override void OnUpdate()
        {
            var enabled = Mod.Settings?.EnableRouteShields == true;
            var spacingPreset = Mod.Settings?.RouteShieldSpacingPreset ?? RouteShieldSpacingPreset.Moderate;
            var databaseVersion = _metadataSystem?.RouteDatabase?.Version ?? -1;
            var importCatalogVersion = RouteShieldImportCatalog.Version;
            var activeRouteSignature = BuildActiveRouteSignature();

            _refreshCountdown--;
            if (!enabled)
            {
                if (_anchors.Count != 0 || _visible.Count != 0)
                {
                    _anchors.Clear();
                    _visible.Clear();
                    ClearOcclusionState();
                    _overlayBinding.Update();
                }

                _lastEnabled = false;
                _lastRouteDatabaseVersion = databaseVersion;
                _lastImportCatalogVersion = importCatalogVersion;
                _lastSpacingPreset = spacingPreset;
                _lastActiveRouteSignature = activeRouteSignature;
                return;
            }

            var geometryChanged = !_lastEnabled
                || _lastRouteDatabaseVersion != databaseVersion
                || _lastImportCatalogVersion != importCatalogVersion
                || _lastSpacingPreset != spacingPreset
                || _lastActiveRouteSignature != activeRouteSignature
                || _refreshCountdown <= 0;
            if (geometryChanged)
            {
                RebuildAnchors(spacingPreset);
                ClearOcclusionState();
                _lastEnabled = true;
                _lastRouteDatabaseVersion = databaseVersion;
                _lastImportCatalogVersion = importCatalogVersion;
                _lastSpacingPreset = spacingPreset;
                _lastActiveRouteSignature = activeRouteSignature;
                _refreshCountdown = GeometryRefreshIntervalFrames;
            }

            ConsumeOcclusionResults();
            ProjectVisibleAnchors();
            _overlayBinding.Update();
        }

        private int BuildActiveRouteSignature()
        {
            if (_toolSystem == null
                || !_toolSystem.IsRunning
                || _toolSystem.Mode != RoadRouteToolMode.AssignMajorRouteNumber
                || _toolSystem.SavedRoutesViewActive
                || !IsSupportedStyle(_toolSystem.RouteShieldStyle, _toolSystem.RouteShieldImportId))
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _toolSystem.SelectedSegments.Count;
                hash = hash * 31 + _toolSystem.WaypointCount;
                hash = hash * 31 + (int)_toolSystem.RouteShieldStyle;
                hash = hash * 31 + (_toolSystem.RouteShieldImportId ?? string.Empty).GetHashCode();
                hash = hash * 31 + (_toolSystem.InputText ?? string.Empty).GetHashCode();
                return hash;
            }
        }

        private void RebuildAnchors(RouteShieldSpacingPreset spacingPreset)
        {
            _anchors.Clear();
            if (_metadataSystem == null)
                return;

            var spacing = ResolveSpacing(spacingPreset);
            foreach (var route in _metadataSystem.RouteDatabase.Routes)
            {
                if (route == null
                    || route.IsDeleted
                    || route.Mode != RoadRouteToolMode.AssignMajorRouteNumber
                    || !IsSupportedStyle(route.RouteShieldStyle, route.RouteShieldImportId)
                    || route.OrderedSegmentIds.Count == 0)
                {
                    continue;
                }

                RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, route.OrderedSegmentIds, route.Waypoints, _scratchCurves, _scratchNodes);
                if (_scratchCurves.Count == 0)
                    continue;

                AddRouteAnchors(route.RouteShieldStyle, route.RouteShieldImportId, BuildShieldLabel(route.RouteShieldStyle, route.RouteCode ?? route.BaseInputValue), spacing);
            }

            AddActiveRouteAnchors(spacing);
        }

        private void AddActiveRouteAnchors(float spacing)
        {
            if (_toolSystem == null
                || !_toolSystem.IsRunning
                || _toolSystem.Mode != RoadRouteToolMode.AssignMajorRouteNumber
                || _toolSystem.SavedRoutesViewActive
                || !IsSupportedStyle(_toolSystem.RouteShieldStyle, _toolSystem.RouteShieldImportId)
                || _toolSystem.SelectedSegments.Count == 0)
            {
                return;
            }

            RouteOverlayGeometryBuilder.BuildRouteGeometry(EntityManager, _toolSystem.SelectedSegments, _toolSystem.Waypoints, _scratchCurves, _scratchNodes);
            if (_scratchCurves.Count == 0)
                return;

            AddRouteAnchors(
                _toolSystem.RouteShieldStyle,
                _toolSystem.RouteShieldImportId,
                BuildShieldLabel(_toolSystem.RouteShieldStyle, _toolSystem.InputText),
                spacing);
        }

        private void AddRouteAnchors(RouteShieldStyle style, string importId, string label, float spacing)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            var totalLength = 0f;
            for (var i = 0; i < _scratchCurves.Count; i++)
                totalLength += RouteOverlayMath.ApproximateLength(_scratchCurves[i]);

            if (totalLength < 5f)
                return;

            if (totalLength < spacing * 0.75f)
            {
                AddAnchorAtDistance(style, importId, label, totalLength * 0.5f);
                return;
            }

            for (var distance = spacing * 0.5f; distance < totalLength; distance += spacing)
                AddAnchorAtDistance(style, importId, label, distance);
        }

        private void AddAnchorAtDistance(RouteShieldStyle style, string importId, string label, float targetDistance)
        {
            var walked = 0f;
            for (var i = 0; i < _scratchCurves.Count; i++)
            {
                var curve = _scratchCurves[i];
                var length = RouteOverlayMath.ApproximateLength(curve);
                if (walked + length < targetDistance)
                {
                    walked += length;
                    continue;
                }

                var localDistance = math.clamp(targetDistance - walked, 0f, length);
                var t = RouteOverlayMath.ParameterAtDistance(curve, localDistance);
                var position = MathUtils.Position(curve, t);
                position.y += ShieldElevation;
                _anchors.Add(new RouteShieldAnchor(NormalizeStyle(style), importId, label, position));
                return;
            }
        }

        private void ProjectVisibleAnchors()
        {
            _visible.Clear();
            var camera = Camera.main;
            if (camera == null || _anchors.Count == 0)
                return;

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

            _occlusionCandidates.Clear();
            var screenHeight = Screen.height;
            var screenWidth = Screen.width;
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

                var hasCachedResult = _occlusionCache.TryGetValue(i, out var cached)
                    && cached.CameraGeneration == _cameraGeneration;
                var occluded = occlusionEnabled && cameraAngle <= angleThreshold && (!hasCachedResult || cached.Occluded);
                _visible.Add(new VisibleRouteShield(
                    i,
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
                || (style >= RouteShieldStyle.USInterstate && style <= RouteShieldStyle.NewZealandStateHighway);
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
            _raycastSubmittedFrame = UnityEngine.Time.frameCount;
        }

        private void ConsumeOcclusionResults()
        {
            if (_raycastSystem == null || _pendingOcclusionAnchors.Count == 0 || UnityEngine.Time.frameCount <= _raycastSubmittedFrame)
                return;

            NativeArray<RaycastResult> results = _raycastSystem.GetResult(_raycastContext);
            if (!results.IsCreated || results.Length != _pendingOcclusionAnchors.Count)
                return;

            var acceptResults = _pendingCameraGeneration == _cameraGeneration;
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
                _occlusionCache[_pendingOcclusionAnchors[i]] = new OcclusionCacheEntry(occluded, UnityEngine.Time.unscaledTime, _cameraGeneration);
            }

            _pendingOcclusionAnchors.Clear();
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

        private void ClearOcclusionState()
        {
            _occlusionCache.Clear();
            _pendingOcclusionAnchors.Clear();
            _raycastSubmittedFrame = -1;
            _pendingCameraGeneration = -1;
        }

        private readonly struct RouteShieldAnchor
        {
            public RouteShieldAnchor(RouteShieldStyle style, string importId, string label, float3 position)
            {
                Style = style;
                ImportId = importId ?? string.Empty;
                Label = label;
                Position = position;
            }

            public RouteShieldStyle Style { get; }
            public string ImportId { get; }
            public string Label { get; }
            public float3 Position { get; }
        }

        private readonly struct VisibleRouteShield
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
