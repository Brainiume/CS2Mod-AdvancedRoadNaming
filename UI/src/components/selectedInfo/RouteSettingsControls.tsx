import { ChangeEvent, useEffect, useRef, useState } from "react";
import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button, PanelFoldout, PanelSectionRow } from "cs2/ui";
import { DelayedTooltip } from "components/DelayedTooltip";
import { PRESET_PREFIXES, ROUTE_SHIELD_STYLES } from "constants";
import { normalizeToken, parseRouteCode, useRouteCodeDraft } from "hooks/useRouteCodeDraft";
import { useAdvancedRoadNamingLocalization } from "localization";
import { PrefixType, RouteNumberPlacement, RouteShieldSelection, RouteShieldStyle } from "types";
import { RouteShieldBrowser } from "components/routeShields/RouteShieldBrowser";
import { RouteShieldPreview } from "components/routeShields/RouteShieldPreview";
import { findShieldDefinition, useRouteShieldCatalog } from "components/routeShields/shieldCatalog";
import styles from "./advancedRoadRoutesContent.module.scss";

interface RouteSettingsControlsProps {
    input: string;
    routeNumberPlacement: RouteNumberPlacement;
    routeShieldStyle: RouteShieldSelection;
    savedRouteInputs: string[];
    initialExpanded: boolean;
    autoShieldDefault?: boolean;
    onInputChange: (value: string) => void;
    onRouteNumberPlacementChange: (value: RouteNumberPlacement) => void;
    onRouteShieldStyleChange: (value: RouteShieldSelection) => void;
}

let sessionRecentShields: RouteShieldSelection[] = [];
const MAX_RECENT_CATALOG_SHIELDS = 5;

export function RouteSettingsControls(props: RouteSettingsControlsProps) {
    const { t } = useAdvancedRoadNamingLocalization();
    const { draft, updateDraft, composed } = useRouteCodeDraft(props.input, props.onInputChange);
    const previousAutoShieldRef = useRef<RouteShieldStyle>(defaultShieldForPrefix(draft.prefixType));
    const manualShieldSelectionRef = useRef(false);
    const [shieldBrowserOpen, setShieldBrowserOpen] = useState(false);
    const [recentShields, setRecentShields] = useState<RouteShieldSelection[]>(sessionRecentShields);
    const shieldCatalog = useRouteShieldCatalog();
    const resultPreview = composed
        ? props.routeNumberPlacement === "BeforeBaseName"
            ? `${composed} - Base name`
            : `Base name - ${composed}`
        : "Enter route number";

    const setPrefix = (prefixType: PrefixType) => {
        const nextAutoShield = defaultShieldForPrefix(prefixType);
        const previousAutoShield = previousAutoShieldRef.current;
        updateDraft({
            ...draft,
            prefixType,
            customPrefix: prefixType === "Custom" ? draft.customPrefix : "",
        });
        if (props.autoShieldDefault && !manualShieldSelectionRef.current && (props.routeShieldStyle === "None" || props.routeShieldStyle === previousAutoShield)) {
            props.onRouteShieldStyleChange(nextAutoShield);
        }
        previousAutoShieldRef.current = nextAutoShield;
    };

    const setCustomPrefix = (value: string) => {
        updateDraft({
            ...draft,
            prefixType: "Custom",
            customPrefix: normalizeToken(value),
        });
        if (props.autoShieldDefault && !manualShieldSelectionRef.current && props.routeShieldStyle === previousAutoShieldRef.current) {
            props.onRouteShieldStyleChange("None");
        }
        previousAutoShieldRef.current = "None";
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

    const setShieldStyle = (value: RouteShieldSelection) => {
        manualShieldSelectionRef.current = true;
        props.onRouteShieldStyleChange(value);
    };

    const selectCatalogShield = (value: RouteShieldSelection) => {
        setShieldStyle(value);
        sessionRecentShields = [value, ...sessionRecentShields.filter((item) => item !== value)].slice(0, MAX_RECENT_CATALOG_SHIELDS);
        setRecentShields(sessionRecentShields);
        setShieldBrowserOpen(false);
    };

    const fixedShieldStyles = new Set<RouteShieldSelection>(ROUTE_SHIELD_STYLES);
    const currentCatalogShield = fixedShieldStyles.has(props.routeShieldStyle)
        ? undefined
        : findShieldDefinition(props.routeShieldStyle, shieldCatalog);
    const quickCatalogStyles = [...new Set([
        ...(currentCatalogShield ? [currentCatalogShield.id] : []),
        ...recentShields.filter((style) => !fixedShieldStyles.has(style)),
    ])].slice(0, MAX_RECENT_CATALOG_SHIELDS);

    useEffect(() => {
        const defaultShield = defaultShieldForPrefix(draft.prefixType);
        previousAutoShieldRef.current = defaultShield;
        if (props.autoShieldDefault && !manualShieldSelectionRef.current && props.routeShieldStyle === "None" && defaultShield !== "None") {
            props.onRouteShieldStyleChange(defaultShield);
        }
    }, [draft.prefixType, props.autoShieldDefault, props.routeShieldStyle, props.onRouteShieldStyleChange]);

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
                header={<PanelSectionRow uppercase={true} disableFocus={true} left="Route Shield" />}
                initialExpanded={props.initialExpanded}
                focusKey={FOCUS_AUTO}
            >
                <PanelSectionRow
                    className={`${styles.routePanelRow} ${styles.controlOnlyRow} ${styles.routeShieldRow}`}
                    disableFocus={true}
                    subRow={true}
                    left={(
                        <FocusDisabled>
                            <div className={styles.routeShieldControls}>
                                <div className={styles.routeShieldGrid}>
                                    {ROUTE_SHIELD_STYLES.map((style) => (
                                        <div className={styles.routeShieldCell} key={style}>
                                            <DelayedTooltip tooltip={t("AdvancedRoadNaming.UI[RouteShieldTooltip]")}>
                                            <Button
                                                variant="flat"
                                                focusKey={FOCUS_AUTO}
                                                aria-label={shieldStyleLabel(style)}
                                                className={`${styles.routeChoiceButton} ${styles.routeShieldButton} ${isSelectedShieldStyle(props.routeShieldStyle, style) ? styles.active : ""}`}
                                                onSelect={() => setShieldStyle(style)}
                                            >
                                                <AustralianShieldPreview style={style} routeCode={composed} catalog={shieldCatalog} />
                                            </Button>
                                            </DelayedTooltip>
                                        </div>
                                    ))}
                                    {quickCatalogStyles.map((style) => {
                                        const definition = findShieldDefinition(style, shieldCatalog);
                                        return definition ? (
                                            <div className={styles.routeShieldCell} key={style}>
                                                <DelayedTooltip tooltip={definition.name}>
                                                    <Button
                                                        variant="flat"
                                                        focusKey={FOCUS_AUTO}
                                                        aria-label={definition.name}
                                                        className={`${styles.routeChoiceButton} ${styles.routeShieldButton} ${props.routeShieldStyle === style ? styles.active : ""}`}
                                                        onSelect={() => selectCatalogShield(style)}
                                                    >
                                                        <RouteShieldPreview definition={definition} routeCode={composed} compact={true} />
                                                    </Button>
                                                </DelayedTooltip>
                                            </div>
                                        ) : null;
                                    })}
                                </div>
                                <Button
                                    variant="flat"
                                    focusKey={FOCUS_AUTO}
                                    className={styles.browseShieldsButton}
                                    onSelect={() => setShieldBrowserOpen(true)}
                                >
                                    Browse shields...
                                </Button>
                            </div>
                        </FocusDisabled>
                    )}
                />
            </PanelFoldout>

            {shieldBrowserOpen && (
                <RouteShieldBrowser
                    routeCode={composed}
                    selected={props.routeShieldStyle}
                    onSelect={selectCatalogShield}
                    onClose={() => setShieldBrowserOpen(false)}
                />
            )}

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

function defaultShieldForPrefix(prefixType: PrefixType): RouteShieldStyle {
    switch (prefixType) {
        case "M":
        case "A":
        case "B":
        case "C":
            return "AustralianARectangle";
        default:
            return "None";
    }
}

function shieldStyleLabel(style: RouteShieldStyle): string {
    switch (style) {
        case "AustralianMRectangle":
        case "AustralianARectangle":
        case "AustralianBRectangle":
        case "AustralianCRectangle":
            return "Australian rectangle";
        case "AustralianNationalShield":
            return "National shield";
        case "BlueHighwayShield":
            return "Blue highway shield";
        case "BlackWhiteShield":
            return "Black-white shield";
        default:
            return "No shield";
    }
}

function AustralianShieldPreview({ style, routeCode, catalog }: { style: RouteShieldStyle; routeCode: string; catalog: ReturnType<typeof useRouteShieldCatalog> }) {
    const code = (routeCode || "1").trim().toUpperCase();

    if (style === "None") {
        return <div className={styles.noShieldPreview}><div className={styles.noShieldSlash} /></div>;
    }

    if (style === "AustralianMRectangle"
        || style === "AustralianARectangle"
        || style === "AustralianBRectangle"
        || style === "AustralianCRectangle") {
        return <div className={styles.australianRectanglePreview}>{code}</div>;
    }

    const definition = findShieldDefinition(style, catalog);
    return definition ? <RouteShieldPreview definition={definition} routeCode={code} compact={true} /> : null;
}

function isSelectedShieldStyle(current: RouteShieldSelection, option: RouteShieldStyle): boolean {
    if (option === "AustralianARectangle") {
        return current === "AustralianMRectangle"
            || current === "AustralianARectangle"
            || current === "AustralianBRectangle"
            || current === "AustralianCRectangle";
    }

    return current === option;
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
    if (prefix === "C") {
        return t("AdvancedRoadNaming.UI[PrefixDescriptionC]");
    }

    return t("AdvancedRoadNaming.UI[PrefixDescriptionI]");
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
