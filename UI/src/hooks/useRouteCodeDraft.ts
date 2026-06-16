import { useEffect, useState } from "react";
import { panelActions } from "bindings";
import { PRESET_PREFIXES } from "constants";
import { PrefixType, RouteCodeDraft } from "types";

const presetPrefixes = new Set<string>(PRESET_PREFIXES);

export function normalizeToken(value: string): string {
    return (value || "").replace(/\s+/g, "").toUpperCase();
}

export function parseRouteCode(value: string): RouteCodeDraft {
    const normalized = normalizeToken(value);
    if (!normalized) {
        return { prefixType: "M", customPrefix: "", numberPart: "" };
    }

    const prefix = normalized.charAt(0);
    if (presetPrefixes.has(prefix)) {
        return { prefixType: prefix as PrefixType, customPrefix: "", numberPart: normalized.substring(1) };
    }

    const customMatch = normalized.match(/^([A-Z]+)(.*)$/);
    if (customMatch) {
        return { prefixType: "Custom", customPrefix: customMatch[1], numberPart: customMatch[2] };
    }

    return { prefixType: "Custom", customPrefix: "", numberPart: normalized };
}

export function composeRouteCode(draft: RouteCodeDraft): string {
    const prefix = draft.prefixType === "Custom" ? normalizeToken(draft.customPrefix) : draft.prefixType;
    return `${prefix}${normalizeToken(draft.numberPart)}`;
}

export function useRouteCodeDraft(input: string, onChange: (value: string) => void = panelActions.setInput) {
    const [draft, setDraft] = useState<RouteCodeDraft>(() => parseRouteCode(input));

    useEffect(() => {
        setDraft(parseRouteCode(input));
    }, [input]);

    const updateDraft = (nextDraft: RouteCodeDraft) => {
        setDraft(nextDraft);
        onChange(composeRouteCode(nextDraft));
    };

    return { draft, updateDraft, composed: composeRouteCode(draft) };
}
