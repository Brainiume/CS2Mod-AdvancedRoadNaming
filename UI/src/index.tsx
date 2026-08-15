import { ModRegistrar, ModuleRegistry } from "cs2/modding";
import { initialize } from "components/vanilla/Components";
import { extendRoadSelectionInfoSection } from "components/selectedInfo/RoadSelectionInfoSection";
import { AdvancedRoadNamingPanel } from "components/selectedInfo/AdvancedRoadNamingPanel";
import { RouteShieldOverlay } from "components/routeShields/RouteShieldOverlay";


const register: ModRegistrar = (moduleRegistry: ModuleRegistry) => {
    initialize(moduleRegistry);
    moduleRegistry.extend(
        "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
        "selectedInfoSectionComponents",
        extendRoadSelectionInfoSection,
    );
    moduleRegistry.append("Game", AdvancedRoadNamingPanel);
    moduleRegistry.append("Game", RouteShieldOverlay);
};

export default register;
