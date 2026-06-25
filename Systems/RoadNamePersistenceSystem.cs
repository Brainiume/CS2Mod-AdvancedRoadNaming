using Game;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadNamePersistenceSystem : GameSystemBase
    {
        private SegmentMetadataSystem _metadataSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
        }

        protected override void OnUpdate()
        {
            _metadataSystem.ProcessLiveNamePersistence();
        }
    }
}
