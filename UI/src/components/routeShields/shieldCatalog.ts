import { useMemo } from "react";
import { useValue } from "cs2/api";
import { routeShieldCatalog$ } from "bindings";
import { parseRouteCode } from "hooks/useRouteCodeDraft";
import { RouteShieldDefinition, RouteShieldSelection, RouteShieldTextLayerDefinition } from "types";

export type ShieldSystem = "National" | "Motorway" | "State" | "Custom";

const AUSTRALIAN_RECTANGLE: RouteShieldDefinition = {
    id: "AustralianARectangle",
    name: "Australian Alphanumeric Route",
    region: "Australia / New Zealand",
    system: "State",
    asset: "",
    assetRoot: "",
    widthRem: 42,
    heightRem: 30,
    textLayers: [],
};

export function useRouteShieldCatalog(): RouteShieldDefinition[] {
    const catalog = useValue(routeShieldCatalog$);
    return useMemo(() => [AUSTRALIAN_RECTANGLE, ...catalog], [catalog]);
}

export function findShieldDefinition(id: RouteShieldSelection, catalog: RouteShieldDefinition[]): RouteShieldDefinition | undefined {
    return catalog.find((shield) => shield.id === id);
}

export function resolveShieldLayerText(layer: RouteShieldTextLayerDefinition, routeCode: string): string {
    const normalized = (routeCode || "1").replace(/\s+/g, "").toUpperCase() || "1";
    const parsed = parseRouteCode(normalized);
    switch (layer.source) {
        case "routeCode":
            return normalized;
        case "prefix":
            return parsed.prefixType === "Custom" ? parsed.customPrefix : parsed.prefixType;
        case "number":
            return parsed.numberPart || normalized;
        case "literal":
            return layer.text;
        default:
            return "";
    }
}
