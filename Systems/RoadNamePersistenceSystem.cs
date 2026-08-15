using Game;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadNamePersistenceSystem : GameSystemBase
    {
        private SegmentMetadataSystem _metadataSystem;
        private ProtectedAggregateRepairSystem _aggregateRepairSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
            _aggregateRepairSystem = World.GetOrCreateSystemManaged<ProtectedAggregateRepairSystem>();
        }

        protected override void OnUpdate()
        {
            _aggregateRepairSystem.ProcessPostModificationRequests();
            _metadataSystem.ProcessLiveNamePersistence();
        }
    }
}
