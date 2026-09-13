using System;
using System.Collections.Generic;
using Game.Net;
using Unity.Entities;

namespace AdvancedRoadNaming.Services
{
    public sealed class RoadNetworkPathingService
    {
        private const int InitialPathQueueCapacity = 128;
        private const int InitialPathMapCapacity = 256;

        private static readonly IReadOnlyList<Entity> EmptyPath = new Entity[0];

        private readonly EntityManager _entityManager;
        private readonly SegmentValidationService _validation;
        private readonly List<Entity> _queue = new List<Entity>(InitialPathQueueCapacity);
        private readonly Dictionary<Entity, Entity> _previous = new Dictionary<Entity, Entity>(InitialPathMapCapacity);
        private readonly Dictionary<Entity, int> _depth = new Dictionary<Entity, int>(InitialPathMapCapacity);
        private readonly Func<long> _getRevision;
        private readonly Dictionary<(Entity, Entity, int), IReadOnlyList<Entity>> _paths = new Dictionary<(Entity, Entity, int), IReadOnlyList<Entity>>();
        private long _cachedRevision = -1;
        private int _cachedEdges;
        public long Revision => _getRevision?.Invoke() ?? 0;

        public RoadNetworkPathingService(EntityManager entityManager, SegmentValidationService validation, Func<long> getRevision = null)
        {
            _entityManager = entityManager;
            _validation = validation;
            _getRevision = getRevision;
        }

        public bool AreConnected(Entity left, Entity right)
        {
            if (!_validation.IsValidRoadSegment(left) || !_validation.IsValidRoadSegment(right))
                return false;

            var leftEdge = _entityManager.GetComponentData<Edge>(left);
            var rightEdge = _entityManager.GetComponentData<Edge>(right);

            return leftEdge.m_Start == rightEdge.m_Start
                || leftEdge.m_Start == rightEdge.m_End
                || leftEdge.m_End == rightEdge.m_Start
                || leftEdge.m_End == rightEdge.m_End;
        }

        public IReadOnlyList<Entity> FindPath(Entity start, Entity target, int maxDepth)
        {
            var revision = Revision;
            if (_cachedRevision != revision)
            {
                _paths.Clear();
                _cachedEdges = 0;
                _cachedRevision = revision;
            }
            var key = (start, target, maxDepth);
            if (_paths.TryGetValue(key, out var cached))
                return cached;
            var result = FindPathUncached(start, target, maxDepth);
            // Bound retained paths, including unsuccessful searches, across long editing sessions.
            if (_paths.Count >= 256 || _cachedEdges + result.Count > 32768)
            {
                _paths.Clear();
                _cachedEdges = 0;
            }
            if (result.Count <= 32768)
            {
                _paths.Add(key, result);
                _cachedEdges += result.Count;
            }
            return result;
        }

        private IReadOnlyList<Entity> FindPathUncached(Entity start, Entity target, int maxDepth)
        {
            if (!_validation.IsValidRoadSegment(start) || !_validation.IsValidRoadSegment(target))
                return EmptyPath;

            if (start == target)
                return new List<Entity> { start };

            _queue.Clear();
            _previous.Clear();
            _depth.Clear();

            try
            {
                var readIndex = 0;
                _queue.Add(start);
                _previous[start] = Entity.Null;
                _depth[start] = 0;

                while (readIndex < _queue.Count)
                {
                    var current = _queue[readIndex++];
                    var currentDepth = _depth[current];
                    if (currentDepth >= maxDepth)
                        continue;

                    var edge = _entityManager.GetComponentData<Edge>(current);
                    var nextDepth = currentDepth + 1;

                    var path = TryVisitNodeNeighbors(edge.m_Start, current, target, nextDepth);
                    if (path != null)
                        return path;

                    path = TryVisitNodeNeighbors(edge.m_End, current, target, nextDepth);
                    if (path != null)
                        return path;
                }

                return EmptyPath;
            }
            finally
            {
                _queue.Clear();
                _previous.Clear();
                _depth.Clear();
            }
        }

        private IReadOnlyList<Entity> TryVisitNodeNeighbors(Entity node, Entity current, Entity target, int nextDepth)
        {
            if (node == Entity.Null || !_entityManager.Exists(node) || !_entityManager.HasBuffer<ConnectedEdge>(node))
                return null;

            var connectedEdges = _entityManager.GetBuffer<ConnectedEdge>(node, true);
            for (var i = 0; i < connectedEdges.Length; i++)
            {
                var neighbor = connectedEdges[i].m_Edge;
                if (neighbor == current || !_validation.IsValidRoadSegment(neighbor) || _previous.ContainsKey(neighbor))
                    continue;

                _previous[neighbor] = current;
                _depth[neighbor] = nextDepth;

                if (neighbor == target)
                    return ReconstructPath(_previous, target);

                _queue.Add(neighbor);
            }

            return null;
        }

        private static IReadOnlyList<Entity> ReconstructPath(Dictionary<Entity, Entity> previous, Entity target)
        {
            var path = new List<Entity>();
            var cursor = target;

            while (cursor != Entity.Null)
            {
                path.Add(cursor);
                cursor = previous[cursor];
            }

            path.Reverse();
            return path;
        }
    }
}



