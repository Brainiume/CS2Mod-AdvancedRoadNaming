using System.Collections.Generic;
using Unity.Entities;

namespace AdvancedRoadNaming.Domain
{
    public sealed class SavedRouteReviewSession
    {
        public long RouteId { get; set; }

        public SavedRouteReviewMode Mode { get; set; }

        public RoadRouteToolMode RouteMode { get; set; }

        public string InputValue { get; set; }

        public RouteNumberPlacement RouteNumberPlacement { get; set; } = RouteNumberPlacement.AfterBaseName;

        public RouteShieldStyle RouteShieldStyle { get; set; } = RouteShieldStyle.None;

        public string RouteShieldImportId { get; set; }

        public string Message { get; set; }

        public bool IsDirty { get; set; }

        public readonly List<RoadRouteWaypoint> CandidateWaypoints = new List<RoadRouteWaypoint>();

        public readonly List<Entity> CandidateSegments = new List<Entity>();
    }
}
