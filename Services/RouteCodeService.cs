using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AdvancedRoadNaming.Services
{
    public sealed class RouteCodeService
    {
        // Unicode compatible: accepts any Unicode letters, numbers, and hyphens
        private static readonly Regex RouteCodePattern = new Regex(
            @"^[\p{L}\p{N}][\p{L}\p{N}\-]{0,15}$", 
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );        
        public bool TryNormalize(string value, out string routeCode, out string error)
        {
            routeCode = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

            if (string.IsNullOrWhiteSpace(routeCode))
            {
                error = "Enter a route code such as A1, M2, HWY7, R3, or 12.";
                return false;
            }

            if (!RouteCodePattern.IsMatch(routeCode))
            {
                error = "Route codes may use letters, numbers and hyphens, for example A8, M2, HWY7, or 12.";
                return false;
            }

            error = null;
            return true;
        }

        public bool AddRouteCode(SegmentRouteMetadataAdapter adapter, string routeCode, bool allowMultiple)
        {
            if (adapter.Contains(routeCode))
                return false;

            if (!allowMultiple)
                adapter.Clear();

            adapter.Add(routeCode);
            return true;
        }

        public sealed class SegmentRouteMetadataAdapter
        {
            private readonly List<string> _routes;

            public SegmentRouteMetadataAdapter(List<string> routes)
            {
                _routes = routes;
            }

            public bool Contains(string routeCode)
            {
                return _routes.Exists(route => string.Equals(route, routeCode, StringComparison.OrdinalIgnoreCase));
            }

            public void Add(string routeCode)
            {
                _routes.Add(routeCode);
            }

            public void Clear()
            {
                _routes.Clear();
            }
        }
    }
}
