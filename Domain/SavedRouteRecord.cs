using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using Unity.Entities;

namespace AdvancedRoadNaming.Domain
{
    public sealed class SavedRouteRecord : IJsonWritable
    {
        public long RouteId { get; set; }
        public string DisplayTitle { get; set; }
        public RoadRouteToolMode Mode { get; set; }
        public string BaseInputValue { get; set; }
        public string RouteCode { get; set; }
        public string RoutePrefixType { get; set; }
        public RouteNumberPlacement RouteNumberPlacement { get; set; } = RouteNumberPlacement.AfterBaseName;
        public long CreatedAtUtcTicks { get; set; }
        public long UpdatedAtUtcTicks { get; set; }
        public long LastAppliedUtcTicks { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsUserDefinedTitle { get; set; }
        public string Notes { get; set; }
        public string StartDistrictName { get; set; }
        public string EndDistrictName { get; set; }
        public string StartRoadName { get; set; }
        public string EndRoadName { get; set; }
        public string DerivedDisplayCorridor { get; set; }
        public string DistrictSummary { get; set; }
        public Entity StartAnchorSegment { get; set; }
        public Entity EndAnchorSegment { get; set; }
        public float StartAnchorCurvePosition { get; set; }
        public float EndAnchorCurvePosition { get; set; }
        public float StartAnchorPositionX { get; set; }
        public float StartAnchorPositionY { get; set; }
        public float StartAnchorPositionZ { get; set; }
        public float EndAnchorPositionX { get; set; }
        public float EndAnchorPositionY { get; set; }
        public float EndAnchorPositionZ { get; set; }
        public int LastKnownResolvedSegmentCount { get; set; }
        public readonly List<RoadRouteWaypoint> Waypoints = new List<RoadRouteWaypoint>();
        public readonly List<Entity> OrderedSegmentIds = new List<Entity>();
        public readonly List<string> OriginalStreetNamesSnapshot = new List<string>();

        public int SegmentCount => OrderedSegmentIds.Count;

        public int WaypointCount => Waypoints.Count;

        public void ClearStoredData()
        {
            Waypoints.Clear();
            OrderedSegmentIds.Clear();
            OriginalStreetNamesSnapshot.Clear();
            Notes = null;
            StartDistrictName = null;
            EndDistrictName = null;
            StartRoadName = null;
            EndRoadName = null;
            DerivedDisplayCorridor = null;
            DistrictSummary = null;
            StartAnchorSegment = Entity.Null;
            EndAnchorSegment = Entity.Null;
            StartAnchorCurvePosition = 0;
            EndAnchorCurvePosition = 0;
            StartAnchorPositionX = 0;
            StartAnchorPositionY = 0;
            StartAnchorPositionZ = 0;
            EndAnchorPositionX = 0;
            EndAnchorPositionY = 0;
            EndAnchorPositionZ = 0;
            LastKnownResolvedSegmentCount = 0;
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin(typeof(SavedRouteRecord).FullName);
            writer.PropertyName(nameof(RouteId));
            writer.Write(RouteId);
            writer.PropertyName(nameof(DisplayTitle));
            writer.Write(DisplayTitle);
            writer.PropertyName(nameof(Mode));
            writer.Write(Enum.GetName(typeof(RoadRouteToolMode), Mode));
            writer.PropertyName(nameof(BaseInputValue));
            writer.Write(BaseInputValue);
            writer.PropertyName(nameof(RouteCode));
            writer.Write(RouteCode);
            writer.PropertyName(nameof(RoutePrefixType));
            writer.Write(RoutePrefixType);
            writer.PropertyName(nameof(RouteNumberPlacement));
            writer.Write(Enum.GetName(typeof(RouteNumberPlacement), RouteNumberPlacement));
            writer.PropertyName(nameof(CreatedAtUtcTicks));
            writer.Write(CreatedAtUtcTicks);
            writer.PropertyName(nameof(UpdatedAtUtcTicks));
            writer.Write(UpdatedAtUtcTicks);
            writer.PropertyName(nameof(LastAppliedUtcTicks));
            writer.Write(LastAppliedUtcTicks);
            writer.PropertyName(nameof(IsDeleted));
            writer.Write(IsDeleted);
            writer.PropertyName(nameof(IsUserDefinedTitle));
            writer.Write(IsUserDefinedTitle);
            writer.PropertyName(nameof(Notes));
            writer.Write(Notes);
            writer.PropertyName(nameof(StartDistrictName));
            writer.Write(StartDistrictName);
            writer.PropertyName(nameof(EndDistrictName));
            writer.Write(EndDistrictName);
            writer.PropertyName(nameof(StartRoadName));
            writer.Write(StartRoadName);
            writer.PropertyName(nameof(EndRoadName));
            writer.Write(EndRoadName);
            writer.PropertyName(nameof(DerivedDisplayCorridor));
            writer.Write(DerivedDisplayCorridor);
            writer.PropertyName(nameof(DistrictSummary));
            writer.Write(DistrictSummary);
            writer.PropertyName(nameof(StartAnchorSegment));
            writer.Write(StartAnchorSegment);
            writer.PropertyName(nameof(EndAnchorSegment));
            writer.Write(EndAnchorSegment);
            writer.PropertyName(nameof(StartAnchorCurvePosition));
            writer.Write(StartAnchorCurvePosition);
            writer.PropertyName(nameof(EndAnchorCurvePosition));
            writer.Write(EndAnchorCurvePosition);
            writer.PropertyName(nameof(StartAnchorPositionX));
            writer.Write(StartAnchorPositionX);
            writer.PropertyName(nameof(StartAnchorPositionY));
            writer.Write(StartAnchorPositionY);
            writer.PropertyName(nameof(StartAnchorPositionZ));
            writer.Write(StartAnchorPositionZ);
            writer.PropertyName(nameof(EndAnchorPositionX));
            writer.Write(EndAnchorPositionX);
            writer.PropertyName(nameof(EndAnchorPositionY));
            writer.Write(EndAnchorPositionY);
            writer.PropertyName(nameof(EndAnchorPositionZ));
            writer.Write(EndAnchorPositionZ);
            writer.PropertyName(nameof(LastKnownResolvedSegmentCount));
            writer.Write(LastKnownResolvedSegmentCount);
            writer.PropertyName(nameof(Waypoints));
            writer.ArrayBegin(Waypoints.Count);
            foreach (var waypoint in Waypoints)
            {
                waypoint.Write(writer);
            }
            writer.ArrayEnd();
            writer.PropertyName(nameof(OrderedSegmentIds));
            writer.ArrayBegin(OrderedSegmentIds.Count);
            foreach (var segmentId in OrderedSegmentIds)
            {
                writer.Write(segmentId);
            }
            writer.ArrayEnd();
            writer.PropertyName(nameof(OriginalStreetNamesSnapshot));
            writer.ArrayBegin(OriginalStreetNamesSnapshot.Count);
            foreach (var streetName in OriginalStreetNamesSnapshot)
            {
                writer.Write(streetName);
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }
    }
}
