import { panelState$ } from "bindings";
import { DEFAULT_PANEL_STATE } from "constants";
import { useBindingValue } from "hooks/useBindingValue";
import { PanelState, RouteToolMode, SavedRoute } from "types";

const DEFAULT_STATE: PanelState = {
    isOpen: false,
    mode: "AssignMajorRouteNumber",
    input: "",
    selectedSegments: 0,
    hoveredSegment: "none",
    previewText: "",
    statusMessage: "",
    inGame: false,
    waypointCount: 0,
    savedRoutes: [],
    routeNumberPlacement: "AfterBaseName",
    undergroundMode: false,
    selectedSavedRouteId: 0,
    savedRoutesViewActive: false,
    savedRouteManipulateMode: false,
    savedRouteReviewRouteId: 0,
    showAdvancedRouteDetails: false,
};

const MODES: Record<string, RouteToolMode> = {
    RenameSelectedSegments: "RenameSelectedSegments",
    AssignMajorRouteNumber: "AssignMajorRouteNumber",
};

function splitState(raw: string): string[] {
    const parts: string[] = [];
    let current = "";

    for (let index = 0; index < raw.length; index++) {
        const char = raw[index];
        if (char === "\\") {
            const next = raw[index + 1];
            if (next === "p") {
                current += "|";
                index++;
                continue;
            }
            if (next === "n") {
                current += "\n";
                index++;
                continue;
            }
            if (next) {
                current += next;
                index++;
                continue;
            }
        }

        if (char === "|") {
            parts.push(current);
            current = "";
        } else {
            current += char;
        }
    }

    parts.push(current);
    return parts;
}

function toNumber(value: string): number {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
}

function parseSavedRoutes(raw: string): SavedRoute[] {
    try {
        const parsed = JSON.parse(raw || "[]") as SavedRoute[];
        return Array.isArray(parsed) ? parsed : [];
    } catch {
        return [];
    }
}

export function parsePanelState(raw: string): PanelState {
    const parts = splitState(raw || "");
    return {
        isOpen: parts[0] === "1",
        mode: MODES[parts[1]] ?? DEFAULT_STATE.mode,
        input: parts[2] ?? "",
        selectedSegments: toNumber(parts[3] ?? "0"),
        hoveredSegment: parts[4] || "none",
        previewText: parts[5] ?? "",
        statusMessage: parts[6] ?? "",
        inGame: parts[7] === "1",
        waypointCount: toNumber(parts[8] ?? "0"),
        savedRoutes: parseSavedRoutes(parts[9] ?? "[]"),
        routeNumberPlacement: parts[10] === "BeforeBaseName" ? "BeforeBaseName" : "AfterBaseName",
        undergroundMode: parts[11] === "1",
        selectedSavedRouteId: toNumber(parts[12] ?? "0"),
        savedRoutesViewActive: parts[13] === "1",
        savedRouteManipulateMode: parts[14] === "1",
        savedRouteReviewRouteId: toNumber(parts[15] ?? "0"),
        showAdvancedRouteDetails: parts[16] === "1",
    };
}

export function usePanelState(): PanelState {
    const rawState = useBindingValue(panelState$, DEFAULT_PANEL_STATE);
    return parsePanelState(rawState);
}
