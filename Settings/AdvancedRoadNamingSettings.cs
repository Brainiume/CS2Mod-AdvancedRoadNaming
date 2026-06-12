using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Systems;
using Unity.Entities;

namespace AdvancedRoadNaming.Settings
{
    [FileLocation("ModsSettings\\AdvancedRoadNaming")]
    [SettingsUITabOrder(GeneralTab)]
    [SettingsUIGroupOrder(DisplayGroup, AdvancedGroup, AboutGroup, ResetGroup)]
    [SettingsUIShowGroupName(DisplayGroup, AdvancedGroup, AboutGroup, ResetGroup)]
    public sealed partial class AdvancedRoadNamingSettings : ModSetting
    {
        internal const string SettingsAssetName = "AdvancedRoadNaming";

        public const string GeneralTab = "General";

        public const string DisplayGroup = "Display";
        public const string AdvancedGroup = "Advanced";
        public const string AboutGroup = "About";
        public const string ResetGroup = "Reset";

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
        [SettingsUIDropdown(typeof(AdvancedRoadNamingSettings), nameof(GetOrderingModeOptions))]
        public RouteNumberOrderingMode OrderingMode { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        public bool EnableLogging { get; set; }

        [SettingsUISection(GeneralTab, AdvancedGroup)]
        public bool CombineRoadAggregates { get; set; }

        [SettingsUIHidden]
        public bool CombineRoadAggregatesDefaultMigrated { get; set; }

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
            EnableLogging = false;
            CombineRoadAggregates = true;
            CombineRoadAggregatesDefaultMigrated = false;
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
