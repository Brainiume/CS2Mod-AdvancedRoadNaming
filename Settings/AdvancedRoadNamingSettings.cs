using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using AdvancedRoadNaming.Systems;
using Unity.Entities;

namespace AdvancedRoadNaming.Settings
{
    [FileLocation("ModsSettings\\AdvancedRoadNaming")]
    [SettingsUITabOrder(GeneralTab, StatisticsTab, KeybindingsTab)]
    [SettingsUIGroupOrder(DisplayGroup, StatisticsGeneralGroup, StatisticsCalculationGroup, ShortcutGroup, AdvancedGroup, AboutGroup, ResetGroup)]
    [SettingsUIShowGroupName(DisplayGroup, StatisticsGeneralGroup, StatisticsCalculationGroup, ShortcutGroup, AdvancedGroup, AboutGroup, ResetGroup)]
    [SettingsUIKeyboardAction(ToggleRenameActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage, Usages.kToolUsage })]
    [SettingsUIKeyboardAction(ToggleRoutesActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage, Usages.kToolUsage })]
    public sealed partial class AdvancedRoadNamingSettings : ModSetting
    {
        internal const string SettingsAssetName = "AdvancedRoadNaming";

        public const string GeneralTab = "General";
        public const string StatisticsTab = "Statistics";
        public const string KeybindingsTab = "Keybindings";

        public const string DisplayGroup = "Display";
        public const string StatisticsGeneralGroup = "StatisticsGeneral";
        public const string StatisticsCalculationGroup = "StatisticsCalculation";
        public const string ShortcutGroup = "Shortcuts";
        public const string AdvancedGroup = "Advanced";
        public const string AboutGroup = "About";
        public const string ResetGroup = "Reset";
        public const string ToggleRenameActionName = "ToggleRenameMode";
        public const string ToggleRoutesActionName = "ToggleRoutesMenu";

        public AdvancedRoadNamingSettings(IMod mod)
            : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUITextInput]
        public string BaseRouteSeparator { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUITextInput]
        public string RouteNumberSeparator { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        public bool AllowMultipleRouteNumbers { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUIHidden]
        public RouteNumberOrderingMode OrderingMode { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetOrderingModeStringOptions))]
        public string OrderingModeOption
        {
            get => OrderingMode.ToString();
            set => OrderingMode = ParseEnum(value, RouteNumberOrderingMode.InsertionOrder);
        }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        public bool ShowAdvancedRouteDetails { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        public bool EnableRouteShields { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUIHidden]
        public RouteShieldSpacingPreset RouteShieldSpacingPreset { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetRouteShieldSpacingStringOptions))]
        public string RouteShieldSpacingOption
        {
            get => RouteShieldSpacingPreset.ToString();
            set => RouteShieldSpacingPreset = ParseEnum(value, RouteShieldSpacingPreset.Moderate);
        }

        [SettingsUIHidden]
        public RouteShieldSizePreset RouteShieldSizePreset { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetRouteShieldSizeStringOptions))]
        public string RouteShieldSizeOption
        {
            get => RouteShieldSizePreset.ToString();
            set => RouteShieldSizePreset = ParseEnum(value, RouteShieldSizePreset.Medium);
        }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        public bool EnableRouteShieldOcclusion { get; set; }

        [SettingsUISection(GeneralTab, DisplayGroup)]
        [SettingsUISlider(min = 5, max = 60, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
        [SettingsUIDisableByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsRouteShieldOcclusionDisabled))]
        public int RouteShieldOcclusionAngle { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsGeneralGroup)]
        public bool EnableRouteStatistics { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsGeneralGroup)]
        [SettingsUIHidden]
        public RouteStatisticsPreset StatisticsPreset { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsGeneralGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetStatisticsPresetStringOptions))]
        public string StatisticsPresetOption
        {
            get => StatisticsPreset.ToString();
            set => StatisticsPreset = ParseEnum(value, RouteStatisticsPreset.Balanced);
        }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIHidden]
        public RouteVolumeAggregationMode StatisticsVolumeMode { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetVolumeAggregationStringOptions))]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public string StatisticsVolumeModeOption
        {
            get => StatisticsVolumeMode.ToString();
            set => StatisticsVolumeMode = ParseEnum(value, RouteVolumeAggregationMode.BusyPercentile);
        }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIHidden]
        public RouteFlowAggregationMode StatisticsFlowMode { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetFlowAggregationStringOptions))]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public string StatisticsFlowModeOption
        {
            get => StatisticsFlowMode.ToString();
            set => StatisticsFlowMode = ParseEnum(value, RouteFlowAggregationMode.VolumeWeighted);
        }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUISlider(min = 10, max = 100, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public int StatisticsBusySegmentShare { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUISlider(min = 50, max = 100, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public int StatisticsBusyVolumePercentile { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUISlider(min = 5, max = 100, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public int StatisticsBottleneckSegmentShare { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUISlider(min = 10, max = 90, step = 5, scalarMultiplier = 1, unit = Game.UI.Unit.kInteger)]
        [SettingsUIHideByCondition(typeof(AdvancedRoadNamingSettings), nameof(IsStatisticsPresetNotCustom))]
        public int StatisticsCongestionFlowThreshold { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIHidden]
        public RouteStatisticsRefreshRate StatisticsRefreshRate { get; set; }

        [SettingsUISection(StatisticsTab, StatisticsCalculationGroup)]
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetStatisticsRefreshRateStringOptions))]
        public string StatisticsRefreshRateOption
        {
            get => StatisticsRefreshRate.ToString();
            set => StatisticsRefreshRate = ParseEnum(value, RouteStatisticsRefreshRate.Balanced);
        }

        [SettingsUISection(KeybindingsTab, ShortcutGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.R, ToggleRenameActionName)]
        public ProxyBinding ToggleRenameBinding { get; set; }

        [SettingsUISection(KeybindingsTab, ShortcutGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.T, ToggleRoutesActionName)]
        public ProxyBinding ToggleRoutesBinding { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        public bool EnableSavedRenameRoutes { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        public bool EnableLogging { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        public bool CombineRoadAggregates { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        [SettingsUIButton]
        public bool RefreshCustomRouteShields
        {
            set
            {
                var uiSystem = World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<RoadRouteToolUISystem>();
                if (uiSystem == null)
                {
                    Mod.log.Warn("Road Naming: custom route shields could not be refreshed because RoadRouteToolUISystem is unavailable.");
                    return;
                }

                uiSystem.RefreshRouteShields();
            }
        }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        [SettingsUIButton]
        public bool OpenCustomRouteShieldFolder
        {
            set => RouteShieldImportCatalog.OpenImportFolder();
        }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        [SettingsUIButton]
        public bool OpenCustomRouteShieldDesigner
        {
            set => RouteShieldImportCatalog.OpenDesigner();
        }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        [SettingsUIValueVersion(typeof(RouteShieldImportCatalog), nameof(RouteShieldImportCatalog.Version))]
        public string RouteShieldCatalogStatus => RouteShieldImportCatalog.Status;

        [SettingsUIHidden]
        public bool CombineRoadAggregatesDefaultMigrated { get; set; }

        [SettingsUIHidden]
        public bool RouteStatisticsDefaultMigrated { get; set; }

        [SettingsUIHidden]
        public bool RouteShieldsDefaultMigrated { get; set; }

        [SettingsUIHidden]
        public bool SavedRenameRoutesDefaultMigrated { get; set; }

        [SettingsUISection(GeneralTab, AboutGroup)]
        public string Version => Mod.Instance?.Version ?? string.Empty;

        [SettingsUISection(GeneralTab, ResetGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool RemoveRoadRouteModeData
        {
            set
            {
                var metadataSystem = World.DefaultGameObjectInjectionWorld?.GetOrCreateSystemManaged<SegmentMetadataSystem>();
                if (metadataSystem == null)
                {
                    Mod.log.Warn("Road Naming: ROAD ROUTE data cleanup could not run because SegmentMetadataSystem is unavailable.");
                    return;
                }

                metadataSystem.RemoveAllRoadRouteModeData(out var message);
                Mod.log.Info(message);
            }
        }

        [SettingsUISection(GeneralTab, ResetGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool ResetGeneralSettings
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            BaseRouteSeparator = " - ";
            RouteNumberSeparator = " / ";
            AllowMultipleRouteNumbers = true;
            OrderingMode = RouteNumberOrderingMode.InsertionOrder;
            ShowAdvancedRouteDetails = false;
            EnableRouteShields = true;
            RouteShieldSpacingPreset = RouteShieldSpacingPreset.Moderate;
            RouteShieldSizePreset = RouteShieldSizePreset.Medium;
            EnableRouteShieldOcclusion = true;
            RouteShieldOcclusionAngle = 30;
            EnableRouteStatistics = true;
            StatisticsPreset = RouteStatisticsPreset.Balanced;
            StatisticsVolumeMode = RouteVolumeAggregationMode.BusyPercentile;
            StatisticsFlowMode = RouteFlowAggregationMode.VolumeWeighted;
            StatisticsBusySegmentShare = 50;
            StatisticsBusyVolumePercentile = 75;
            StatisticsBottleneckSegmentShare = 25;
            StatisticsCongestionFlowThreshold = 50;
            StatisticsRefreshRate = RouteStatisticsRefreshRate.Balanced;
            EnableSavedRenameRoutes = true;
            EnableLogging = false;
            CombineRoadAggregates = true;
            CombineRoadAggregatesDefaultMigrated = false;
            RouteStatisticsDefaultMigrated = false;
            RouteShieldsDefaultMigrated = false;
            SavedRenameRoutesDefaultMigrated = false;
        }

        public void EnsureCombineRoadAggregatesDefaultEnabled()
        {
            if (CombineRoadAggregatesDefaultMigrated)
                return;

            CombineRoadAggregates = true;
            CombineRoadAggregatesDefaultMigrated = true;
            ApplyAndSave();
            Mod.log.Info("Road Naming: Combine Road Aggregates was enabled by default migration.");
        }

        public void EnsureRouteStatisticsDefaultEnabled()
        {
            if (RouteStatisticsDefaultMigrated)
                return;

            EnableRouteStatistics = true;
            RouteStatisticsDefaultMigrated = true;
            ApplyAndSave();
            Mod.log.Info("Road Naming: Route Statistics was enabled by default migration.");
        }

        public void EnsureRouteShieldsDefaultEnabled()
        {
            if (RouteShieldsDefaultMigrated)
                return;

            EnableRouteShields = true;
            RouteShieldSpacingPreset = RouteShieldSpacingPreset.Moderate;
            RouteShieldsDefaultMigrated = true;
            ApplyAndSave();
            Mod.log.Info("Road Naming: Route Shields were enabled by default migration.");
        }

        public void EnsureSavedRenameRoutesDefaultEnabled()
        {
            if (SavedRenameRoutesDefaultMigrated)
                return;

            EnableSavedRenameRoutes = true;
            SavedRenameRoutesDefaultMigrated = true;
            ApplyAndSave();
            Mod.log.Info("Road Naming: Saved Rename Routes were enabled by default migration.");
        }

        public DropdownItem<RouteNumberOrderingMode>[] GetOrderingModeOptions()
        {
            return new[]
            {
                new DropdownItem<RouteNumberOrderingMode>
                {
                    value = RouteNumberOrderingMode.InsertionOrder,
                    displayName = GetOrderingModeLocaleID(RouteNumberOrderingMode.InsertionOrder),
                },
                new DropdownItem<RouteNumberOrderingMode>
                {
                    value = RouteNumberOrderingMode.Sorted,
                    displayName = GetOrderingModeLocaleID(RouteNumberOrderingMode.Sorted),
                },
            };
        }

        public DropdownItem<RouteShieldSpacingPreset>[] GetRouteShieldSpacingPresetOptions()
        {
            return BuildEnumOptions(
                RouteShieldSpacingPreset.Frequent,
                RouteShieldSpacingPreset.Moderate,
                RouteShieldSpacingPreset.Sparse);
        }

        public DropdownItem<string>[] GetOrderingModeStringOptions()
        {
            return new[]
            {
                new DropdownItem<string> { value = nameof(RouteNumberOrderingMode.InsertionOrder), displayName = GetOrderingModeLocaleID(RouteNumberOrderingMode.InsertionOrder) },
                new DropdownItem<string> { value = nameof(RouteNumberOrderingMode.Sorted), displayName = GetOrderingModeLocaleID(RouteNumberOrderingMode.Sorted) },
            };
        }

        public DropdownItem<string>[] GetRouteShieldSpacingStringOptions()
        {
            return BuildStringEnumOptions(
                RouteShieldSpacingPreset.Frequent,
                RouteShieldSpacingPreset.Moderate,
                RouteShieldSpacingPreset.Sparse);
        }

        public DropdownItem<string>[] GetRouteShieldSizeStringOptions()
        {
            return BuildStringEnumOptions(
                RouteShieldSizePreset.Small,
                RouteShieldSizePreset.Medium,
                RouteShieldSizePreset.Large,
                RouteShieldSizePreset.VeryLarge);
        }

        public bool IsRouteShieldOcclusionDisabled()
        {
            return !EnableRouteShieldOcclusion;
        }

        public DropdownItem<string>[] GetStatisticsPresetStringOptions()
        {
            return BuildStringEnumOptions(
                RouteStatisticsPreset.Balanced,
                RouteStatisticsPreset.CorridorScale,
                RouteStatisticsPreset.BottleneckAnalysis,
                RouteStatisticsPreset.Custom);
        }

        public DropdownItem<string>[] GetVolumeAggregationStringOptions()
        {
            return BuildStringEnumOptions(
                RouteVolumeAggregationMode.BusyPercentile,
                RouteVolumeAggregationMode.BusiestShareAverage,
                RouteVolumeAggregationMode.AllSegmentsAverage,
                RouteVolumeAggregationMode.CorridorLoad);
        }

        public DropdownItem<string>[] GetFlowAggregationStringOptions()
        {
            return BuildStringEnumOptions(
                RouteFlowAggregationMode.VolumeWeighted,
                RouteFlowAggregationMode.BusiestShareAverage,
                RouteFlowAggregationMode.AllSegmentsAverage,
                RouteFlowAggregationMode.Bottleneck);
        }

        public DropdownItem<string>[] GetStatisticsRefreshRateStringOptions()
        {
            return BuildStringEnumOptions(
                RouteStatisticsRefreshRate.Fast,
                RouteStatisticsRefreshRate.Balanced,
                RouteStatisticsRefreshRate.Efficient);
        }

        public bool IsStatisticsPresetNotCustom()
        {
            return StatisticsPreset != RouteStatisticsPreset.Custom;
        }

        public DropdownItem<RouteStatisticsPreset>[] GetStatisticsPresetOptions()
        {
            return BuildEnumOptions(
                RouteStatisticsPreset.Balanced,
                RouteStatisticsPreset.CorridorScale,
                RouteStatisticsPreset.BottleneckAnalysis,
                RouteStatisticsPreset.Custom);
        }

        public DropdownItem<RouteVolumeAggregationMode>[] GetVolumeAggregationModeOptions()
        {
            return BuildEnumOptions(
                RouteVolumeAggregationMode.BusyPercentile,
                RouteVolumeAggregationMode.BusiestShareAverage,
                RouteVolumeAggregationMode.AllSegmentsAverage,
                RouteVolumeAggregationMode.CorridorLoad);
        }

        public DropdownItem<RouteFlowAggregationMode>[] GetFlowAggregationModeOptions()
        {
            return BuildEnumOptions(
                RouteFlowAggregationMode.VolumeWeighted,
                RouteFlowAggregationMode.BusiestShareAverage,
                RouteFlowAggregationMode.AllSegmentsAverage,
                RouteFlowAggregationMode.Bottleneck);
        }

        public DropdownItem<RouteStatisticsRefreshRate>[] GetStatisticsRefreshRateOptions()
        {
            return BuildEnumOptions(
                RouteStatisticsRefreshRate.Fast,
                RouteStatisticsRefreshRate.Balanced,
                RouteStatisticsRefreshRate.Efficient);
        }

        private DropdownItem<T>[] BuildEnumOptions<T>(params T[] values)
        {
            var result = new DropdownItem<T>[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                result[i] = new DropdownItem<T>
                {
                    value = values[i],
                    displayName = GetStatisticsEnumLocaleID(typeof(T).Name, values[i].ToString()),
                };
            }

            return result;
        }

        private DropdownItem<string>[] BuildStringEnumOptions<T>(params T[] values)
        {
            var result = new DropdownItem<string>[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i].ToString();
                result[i] = new DropdownItem<string>
                {
                    value = value,
                    displayName = GetStatisticsEnumLocaleID(typeof(T).Name, value),
                };
            }

            return result;
        }

        private static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            return System.Enum.TryParse(value ?? string.Empty, true, out T parsed) ? parsed : fallback;
        }

        public string GetStatisticsEnumLocaleID(string typeName, string value)
        {
            return $"{Mod.Id}.Options.{typeName}[{value}]";
        }

        public RouteStatisticsConfiguration GetRouteStatisticsConfiguration()
        {
            var configuration = new RouteStatisticsConfiguration
            {
                VolumeMode = StatisticsVolumeMode,
                FlowMode = StatisticsFlowMode,
                BusySegmentShare = Clamp(StatisticsBusySegmentShare, 10, 100),
                BusyVolumePercentile = Clamp(StatisticsBusyVolumePercentile, 50, 100),
                BottleneckSegmentShare = Clamp(StatisticsBottleneckSegmentShare, 5, 100),
                CongestionFlowThreshold = Clamp(StatisticsCongestionFlowThreshold, 10, 90),
                RefreshInterval = ResolveRefreshInterval(StatisticsRefreshRate),
            };

            switch (StatisticsPreset)
            {
                case RouteStatisticsPreset.CorridorScale:
                    configuration.VolumeMode = RouteVolumeAggregationMode.CorridorLoad;
                    configuration.FlowMode = RouteFlowAggregationMode.VolumeWeighted;
                    configuration.BusySegmentShare = 50;
                    configuration.BusyVolumePercentile = 75;
                    configuration.BottleneckSegmentShare = 25;
                    configuration.CongestionFlowThreshold = 50;
                    break;
                case RouteStatisticsPreset.BottleneckAnalysis:
                    configuration.VolumeMode = RouteVolumeAggregationMode.BusyPercentile;
                    configuration.FlowMode = RouteFlowAggregationMode.Bottleneck;
                    configuration.BusySegmentShare = 50;
                    configuration.BusyVolumePercentile = 75;
                    configuration.BottleneckSegmentShare = 25;
                    configuration.CongestionFlowThreshold = 60;
                    break;
                case RouteStatisticsPreset.Balanced:
                    configuration.VolumeMode = RouteVolumeAggregationMode.BusyPercentile;
                    configuration.FlowMode = RouteFlowAggregationMode.VolumeWeighted;
                    configuration.BusySegmentShare = 50;
                    configuration.BusyVolumePercentile = 75;
                    configuration.BottleneckSegmentShare = 25;
                    configuration.CongestionFlowThreshold = 50;
                    break;
            }

            return configuration;
        }

        private static uint ResolveRefreshInterval(RouteStatisticsRefreshRate refreshRate)
        {
            switch (refreshRate)
            {
                case RouteStatisticsRefreshRate.Fast:
                    return 128;
                case RouteStatisticsRefreshRate.Efficient:
                    return 512;
                default:
                    return 256;
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        public string GetOrderingModeLocaleID(RouteNumberOrderingMode mode)
        {
            return $"{Mod.Id}.Options.OrderingMode[{mode}]";
        }

        public SegmentDisplaySettings ToDisplaySettings()
        {
            return new SegmentDisplaySettings
            {
                BaseRouteSeparator = string.IsNullOrEmpty(BaseRouteSeparator) ? " - " : BaseRouteSeparator,
                RouteNumberSeparator = string.IsNullOrEmpty(RouteNumberSeparator) ? " / " : RouteNumberSeparator,
                AllowMultipleRouteNumbers = AllowMultipleRouteNumbers,
                OrderingMode = OrderingMode,
            };
        }

    }
}
