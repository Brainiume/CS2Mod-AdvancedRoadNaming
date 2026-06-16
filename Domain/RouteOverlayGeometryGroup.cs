using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Domain
{
    public sealed class RouteOverlayGeometryGroup
    {
        public long RouteId;
        public bool Selected;
        public readonly List<Bezier4x3> Curves = new List<Bezier4x3>();
        public readonly List<float3> Nodes = new List<float3>();

        public void Clear()
        {
            Curves.Clear();
            Nodes.Clear();
        }
    }
}
