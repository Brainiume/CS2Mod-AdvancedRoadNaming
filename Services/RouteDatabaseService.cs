using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using Unity.Entities;

namespace AdvancedRoadNaming.Services
{
    public sealed class RouteDatabaseService
    {
        private readonly List<SavedRouteRecord> _routes = new List<SavedRouteRecord>();
        private long _nextRouteId = 1;

        public IReadOnlyList<SavedRouteRecord> Routes => _routes;

        public int Count => _routes.Count;

        public long NextRouteId
        {
            get => _nextRouteId;
            set => _nextRouteId = value < 1 ? 1 : value;
        }

        public void Clear()
        {
            for (var i = 0; i < _routes.Count; i++)
                _routes[i]?.ClearStoredData();

            _routes.Clear();
            _nextRouteId = 1;
        }

        public SavedRouteRecord CreateRoute(string title, RoadRouteToolMode mode, string inputValue, RouteNumberPlacement placement, IReadOnlyList<RoadRouteWaypoint> waypoints, IReadOnlyList<Entity> segments, IReadOnlyList<string> streetNames)
        {
            var now = DateTime.UtcNow.Ticks;
            var route = new SavedRouteRecord
            {
                RouteId = _nextRouteId++,
                DisplayTitle = string.IsNullOrWhiteSpace(title) ? BuildDefaultTitle(mode, inputValue) : title.Trim(),
                Mode = mode,
                BaseInputValue = inputValue?.Trim() ?? string.Empty,
                RouteCode = inputValue?.Trim() ?? string.Empty,
                RoutePrefixType = ResolveRoutePrefixType(inputValue),
                RouteNumberPlacement = placement,
                CreatedAtUtcTicks = now,
                UpdatedAtUtcTicks = now,
                LastAppliedUtcTicks = now
            };

            CopyRouteGeometry(route, waypoints, segments, streetNames);
            PopulateRouteIntentMetadata(route);
            _routes.Add(route);
            return route;
        }

        public bool TryGet(long routeId, out SavedRouteRecord route)
        {
            for (var i = 0; i < _routes.Count; i++)
            {
                if (_routes[i].RouteId == routeId)
                {
                    route = _routes[i];
                    return true;
                }
            }

            route = null;
            return false;
        }

        public bool Delete(long routeId, bool clearStoredData = true)
        {
            for (var i = 0; i < _routes.Count; i++)
            {
                if (_routes[i].RouteId == routeId)
                {
                    if (clearStoredData)
                        _routes[i].ClearStoredData();

                    _routes.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public int DeleteByMode(RoadRouteToolMode mode)
        {
            var removed = 0;
            for (var i = _routes.Count - 1; i >= 0; i--)
            {
                var route = _routes[i];
                if (route == null || route.Mode != mode)
                    continue;

                route.ClearStoredData();
                _routes.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        public bool Rename(long routeId, string title)
        {
            if (!TryGet(routeId, out var route) || string.IsNullOrWhiteSpace(title))
                return false;

            route.DisplayTitle = title.Trim();
            route.IsUserDefinedTitle = true;
            route.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
            return true;
        }

        public bool UpdateInput(long routeId, string inputValue)
        {
            if (!TryGet(routeId, out var route))
                return false;

            route.BaseInputValue = inputValue?.Trim() ?? string.Empty;
            route.RouteCode = route.BaseInputValue;
            route.RoutePrefixType = ResolveRoutePrefixType(route.BaseInputValue);
            route.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
            return true;
        }

        public bool UpdatePlacement(long routeId, RouteNumberPlacement placement)
        {
            if (!TryGet(routeId, out var route))
                return false;

            route.RouteNumberPlacement = placement;
            route.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
            return true;
        }

        public void ReplaceRouteIntent(SavedRouteRecord route, RouteNumberPlacement placement, IReadOnlyList<RoadRouteWaypoint> waypoints, IReadOnlyList<Entity> segments, IReadOnlyList<string> streetNames)
        {
            route.Waypoints.Clear();
            if (waypoints != null)
            {
                for (var i = 0; i < waypoints.Count; i++)
                    route.Waypoints.Add(waypoints[i]);
            }

            route.OrderedSegmentIds.Clear();
            route.OriginalStreetNamesSnapshot.Clear();
            for (var i = 0; i < segments.Count; i++)
            {
                route.OrderedSegmentIds.Add(segments[i]);
                route.OriginalStreetNamesSnapshot.Add(i < streetNames.Count ? streetNames[i] ?? string.Empty : string.Empty);
            }

            route.RouteNumberPlacement = placement;
            PopulateRouteIntentMetadata(route);
            route.UpdatedAtUtcTicks = DateTime.UtcNow.Ticks;
        }

        public SavedRouteStatus EvaluateStatus(SavedRouteRecord route, SegmentValidationService validation)
        {
            if (route == null || route.IsDeleted)
                return SavedRouteStatus.Deleted;

            if (route.OrderedSegmentIds.Count == 0)
                return route.Waypoints.Count >= 2 ? SavedRouteStatus.RebuildNeeded : SavedRouteStatus.MissingSegments;

            var valid = 0;
            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                if (validation.IsValidRoadSegment(route.OrderedSegmentIds[i]))
                    valid++;
            }

            if (valid == route.OrderedSegmentIds.Count)
                return SavedRouteStatus.Valid;

            return valid == 0 ? SavedRouteStatus.MissingSegments : SavedRouteStatus.PartiallyValid;
        }

        public void AddLoadedRoute(SavedRouteRecord route)
        {
            if (route == null)
                return;

            _routes.Add(route);
            if (route.RouteId >= _nextRouteId)
                _nextRouteId = route.RouteId + 1;
        }

        private static void CopyRouteGeometry(SavedRouteRecord route, IReadOnlyList<RoadRouteWaypoint> waypoints, IReadOnlyList<Entity> segments, IReadOnlyList<string> streetNames)
        {
            if (waypoints != null)
            {
                for (var i = 0; i < waypoints.Count; i++)
                    route.Waypoints.Add(waypoints[i]);
            }

            if (segments == null)
                return;

            for (var i = 0; i < segments.Count; i++)
            {
                route.OrderedSegmentIds.Add(segments[i]);
                route.OriginalStreetNamesSnapshot.Add(streetNames != null && i < streetNames.Count ? streetNames[i] ?? string.Empty : string.Empty);
            }
        }

        private static void PopulateRouteIntentMetadata(SavedRouteRecord route)
        {
            route.LastKnownResolvedSegmentCount = route.OrderedSegmentIds.Count;
            if (route.Waypoints.Count == 0)
                return;

            var start = route.Waypoints[0];
            var end = route.Waypoints[route.Waypoints.Count - 1];
            route.StartAnchorSegment = start.Segment;
            route.EndAnchorSegment = end.Segment;
            route.StartAnchorCurvePosition = start.CurvePosition;
            route.EndAnchorCurvePosition = end.CurvePosition;
            route.StartAnchorPositionX = start.Position.x;
            route.StartAnchorPositionY = start.Position.y;
            route.StartAnchorPositionZ = start.Position.z;
            route.EndAnchorPositionX = end.Position.x;
            route.EndAnchorPositionY = end.Position.y;
            route.EndAnchorPositionZ = end.Position.z;
        }

        private static string BuildDefaultTitle(RoadRouteToolMode mode, string inputValue)
        {
            var value = string.IsNullOrWhiteSpace(inputValue) ? mode.ToString() : inputValue.Trim();
            return mode == RoadRouteToolMode.RenameSelectedSegments ? "Road name: " + value : "Route: " + value;
        }

        private static string ResolveRoutePrefixType(string inputValue)
        {
            var value = (inputValue ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0)
                return "None";

            var first = value[0].ToString();
            return first == "M" || first == "A" || first == "B" || first == "C" ? first : "Custom";
        }
    }
}
