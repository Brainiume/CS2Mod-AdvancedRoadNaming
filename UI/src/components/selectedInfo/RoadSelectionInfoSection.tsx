import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { getModule } from "cs2/modding";
import { ButtonProps, PanelSection, PanelSectionRow } from "cs2/ui";
import { openAdvancedRoadNamingPanel, openAdvancedRoadRoutesPanel } from "bindings";
import { useAdvancedRoadNamingLocalization } from "localization";
import styles from "./roadSelectionInfoSection.module.scss";

interface ToolButtonProps extends ButtonProps {
    src: string;
    tooltip?: string;
}

const ToolButton = getModule(
    "game-ui/game/components/tool-options/tool-button/tool-button.tsx",
    "ToolButton",
) as React.FC<ToolButtonProps>;

interface RoadSelectionInfoSectionProps {
    segmentIndex: number;
    roadName: string;
    routeNumbers: string;
    hasAdvancedRoadNamingData: boolean;
}

export const extendRoadSelectionInfoSection = (componentList: Record<string, any>) => {
    componentList["AdvancedRoadNaming.Systems.RoadSelectionInfoSectionSystem"] = RoadSelectionInfoSection;
    return componentList;
};

function RoadSelectionInfoSection(props: RoadSelectionInfoSectionProps) {
    const { t } = useAdvancedRoadNamingLocalization();

    return (
        <>
            <PanelSection>
                <PanelSectionRow
                    uppercase={true}
                    disableFocus={true}
                    left="Road Naming Tools"
                    right={
                        <FocusDisabled>
                            <div className={styles.headerActions}>
                                <ToolButton
                                    id="rst-selected-road-name"
                                    focusKey={FOCUS_AUTO}
                                    src="coui://rst/PencilEdit.svg"
                                    tooltip="Advanced Road Naming"
                                    onSelect={openAdvancedRoadNamingPanel}
                                />
                                <ToolButton
                                    id="rst-selected-route-number"
                                    focusKey={FOCUS_AUTO}
                                    src="coui://rst/Route.svg"
                                    tooltip={t("AdvancedRoadNaming.UI[AdvancedRoadRoutesTooltip]")}
                                    onSelect={openAdvancedRoadRoutesPanel}
                                />
                            </div>
                        </FocusDisabled>
                    }
                />
            </PanelSection>
            <PanelSection>
                <PanelSectionRow
                    disableFocus={true}
                    left={
                        <span className={styles.roadSummary}>
                           {/* <img className={styles.logo} src={ICON_SRC} /> */}
                            <span>{props.roadName || `Road segment ${props.segmentIndex}`}</span>
                        </span>
                    }
                    right={props.routeNumbers || "No route"}
                    tooltip={props.hasAdvancedRoadNamingData ? `This road is apart of the ${props.routeNumbers} route` : "This road is not apart of any routes"}
                />
            </PanelSection>
        </>
    );
}
