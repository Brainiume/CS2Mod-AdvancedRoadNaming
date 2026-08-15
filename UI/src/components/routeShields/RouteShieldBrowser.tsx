import React, { useMemo, useState } from "react";
import { FOCUS_AUTO, FocusDisabled } from "cs2/input";
import { Button, Portal } from "cs2/ui";
import { DelayedTooltip } from "components/DelayedTooltip";
import { RouteShieldSelection } from "types";
import {
    closeButtonClass,
    closeButtonImageClass,
    panelModule,
    selectedInfoThemeModule,
    selectedInfoWrapperClass,
} from "components/selectedInfo/selectedInfoPanelStyles";
import { RouteShieldPreview } from "./RouteShieldPreview";
import { ShieldSystem, useRouteShieldCatalog } from "./shieldCatalog";
import styles from "./routeShieldBrowser.module.scss";

interface RouteShieldBrowserProps {
    routeCode: string;
    selected: RouteShieldSelection;
    onSelect: (style: RouteShieldSelection) => void;
    onClose: () => void;
}

const SYSTEMS: ShieldSystem[] = ["National", "Motorway", "State", "Custom"];
const availableSystems = SYSTEMS;

export function RouteShieldBrowser(props: RouteShieldBrowserProps) {
    const [region, setRegion] = useState<string>("");
    const [system, setSystem] = useState<ShieldSystem | "">("");
    const catalog = useRouteShieldCatalog();
    const regions = useMemo(() => Array.from(new Set(catalog.map((shield) => shield.region))), [catalog]);
    const shields = useMemo(() => catalog.filter((shield) =>
        (!region || shield.region === region) && (!system || shield.system === system)), [catalog, region, system]);

    return (
        <Portal>
            <div className={styles.browserPosition}>
                <div className={`${selectedInfoWrapperClass} ${styles.browserPanel}`}>
                    <div className={selectedInfoThemeModule.header}>
                        <div className={panelModule.titleBar}>
                            <div className={selectedInfoThemeModule.title}>Route Shields</div>
                            <button className={closeButtonClass} onClick={props.onClose} aria-label="Close shield browser">
                                <div className={closeButtonImageClass} style={{ maskImage: "url(Media/Glyphs/Close.svg)" }} />
                            </button>
                        </div>
                    </div>
                    <div className={`${selectedInfoThemeModule.content} ${styles.browserContent}`}>
                        <FocusDisabled>
                            <div className={styles.filters}>
                                <div className={styles.filterLabel}>Country or region</div>
                                <div className={styles.chipRow}>
                                    <Button
                                        variant="flat"
                                        focusKey={FOCUS_AUTO}
                                        className={`${styles.filterChip} ${!region ? styles.filterChipSelected : ""}`}
                                        onSelect={() => setRegion("")}
                                    >
                                        All
                                    </Button>
                                    {regions.map((value) => (
                                        <Button
                                            key={value}
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            className={`${styles.filterChip} ${region === value ? styles.filterChipSelected : ""}`}
                                            onSelect={() => setRegion(value)}
                                        >
                                            {value}
                                        </Button>
                                    ))}
                                </div>
                                <div className={styles.filterLabel}>Road system</div>
                                <div className={styles.chipRow}>
                                    <Button
                                        variant="flat"
                                        focusKey={FOCUS_AUTO}
                                        className={`${styles.filterChip} ${!system ? styles.filterChipSelected : ""}`}
                                        onSelect={() => setSystem("")}
                                    >
                                        All
                                    </Button>
                                    {availableSystems.map((value) => (
                                        <Button
                                            key={value}
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            className={`${styles.filterChip} ${system === value ? styles.filterChipSelected : ""}`}
                                            onSelect={() => setSystem(value)}
                                        >
                                            {value}
                                        </Button>
                                    ))}
                                </div>
                            </div>
                            <div className={styles.previewGrid}>
                                {shields.map((shield) => (
                                    <DelayedTooltip key={shield.id} tooltip={shield.name}>
                                        <Button
                                            variant="flat"
                                            focusKey={FOCUS_AUTO}
                                            aria-label={shield.name}
                                            className={`${styles.previewButton} ${props.selected === shield.id ? styles.selected : ""}`}
                                            onSelect={() => props.onSelect(shield.id)}
                                        >
                                            <RouteShieldPreview definition={shield} routeCode={props.routeCode} />
                                        </Button>
                                    </DelayedTooltip>
                                ))}
                            </div>
                        </FocusDisabled>
                    </div>
                </div>
            </div>
        </Portal>
    );
}
