import { ChangeEvent, CompositionEvent, FocusEvent, useEffect, useRef, useState } from "react";
import { VC } from "components/vanilla/Components";

const imeEmptyInputInitializer = "\u2060";

interface RoadNameTextInputProps {
    value: string;
    className: string;
    debugName: string;
    allowEmpty?: boolean;
    onChange: (value: string) => void;
    onDraftChange?: (value: string) => void;
}

export function RoadNameTextInput(props: RoadNameTextInputProps) {
    const [draft, setDraft] = useState(props.value);
    const composingRef = useRef(false);
    const focusedRef = useRef(false);

    useEffect(() => {
        if (!focusedRef.current && !composingRef.current) {
            setDraft(props.value);
            props.onDraftChange?.(props.value);
        }
    }, [props.value]);

    const publish = (value: string) => {
        const normalized = normalizeInputValue(value);
        setDraft(normalized);
        props.onDraftChange?.(normalized);
        if (props.allowEmpty !== false || normalized.trim().length > 0) {
            props.onChange(normalized);
        }
    };

    const onInputChange = (event: ChangeEvent<HTMLInputElement>) => {
        const value = normalizeInputValue(event.currentTarget.value);
        setDraft(value);
        props.onDraftChange?.(value);
        if (!composingRef.current && (props.allowEmpty !== false || value.trim().length > 0)) {
            props.onChange(value);
        }
    };

    const onCompositionStart = () => {
        composingRef.current = true;
    };

    const onCompositionEnd = (event: CompositionEvent<HTMLInputElement>) => {
        composingRef.current = false;
        publish(event.currentTarget.value);
    };

    const onInputFocus = () => {
        focusedRef.current = true;
    };

    const onInputBlur = (event: FocusEvent<HTMLInputElement>) => {
        focusedRef.current = false;
        composingRef.current = false;
        const value = normalizeInputValue(event.currentTarget.value);
        if (props.allowEmpty === false && value.trim().length === 0) {
            setDraft(props.value);
            props.onDraftChange?.(props.value);
            return;
        }

        publish(value);
    };

    return (
        <VC.TextInput
            className={props.className}
            type="text"
            debugName={props.debugName}
            selectAllOnFocus={false}
            value={draft || imeEmptyInputInitializer}
            onChange={onInputChange}
            onCompositionStart={onCompositionStart}
            onCompositionEnd={onCompositionEnd}
            onFocus={onInputFocus}
            onBlur={onInputBlur}
        />
    );
}

function normalizeInputValue(value: string): string {
    return value.split(imeEmptyInputInitializer).join("");
}
