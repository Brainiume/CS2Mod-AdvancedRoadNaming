namespace AdvancedRoadNaming.Settings
{
    public enum RouteStatisticsPreset
    {
        Balanced,
        CorridorScale,
        BottleneckAnalysis,
        Custom
    }

    public enum RouteVolumeAggregationMode
    {
        BusyPercentile,
        BusiestShareAverage,
        AllSegmentsAverage,
        CorridorLoad
    }

    public enum RouteFlowAggregationMode
    {
        VolumeWeighted,
        BusiestShareAverage,
        AllSegmentsAverage,
        Bottleneck
    }

    public enum RouteStatisticsRefreshRate
    {
        Fast,
        Balanced,
        Efficient
    }

    public struct RouteStatisticsConfiguration
    {
        public RouteVolumeAggregationMode VolumeMode;
        public RouteFlowAggregationMode FlowMode;
        public int BusySegmentShare;
        public int BusyVolumePercentile;
        public int BottleneckSegmentShare;
        public int CongestionFlowThreshold;
        public uint RefreshInterval;
    }
}
