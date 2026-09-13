using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Entities;

namespace AdvancedRoadNaming.Systems
{
    // Observe change tags in ModificationEnd, before the frame's tag cleanup.
    // Query emptiness needs no entity arrays or main-thread component reads.
    public sealed partial class RoadNetworkRevisionSystem : GameSystemBase
    {
        private EntityQuery _changedEdges;
        private EntityQuery _changedNodes;
        public long Revision { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            _changedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Road>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
            _changedNodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnUpdate()
        {
            if (!_changedEdges.IsEmptyIgnoreFilter || !_changedNodes.IsEmptyIgnoreFilter)
                Revision++;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            Revision++;
        }
    }
}
