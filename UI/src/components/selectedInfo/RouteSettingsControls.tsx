import { ChangeEvent } from "react";
import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button, PanelFoldout, PanelSectionRow } from "cs2/ui";
import { DelayedTooltip } from "components/DelayedTooltip";
import { PRESET_PREFIXES } from "constants";
import { normalizeToken, parseRouteCode, useRouteCodeDraft } from "hooks/useRouteCodeDraft";
import { useAdvancedRoadNamingLocalization } from "localization";
import { PrefixType, RouteNumberPlacement } from "types";
import styles from "./advancedRoadRoutesContent.module.scss";

interface RouteSettingsControlsProps {
    input: string;
    routeNumberPlacement: RouteNumberPlacement;
    savedRouteInputs: string[];
    initialExpanded: boolean;
    onInputChange: (value: string) => void;
    onRouteNumberPlacementChange: (value: RouteNumberPlacement) => void;
}

export function RouteSettingsControls(props: RouteSettingsControlsProps) {
    const { t } = useAdvancedRoadNamingLocalization();
    const { draft, updateDraft, composed } = useRouteCodeDraft(props.input, props.onInputChange);
    const resultPreview = composed
        ? props.routeNumberPlacement === "BeforeBaseName"
            ? `${composed} - Base name`
            : `Base name - ${composed}`
        : "Enter route number";

    const setPrefix = (prefixType: PrefixType) => {
        updateDraft({
            ...draft,
            prefixType,
            customPrefix: prefixType === "Custom" ? draft.customPrefix : "",
        });
    };

    const setCustomPrefix = (value: string) => {
        updateDraft({
            ...draft,
            prefixType: "Custom",
            customPrefix: normalizeToken(value),
        });
    };

    const setNumber = (event: ChangeEvent<HTMLInputElement>) => {
        updateDraft({
            ...draft,
            numberPart: normalizeToken(event.currentTarget.value),
        });
    };

    const useAutoNumber = () => {
        updateDraft({
            ...draft,
            numberPart: nextRouteNumberForPrefix(draft.prefixType, draft.customPrefix, props.savedRouteInputs),
        });
    };

    return (
        <div className={styles.routeFoldouts}>
            <PanelFoldout
                header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Prefix" />}
                initialExpanded={props.initialExpanded}
                focusKey={FOCUS_AUTO}
            >
                <PanelSectionRow
                    className={`${styles.routePanelRow} ${styles.controlOnlyRow}`}
                    disableFocus={true}
                    subRow={true}
                    left={(
                        <FocusDisabled>
                            <div className={`${styles.routeButtonRow} ${styles.prefixButtonRow}`}>
                                {PRESET_PREFIXES.map((prefix) => (
                                    <div className={styles.routeButtonCell} key={prefix}>
                                        <DelayedTooltip tooltip={prefixDescription(prefix, t)}>
                                            <Button
                                                variant="flat"
                                                focusKey={FOCUS_AUTO}
                                                className={`${styles.routeChoiceButton} ${draft.prefixType === prefix ? styles.active : ""}`}
                                                onSelect={() => setPrefix(prefix)}
                                            >
                                                {prefix}
                                            </Button>
                                        </DelayedTooltip>
                                    </div>
                                ))}
                                <div className={`${styles.routeButtonCell} ${styles.routeButtonCellWide}`}>
                                    <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[CustomPrefixTooltip]")}>
                                        <Button
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            className={`${styles.routeChoiceButton} ${draft.prefixType === "Custom" ? styles.active : ""}`}
                                            onSelect={() => setPrefix("Custom")}
                                        >
                                            Custom
                                        </Button>
                                    </DelayedTooltip>
                                </div>
                            </div>
                        </FocusDisabled>
                    )}
                />
                {draft.prefixType === "Custom" && (
                    <PanelSectionRow
                        className={`${styles.routePanelRow} ${styles.controlOnlyRow}`}
                        disableFocus={true}
                        subRow={true}
                        left={(
                            <input
                                className={`${styles.textInput} ${styles.sectionTextInput}`}
                                type="text"
                                value={draft.customPrefix}
                                onChange={(event) => setCustomPrefix(event.currentTarget.value)}
                                aria-label={t("AdvancedRoadNaming.UI[CustomRoutePrefixAria]")}
                            />
                        )}
                    />
                )}
            </PanelFoldout>

            <PanelFoldout
                header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Position" />}
                initialExpanded={props.initialExpanded}
                focusKey={FOCUS_AUTO}
            >
                <PanelSectionRow
                    className={`${styles.routePanelRow} ${styles.controlOnlyRow}`}
                    disableFocus={true}
                    subRow={true}
                    left={(
                        <FocusDisabled>
                            <div className={`${styles.routeButtonRow} ${styles.positionButtonRow}`}>
                                <div className={styles.routeButtonCell}>
                                    <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[PositionBeforeTooltip]")}>
                                        <Button
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            className={`${styles.routeChoiceButton} ${props.routeNumberPlacement === "BeforeBaseName" ? styles.active : ""}`}
                                            onSelect={() => props.onRouteNumberPlacementChange("BeforeBaseName")}
                                        >
                                            Before
                                        </Button>
                                    </DelayedTooltip>
                                </div>
                                <div className={styles.routeButtonCell}>
                                    <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[PositionAfterTooltip]")}>
                                        <Button
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            className={`${styles.routeChoiceButton} ${props.routeNumberPlacement === "AfterBaseName" ? styles.active : ""}`}
                                            onSelect={() => props.onRouteNumberPlacementChange("AfterBaseName")}
                                        >
                                            After
                                        </Button>
                                    </DelayedTooltip>
                                </div>
                            </div>
                        </FocusDisabled>
                    )}
                />
            </PanelFoldout>

            <PanelFoldout
                header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Number" />}
                initialExpanded={props.initialExpanded}
                focusKey={FOCUS_AUTO}
            >
                <PanelSectionRow
                    className={`${styles.routePanelRow} ${styles.controlOnlyRow}`}
                    disableFocus={true}
                    subRow={true}
                    left={(
                        <div className={styles.sectionInputLine}>
                            <div className={styles.inlineActionCell}>
                                <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[AutoRouteNumberTooltip]")}>
                                    <Button
                                        variant="flat"
                                        className={styles.inlineActionButton}
                                        onSelect={useAutoNumber}
                                    >
                                        Auto
                                    </Button>
                                </DelayedTooltip>
                            </div>
                            <input
                                className={styles.textInput}
                                type="text"
                                value={draft.numberPart}
                                onChange={setNumber}
                                aria-label={t("AdvancedRoadNaming.UI[CustomRouteNumberAria]")}
                            />
                        </div>
                    )}
                />
            </PanelFoldout>

            <PanelFoldout
                header={<PanelSectionRow uppercase={true} disableFocus={true} left="Result" />}
                initialExpanded={props.initialExpanded}
                focusKey={FOCUS_AUTO}
            >
                <PanelSectionRow
                    className={styles.routePanelRow}
                    disableFocus={true}
                    subRow={true}
                    left="Preview"
                    right={<div className={styles.resultText}>{resultPreview}</div>}
                />
            </PanelFoldout>
        </div>
    );
}

function prefixDescription(prefix: string, t: (key: string) => string): string {
    if (prefix === "M") {
        return t("AdvancedRoadNaming.UI[PrefixDescriptionM]");
    }
    if (prefix === "A") {
        return t("AdvancedRoadNaming.UI[PrefixDescriptionA]");
    }
    if (prefix === "B") {
        return t("AdvancedRoadNaming.UI[PrefixDescriptionB]");
    }

    return t("AdvancedRoadNaming.UI[PrefixDescriptionC]");
}

function nextRouteNumberForPrefix(prefixType: PrefixType, customPrefix: string, routeInputs: string[]): string {
    const targetPrefix = prefixType === "Custom" ? normalizeToken(customPrefix) : prefixType;
    if (!targetPrefix) {
        return "1";
    }

    let highestNumber = 0;
    routeInputs.forEach((routeInput) => {
        const parsed = parseRouteCode(routeInput);
        const parsedPrefix = parsed.prefixType === "Custom" ? normalizeToken(parsed.customPrefix) : parsed.prefixType;
        const parsedNumber = Number(normalizeToken(parsed.numberPart));
        if (parsedPrefix === targetPrefix && Number.isFinite(parsedNumber) && parsedNumber > highestNumber) {
            highestNumber = parsedNumber;
        }
    });

    return (highestNumber + 1).toString();
}
