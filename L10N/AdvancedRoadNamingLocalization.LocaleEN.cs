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

                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.DisplayGroup), "Display" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.AdvancedGroup), "Advanced" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.AboutGroup), "About" },
                    { _settings.GetOptionGroupLocaleID(AdvancedRoadNamingSettings.ResetGroup), "Reset" },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.BaseRouteSeparator)), "Base Route Separator" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.BaseRouteSeparator)), "Text inserted between the preserved base street name and the rendered route numbers." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RouteNumberSeparator)), "Route Number Separator" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RouteNumberSeparator)), "Text inserted between multiple route numbers shown on the same road segment." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.AllowMultipleRouteNumbers)), "Allow Multiple Route Numbers" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.AllowMultipleRouteNumbers)), "Allow a segment to display more than one route number when several routes share the same road." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.OrderingMode)), "Ordering Mode" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.OrderingMode)), "Choose whether route numbers keep the order they were applied or are sorted before display." },
                    { _settings.GetOrderingModeLocaleID(RouteNumberOrderingMode.InsertionOrder), "Insertion Order" },
                    { _settings.GetOrderingModeLocaleID(RouteNumberOrderingMode.Sorted), "Sorted" },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.EnableLogging)), "Enable Debug Logging" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.EnableLogging)), "Write verbose diagnostic logs for Advanced Road Naming. Leave this unchecked during normal play." },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.CombineRoadAggregates)), "Combine Road Aggregates" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.CombineRoadAggregates)), "Combine and protect contiguous compatible road aggregates created by rename and numbered route operations." },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.Version)), "Version" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.Version)), "Installed Advanced Road Naming version." },

                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Reset General Settings" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Restore all Advanced Road Naming settings to their default values." },
                    { _settings.GetOptionWarningLocaleID(nameof(AdvancedRoadNamingSettings.ResetGeneralSettings)), "Reset all Advanced Road Naming settings to defaults?" },
                    { _settings.GetOptionLabelLocaleID(nameof(AdvancedRoadNamingSettings.RemoveRoadRouteModeData)), "Remove ROAD ROUTE Mode Data" },
                    { _settings.GetOptionDescLocaleID(nameof(AdvancedRoadNamingSettings.RemoveRoadRouteModeData)), "Remove saved route-number records and route-number metadata from roads in the current save. Road-name rename data is preserved." },
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
                    { UIKeys.CustomPrefixTooltip, "Use a custom route prefix that you type yourself." },
                    { UIKeys.PositionBeforeTooltip, "Show the route number before the road name, for example M1 - Northern Hwy." },
                    { UIKeys.PositionAfterTooltip, "Show the route number after the road name, for example Northern Hwy - M1." },
                    { UIKeys.CustomRoutePrefixAria, "Custom route prefix" },
                    { UIKeys.AutoRouteNumberTooltip, "Pick the next available route number for the selected prefix." },
                    { UIKeys.CustomRouteNumberAria, "Custom route number" },
                };
            }

            public void Unload()
            {
            }
        }
    }
}
