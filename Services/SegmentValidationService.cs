using Game.Common;
using Game.Net;
using Unity.Entities;

namespace AdvancedRoadNaming.Services
{
    public sealed class SegmentValidationService
    {
        private readonly EntityManager _entityManager;

        public SegmentValidationService(EntityManager entityManager)
        {
            _entityManager = entityManager;
        }

        public bool IsValidRoadSegment(Entity entity)
        {
            return entity != Entity.Null
                && _entityManager.Exists(entity)
                && _entityManager.HasComponent<Edge>(entity)
                && _entityManager.HasComponent<Road>(entity)
                && _entityManager.HasComponent<Curve>(entity)
                && _entityManager.HasComponent<Composition>(entity)
                && !_entityManager.HasComponent<Deleted>(entity)
                && !_entityManager.HasComponent<Destroyed>(entity)
                && !_entityManager.HasComponent<Game.Tools.Temp>(entity);
        }
    }
}
