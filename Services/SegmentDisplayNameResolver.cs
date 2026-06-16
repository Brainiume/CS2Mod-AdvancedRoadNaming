using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;

namespace AdvancedRoadNaming.Services
{
    public sealed class SegmentDisplayNameResolver
    {
        public string Resolve(string gameOrGeneratedBaseName, SegmentRouteMetadata metadata, SegmentDisplaySettings settings)
        {
            if (metadata == null)
                return SafeBaseName(gameOrGeneratedBaseName);

            var baseName = !string.IsNullOrWhiteSpace(metadata.OptionalCustomRoadName)
                ? metadata.OptionalCustomRoadName.Trim()
                : !string.IsNullOrWhiteSpace(metadata.BaseNameSnapshot)
                    ? metadata.BaseNameSnapshot.Trim()
                    : SafeBaseName(gameOrGeneratedBaseName);

            if (metadata.RouteNumbers == null || metadata.RouteNumbers.Count == 0)
                return baseName;

            var routeNumbers = GetOrderedRouteNumbers(metadata.RouteNumbers, settings);
            if (routeNumbers.Count == 0)
                return baseName;

            var routeNumberDisplay = string.Join(settings.RouteNumberSeparator, routeNumbers);
            return metadata.RouteNumberPlacement == RouteNumberPlacement.BeforeBaseName
                ? routeNumberDisplay + settings.BaseRouteSeparator + baseName
                : baseName + settings.BaseRouteSeparator + routeNumberDisplay;
        }

        private static string SafeBaseName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unnamed Road Segment" : value.Trim();
        }

        private static List<string> GetOrderedRouteNumbers(IReadOnlyList<string> routeNumbers, SegmentDisplaySettings settings)
        {
            var ordered = new List<string>();
            if (routeNumbers == null || routeNumbers.Count == 0)
                return ordered;

            for (var i = 0; i < routeNumbers.Count; i++)
            {
                var route = routeNumbers[i];
                if (string.IsNullOrWhiteSpace(route))
                    continue;

                route = route.Trim();
                if (!ContainsRouteNumber(ordered, route))
                    ordered.Add(route);
            }

            if (settings.OrderingMode == RouteNumberOrderingMode.Sorted && ordered.Count > 1)
                ordered.Sort(StringComparer.OrdinalIgnoreCase);

            return ordered;
        }

        private static bool ContainsRouteNumber(IReadOnlyList<string> routeNumbers, string candidate)
        {
            for (var i = 0; i < routeNumbers.Count; i++)
            {
                if (string.Equals(routeNumbers[i], candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
