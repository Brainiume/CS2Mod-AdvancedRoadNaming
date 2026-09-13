using System.Collections.Generic;
using Colossal;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Settings;

namespace AdvancedRoadNaming.L10N
{
    public static partial class AdvancedRoadNamingLocalization
    {
        public sealed class LocaleEN : IDictionarySource
        {
            private readonly AdvancedRoadNamingSettings _settings;

            public LocaleEN(AdvancedRoadNamingSettings settings)
            {
                _settings = settings;
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
            {
                return new Dictionary<string, string>
                {
                    { _settings.GetSettingsLocaleID(), "Advanced Road Naming" },
                    { _settings.GetOptionTabLocaleID(AdvancedRoadNamingSettings.GeneralTab), "General" },
                    { _settings.GetOptionTabLocaleID(AdvancedRoadNamingSettings.StatisticsTab), "Statistics" },
                    { _settings.GetOptionTabLocaleID(AdvancedRoadNamingSettings.KeybindingsTab), "Key Bindings" },

                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.DisplayGroup), "Display" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.StatisticsGeneralGroup), "General" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.StatisticsCalculationGroup), "Calculation" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.ShortcutGroup), "Shortcuts" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.AdvancedGroup), "Advanced" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.AboutGroup), "About" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.ResetGroup), "Reset" },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.BaseRouteSeparator)), "Base Route Separator" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.BaseRouteSeparator)), "Text inserted between the preserved <base street name> and the rendered <route numbers>." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteNumberSeparator)), "Route Number Separator" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteNumberSeparator)), "Text inserted between <multiple route numbers> shown on the same road segment." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.AllowMultipleRouteNumbers)), "Allow Multiple Route Numbers" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.AllowMultipleRouteNumbers)), "Allow a segment to display <more than one route number> when several routes share the same road." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.OrderingModeOption)), "Ordering Mode" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.OrderingModeOption)), "Choose whether route numbers keep <the order they were applied> or are <sorted> before display." },
                    { _settings.GetOrderingModeLocaleID(RouteNumberOrderingMode.InsertionOrder), "Insertion Order" },
                    { _settings.GetOrderingModeLocaleID(RouteNumberOrderingMode.Sorted), "Sorted" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.ShowAdvancedRouteDetails)), "Show Advanced Route Details" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.ShowAdvancedRouteDetails)), "Append <technical saved-route details> to the <Route Info> foldout in <Manage Routes>." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteShields)), "Enable Route Shields" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteShields)), "Show <route shield overlays> for saved routes. **A shield style must be selected for the route.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldSpacingOption)), "Route Shield Spacing" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldSpacingOption)), "Controls <how frequently> route shields appear along saved routes." },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSpacingPreset), nameof(RouteShieldSpacingPreset.Frequent)), "Frequent" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSpacingPreset), nameof(RouteShieldSpacingPreset.Moderate)), "Moderate" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSpacingPreset), nameof(RouteShieldSpacingPreset.Sparse)), "Sparse" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldSizeOption)), "Route Shield Size" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldSizeOption)), "Controls the <on-screen size> of route shield overlays." },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSizePreset), nameof(RouteShieldSizePreset.Small)), "Small" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSizePreset), nameof(RouteShieldSizePreset.Medium)), "Medium" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSizePreset), nameof(RouteShieldSizePreset.Large)), "Large" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteShieldSizePreset), nameof(RouteShieldSizePreset.VeryLarge)), "Very Large" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteShieldOcclusion)), "Low-Angle Shield Occlusion" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteShieldOcclusion)), "Fade route shields when <buildings> block them at <low camera angles>. **Terrain is not included.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldOcclusionAngle)), "Occlusion Camera Angle" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldOcclusionAngle)), "Camera angles <at or below this value> check whether buildings obstruct route shields." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteStatistics)), "Enable Route Statistics" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableRouteStatistics)), "Show on-demand <traffic flow> and <volume> graphs for saved routes. **Statistics are only calculated while their foldout is open.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsPresetOption)), "Calculation Preset" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsPresetOption)), "<Balanced> emphasizes representative busy-road performance.\n<Corridor Scale> emphasizes total route load.\n<Bottleneck Analysis> emphasizes congestion.\n<Custom> exposes every calculation control." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsVolumeModeOption)), "Primary Volume Calculation" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsVolumeModeOption)), "Choose the calculation used by the configured <Traffic Volume> graph." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsFlowModeOption)), "Primary Flow Calculation" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsFlowModeOption)), "Choose the calculation used by the configured <Traffic Flow> graph." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBusySegmentShare)), "Busiest Segment Share (%)" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBusySegmentShare)), "Percentage of the <highest-volume route segments> included in busiest-share averages." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBusyVolumePercentile)), "Busy Volume Percentile" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBusyVolumePercentile)), "Percentile used for the <Busy Volume> metric. **Higher values focus on busier route sections.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBottleneckSegmentShare)), "Bottleneck Search Share (%)" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsBottleneckSegmentShare)), "Search for the <lowest flow> only within this <highest-volume percentage> of the route." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsCongestionFlowThreshold)), "Congestion Flow Threshold (%)" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsCongestionFlowThreshold)), "A route segment <below this traffic-flow percentage> contributes to <Congested Distance>." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsRefreshRateOption)), "Refresh Rate" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.StatisticsRefreshRateOption)), "<Fast> refreshes every <128 simulation frames>, <Balanced> every <256>, and <Efficient> every <512>.\n**Calculations remain inactive while the foldout is closed.**" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsPreset), nameof(RouteStatisticsPreset.Balanced)), "Balanced" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsPreset), nameof(RouteStatisticsPreset.CorridorScale)), "Corridor Scale" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsPreset), nameof(RouteStatisticsPreset.BottleneckAnalysis)), "Bottleneck Analysis" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsPreset), nameof(RouteStatisticsPreset.Custom)), "Custom" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteVolumeAggregationMode), nameof(RouteVolumeAggregationMode.BusyPercentile)), "Busy Volume Percentile" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteVolumeAggregationMode), nameof(RouteVolumeAggregationMode.BusiestShareAverage)), "Busiest-Share Average" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteVolumeAggregationMode), nameof(RouteVolumeAggregationMode.AllSegmentsAverage)), "All-Segment Average" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteVolumeAggregationMode), nameof(RouteVolumeAggregationMode.CorridorLoad)), "Corridor Load" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteFlowAggregationMode), nameof(RouteFlowAggregationMode.VolumeWeighted)), "Volume-Weighted Flow" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteFlowAggregationMode), nameof(RouteFlowAggregationMode.BusiestShareAverage)), "Busiest-Share Average" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteFlowAggregationMode), nameof(RouteFlowAggregationMode.AllSegmentsAverage)), "All-Segment Average" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteFlowAggregationMode), nameof(RouteFlowAggregationMode.Bottleneck)), "Bottleneck Flow" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsRefreshRate), nameof(RouteStatisticsRefreshRate.Fast)), "Fast" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsRefreshRate), nameof(RouteStatisticsRefreshRate.Balanced)), "Balanced" },
                    { _settings.GetStatisticsEnumLocaleID(nameof(RouteStatisticsRefreshRate), nameof(RouteStatisticsRefreshRate.Efficient)), "Efficient" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.ToggleRenameBinding)), "Toggle Rename Mode" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.ToggleRenameBinding)), "Open or close Advanced Road Naming in <Rename mode>." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.ToggleRoutesBinding)), "Toggle Routes Menu" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.ToggleRoutesBinding)), "Open or close the <Advanced Road Routes> menu." },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableLogging)), "Enable Debug Logging" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableLogging)), "Write <verbose diagnostic logs> for Advanced Road Naming.\n**Leave this unchecked during normal play.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.CombineRoadAggregates)), "Combine Road Aggregates" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.CombineRoadAggregates)), "Combine and protect <contiguous compatible road aggregates> created by rename and numbered route operations.\n**Highly recommended to keep this enabled.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableSavedRenameRoutes)), "Enable Saved Rename Routes" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableSavedRenameRoutes)), "Save <renamed road corridors> so they can be reviewed, manipulated, and reapplied. **Disable for the older direct-renaming workflow.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RefreshCustomRouteShields)), "Refresh Route Shields" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RefreshCustomRouteShields)), "Reload <bundled> and <custom> SVG and JSON route shield definitions.\n**Custom shields are a work in progress.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.OpenCustomRouteShieldFolder)), "Open ModsData Folder" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.OpenCustomRouteShieldFolder)), "Open the <ModsData folder> where custom route shield <SVG> and <JSON files> are stored." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.OpenCustomRouteShieldDesigner)), "Open Route Shield Designer" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.OpenCustomRouteShieldDesigner)), "Open the <custom route shield form> in your default web browser. **The designer is a work in progress.**" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldCatalogStatus)), "Route Shield Catalog Status" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteShieldCatalogStatus)), "Loaded <built-in> and <custom definition counts>,\n**validation errors, and the latest error.**" },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.Version)), "Version" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.Version)), "Installed <Advanced Road Naming version>." },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Reset General Settings" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Restore <all Advanced Road Naming settings> to their <default values>." },
                    { _settings.GetOptionWarningLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Reset all Advanced Road Naming settings to defaults?" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RemoveRoadRouteModeData)), "Remove ROAD ROUTE Mode Data" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RemoveRoadRouteModeData)), "Remove <saved route-number records> and <route-number metadata> from roads in the current save.\n**Road-name rename data is preserved.**" },
                    { _settings.GetOptionWarningLocaleID(nameof(AdvancedRoadNamingSettings.RemoveRoadRouteModeData)), "Remove all ROAD ROUTE mode data from roads in the current save? Save the game afterwards to persist this cleanup." },

                    { UIKeys.ClosePanelTooltip, "Close the Advanced Road Naming panel." },
                    { UIKeys.RenameToolClickAddWaypointTooltip, "Click to add waypoint" },
                    { UIKeys.RenameToolDragMoveWaypointTooltip, "Drag to move waypoint" },
                    { UIKeys.UndoWaypointTooltip, "Remove the last committed waypoint from the current route." },
                    { UIKeys.ClearTooltip, "Clear the current route draft, including waypoints and input." },
                    { UIKeys.ApplyTooltip, "Apply the current route configuration to the selected route." },
                    { UIKeys.AdvancedRoadRoutesTooltip, "Advanced Road Routes (WIP)." },
                    { UIKeys.AdvancedRoadRoutesWip, "Work in progress (WIP)" },
                    { UIKeys.PrefixDescriptionM, "Motorway / Highway - Carries the most traffic in your city. High-speed, limited-access roads." },
                    { UIKeys.PrefixDescriptionA, "A-Road - Major arterial road connecting districts and suburbs. High traffic volume." },
                    { UIKeys.PrefixDescriptionB, "B-Road - Secondary road serving as an alternative to A-roads. Moderate traffic volume." },
                    { UIKeys.PrefixDescriptionC, "C-Road - Minor road connecting smaller points of interest. Low to moderate traffic." },
                    { UIKeys.PrefixDescriptionI, "Interstate Highway - Part of the American Interstate Highway System." },
                    { UIKeys.CustomPrefixTooltip, "Use a custom route prefix that you type yourself. Include punctuation in the prefix when needed, for example X-." },
                    { UIKeys.PositionBeforeTooltip, "Show the route number before the road name, for example M1 - Northern Hwy." },
                    { UIKeys.PositionAfterTooltip, "Show the route number after the road name, for example Northern Hwy - M1." },
                    { UIKeys.CustomRoutePrefixAria, "Custom route prefix" },
                    { UIKeys.AutoRouteNumberTooltip, "Pick the next available route number for the selected prefix." },
                    { UIKeys.CustomRouteNumberAria, "Custom route number" },
                    { UIKeys.RouteStatistics, "Statistics" },
                    { UIKeys.RouteStatisticsTrafficFlow, "Traffic Flow" },
                    { UIKeys.RouteStatisticsTrafficVolume, "Traffic Volume" },
                    { UIKeys.RouteStatisticsLoading, "Loading route statistics... If this takes a long time, reopen the panel." },
                    { UIKeys.RouteStatisticsNoValidRoads, "No valid road segments are available for this route." },
                    { UIKeys.RouteStatisticsExcludedRoads, "{EXCLUDED} of {TOTAL} stored road segments could not be included." },
                    { UIKeys.RouteStatisticsUnavailable, "Route statistics are unavailable for this game version." },
                    { UIKeys.RouteStatisticsConfigured, "Configured" },
                    { UIKeys.RouteStatisticsBusyVolumePercentile, "Busy Volume Percentile" },
                    { UIKeys.RouteStatisticsBusiestShareAverage, "Busiest-Share Average" },
                    { UIKeys.RouteStatisticsAllSegmentAverage, "All-Segment Average" },
                    { UIKeys.RouteStatisticsBusyVolume, "Busy Volume" },
                    { UIKeys.RouteStatisticsCorridorLoad, "Corridor Total" },
                    { UIKeys.RouteStatisticsVolumeWeightedFlow, "Volume-Weighted Flow" },
                    { UIKeys.RouteStatisticsBottleneckFlow, "Bottleneck Flow" },
                    { UIKeys.RouteStatisticsCongestedDistance, "Congested Distance" },
                    { UIKeys.RouteStatisticsConfiguredFlowTooltip, "Uses the traffic-flow calculation selected on the Statistics settings page." },
                    { UIKeys.RouteStatisticsConfiguredVolumeTooltip, "Uses the traffic-volume calculation selected on the Statistics settings page." },
                    { UIKeys.RouteStatisticsBusyVolumeTooltip, "Average of the high-volume percentile route segments"},
                    { UIKeys.RouteStatisticsCorridorLoadTooltip, "Sums activity across all route segments. Vehicles travelling through several segments contribute to each segment they cross." },
                    { UIKeys.RouteStatisticsVolumeWeightedFlowTooltip, "Weights each segment's traffic flow by its traffic volume, so the busiest parts of the route have the greatest influence." },
                    { UIKeys.RouteStatisticsBottleneckFlowTooltip, "Shows the lowest traffic flow among the route's busiest share of segments." },
                    { UIKeys.RouteStatisticsCongestedDistanceTooltip, "Shows the percentage of the route's road length below the configured traffic-flow threshold." },
                };
            }

            public void Unload()
            {
            }
        }
    }
}
