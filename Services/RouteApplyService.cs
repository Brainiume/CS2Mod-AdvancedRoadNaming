using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using Unity.Entities;

namespace AdvancedRoadNaming.Services
{
    public sealed class RouteApplyService
    {
        private readonly SegmentMetadataRepository _repository;
        private readonly SegmentValidationService _validation;
        private readonly SegmentDisplayNameResolver _resolver;
        private readonly RouteCodeService _routeCodeService;
        private readonly Func<Entity, string> _baseNameProvider;
        private readonly Action<string> _logInfo;

        public RouteApplyService(
            SegmentMetadataRepository repository,
            SegmentValidationService validation,
            SegmentDisplayNameResolver resolver,
            RouteCodeService routeCodeService,
            Func<Entity, string> baseNameProvider,
            Action<string> logInfo)
        {
            _repository = repository;
            _validation = validation;
            _resolver = resolver;
            _routeCodeService = routeCodeService;
            _baseNameProvider = baseNameProvider;
            _logInfo = logInfo;
        }

        public bool ApplyRename(IReadOnlyList<Entity> selectedSegments, string newRoadName, SegmentDisplaySettings settings, out string message)
        {
            if (selectedSegments == null || selectedSegments.Count == 0)
            {
                message = "Create a committed waypoint route before applying.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newRoadName))
            {
                message = "Enter the new road name.";
                return false;
            }

            var count = 0;
            foreach (var segment in selectedSegments)
            {
                if (!_validation.IsValidRoadSegment(segment))
                    continue;

                var metadata = _repository.GetOrCreate(segment);
                EnsureBaseSnapshot(segment, metadata);
                metadata.OptionalCustomRoadName = newRoadName.Trim();
                metadata.Touch();
                LogResolvedName(segment, metadata, settings);
                count++;
            }

            message = $"Renamed {count} committed route segment(s) to {newRoadName.Trim()}.";
            _logInfo(message);
            return count > 0;
        }

        public bool ApplyRouteNumber(IReadOnlyList<Entity> selectedSegments, string routeCodeInput, RouteNumberPlacement placement, SegmentDisplaySettings settings, out string message)
        {
            if (selectedSegments == null || selectedSegments.Count == 0)
            {
                message = "Create a committed waypoint route before applying.";
                return false;
            }

            if (!_routeCodeService.TryNormalize(routeCodeInput, out var routeCode, out message))
                return false;

            var changed = 0;
            var visited = 0;
            foreach (var segment in selectedSegments)
            {
                if (!_validation.IsValidRoadSegment(segment))
                    continue;

                var metadata = _repository.GetOrCreate(segment);
                EnsureBaseSnapshot(segment, metadata);
                var adapter = new RouteCodeService.SegmentRouteMetadataAdapter(metadata.RouteNumbers);
                if (_routeCodeService.AddRouteCode(adapter, routeCode, settings.AllowMultipleRouteNumbers))
                    changed++;

                metadata.RouteNumberPlacement = placement;
                metadata.Touch();
                LogResolvedName(segment, metadata, settings);
                visited++;
            }

            message = $"Applied {routeCode} to {visited} committed route segment(s); {changed} segment(s) changed.";
            _logInfo(message);
            return visited > 0;
        }

        private void EnsureBaseSnapshot(Entity segment, SegmentRouteMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot))
                metadata.BaseNameSnapshot = _baseNameProvider(segment);
        }

        private void LogResolvedName(Entity segment, SegmentRouteMetadata metadata, SegmentDisplaySettings settings)
        {
            var gameStreetName = _baseNameProvider(segment);
            var resolvedName = _resolver.Resolve(gameStreetName, metadata, settings);
            var routeCodes = metadata.RouteNumbers.Count == 0 ? string.Empty : string.Join(settings.RouteNumberSeparator, metadata.RouteNumbers.ToArray());
            _logInfo($"Segment={segment.Index}, GameStreetName=\'{gameStreetName ?? string.Empty}\', CustomOverride=\'{metadata.OptionalCustomRoadName ?? string.Empty}\', RouteCodes=\'{routeCodes}\', FinalRendered=\'{resolvedName}\'");
        }
    }
}
