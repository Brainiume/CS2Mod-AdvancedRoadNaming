using Unity.Collections;
using Unity.Entities;

namespace AdvancedRoadNaming.Components
{
    public struct AdvancedRoadNamingAggregateNameRequest : IComponentData
    {
        public FixedString4096Bytes Name;
        public int GroupIndex;
        public byte Clear;
    }

    public struct AdvancedRoadNamingAggregateRepairResult : IBufferElementData
    {
        public int ModifiedEntitiesInspected;
        public int DirtyGroupsQueued;
        public int GroupsProcessed;
        public int AggregatesCreated;
        public int ExactOwnersReused;
        public int MixedOwnersSeparated;
        public int InvalidEdgesDropped;
        public int GroupsDeferred;
    }

    public struct AdvancedRoadNamingAggregateReplacementResult : IBufferElementData
    {
        public Entity Original;
        public Entity Replacement;
        public Colossal.Mathematics.Bezier4x3 OriginalCurve;
    }

    public struct AdvancedRoadNamingAggregateRepairRuntime : IComponentData
    {
    }
}
