import { RouteShieldDefinition, RouteShieldOverlayItem } from "types";

export function isAustralianRectangle(style: string): boolean {
    return style === "AustralianARectangle" || style === "AustralianMRectangle"
        || style === "AustralianBRectangle" || style === "AustralianCRectangle";
}

export function rectangleWidth(label: string): number {
    return Math.max(42, label.length * 11 + 18);
}

interface Bounds { left: number; top: number; right: number; bottom: number; }

// Keep world positions fixed. At junctions/low zoom, omit colliding artwork instead of
// moving it away from its road. The backend orders the active edit before saved routes.
export function selectNonOverlappingShields(
    shields: RouteShieldOverlayItem[], catalog: RouteShieldDefinition[], rem: number,
): RouteShieldOverlayItem[] {
    const definitions = new Map(catalog.map((definition) => [definition.id, definition]));
    const accepted: RouteShieldOverlayItem[] = [];
    const cells = new Map<string, Bounds[]>();
    const cellSize = 96;
    const padding = 6;
    const unit = Number.isFinite(rem) && rem > 0 ? rem : 1;
    for (const shield of shields) {
        if (shield.occluded || !Number.isFinite(shield.left) || !Number.isFinite(shield.top)) continue;
        const rectangle = isAustralianRectangle(shield.style);
        const definition = definitions.get(shield.style);
        if (!rectangle && !definition) continue;
        const scale = Number.isFinite(shield.scale) && shield.scale > 0 ? shield.scale : 1;
        const halfWidth = (rectangle ? rectangleWidth(shield.label) : definition!.widthRem) * unit * scale / 2 + padding;
        const halfHeight = (rectangle ? 30 : definition!.heightRem) * unit * scale / 2 + padding;
        const bounds = { left: shield.left - halfWidth, right: shield.left + halfWidth,
            top: shield.top - halfHeight, bottom: shield.top + halfHeight };
        const minX = Math.floor(bounds.left / cellSize), maxX = Math.floor(bounds.right / cellSize);
        const minY = Math.floor(bounds.top / cellSize), maxY = Math.floor(bounds.bottom / cellSize);
        let overlaps = false;
        for (let y = minY; y <= maxY && !overlaps; y++) {
            for (let x = minX; x <= maxX && !overlaps; x++) {
                overlaps = (cells.get(`${x},${y}`) ?? []).some((other) =>
                    bounds.left < other.right && bounds.right > other.left
                    && bounds.top < other.bottom && bounds.bottom > other.top);
            }
        }
        if (overlaps) continue;
        accepted.push(shield);
        for (let y = minY; y <= maxY; y++) {
            for (let x = minX; x <= maxX; x++) {
                const key = `${x},${y}`;
                const bucket = cells.get(key);
                if (bucket) bucket.push(bounds);
                else cells.set(key, [bounds]);
            }
        }
    }
    return accepted;
}
