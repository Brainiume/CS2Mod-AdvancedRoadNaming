using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Services
{
    // One arc-length table per rebuild. All anchors use the same distance approximation.
    internal sealed class RouteCurveSampler
    {
        private const int SamplesPerCurve = 64;
        private IReadOnlyList<Bezier4x3> _curves;
        private readonly List<float> _distances = new List<float>();
        public float Length { get; private set; }

        public void Rebuild(IReadOnlyList<Bezier4x3> curves)
        {
            _curves = curves;
            _distances.Clear();
            Length = 0f;
            for (var i = 0; i < curves.Count; i++)
            {
                var curve = curves[i];
                var previous = curve.a;
                _distances.Add(Length);
                for (var sample = 1; sample <= SamplesPerCurve; sample++)
                {
                    var point = MathUtils.Position(curve, sample / (float)SamplesPerCurve);
                    Length += math.distance(previous, point);
                    _distances.Add(Length);
                    previous = point;
                }
            }
        }

        public void Place(float requestedSpacing, List<float3> positions)
        {
            positions.Clear();
            if (Length < 5f || !math.isfinite(Length) || _distances.Count < 2)
                return;
            var count = math.max(1, (int)math.floor(Length / math.max(1f, requestedSpacing) + 0.5f));
            var spacing = Length / count;
            var sample = 1;
            for (var i = 0; i < count; i++)
            {
                // Equal half-spacing margins avoid a short final interval at the route end.
                var distance = (i + 0.5f) * spacing;
                while (sample < _distances.Count - 1 && _distances[sample] < distance)
                    sample++;
                var step = _distances[sample] - _distances[sample - 1];
                var t = step > 0.00001f ? math.saturate((distance - _distances[sample - 1]) / step) : 0f;
                var curveIndex = sample / (SamplesPerCurve + 1);
                var curveSample = sample % (SamplesPerCurve + 1);
                var parameter = curveSample == 0 ? 0f : (curveSample - 1 + t) / SamplesPerCurve;
                positions.Add(MathUtils.Position(_curves[curveIndex], parameter));
            }
        }
    }
}
