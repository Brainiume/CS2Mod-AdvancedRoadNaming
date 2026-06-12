using Game;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadAggregateProtectionSystem : GameSystemBase
    {
        private SegmentMetadataSystem _metadataSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
        }

        protected override void OnUpdate()
        {
            _metadataSystem?.ProtectModAggregatesBeforeVanilla();
        }
    }
}
