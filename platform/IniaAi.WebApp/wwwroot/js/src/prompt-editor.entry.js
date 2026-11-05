import { EditorView, Decoration, WidgetType, keymap, placeholder } from '@codemirror/view';
import { EditorState, StateEffect, StateField } from '@codemirror/state';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { autocompletion, completionKeymap } from '@codemirror/autocomplete';
import { grammarFor } from './tag-completions.js';

// Mirrors GET_PLACEHOLDERS in IniaAi.Common/Extensions/StringExtensions.cs
const TAG_PATTERN = /\{\{?[a-zA-Z0-9._\-\[\] &\/()%]+\}?\}/g;
const DEBOUNCE_MS = 250;

const instances = new WeakMap();
const chipClickHandlers = new WeakMap();

const setSignaturesEffect = StateEffect.define();
const setTagsEffect = StateEffect.define();

const signaturesField = StateField.define({
    create: () => ({}),
    update(value, tr) {
        for (const effect of tr.effects) {
            if (effect.is(setSignaturesEffect)) { return effect.value; }
        }
        return value;
    },
});

const tagsField = StateField.define({
    create: () => ({ available: [], invalidSet: new Set() }),
    update(value, tr) {
        for (const effect of tr.effects) {
            if (effect.is(setTagsEffect)) { return effect.value; }
        }
        return value;
    },
});

function tagCompletionsFor(grammar) {
    return (context) => {
        const partial = context.matchBefore(grammar.partialPattern);
        if (!partial || (partial.from === partial.to && !context.explicit)) { return null; }

        const tags = context.state.field(tagsField);
        const signatures = context.state.field(signaturesField);
        const names = new Set([...tags.available, ...Object.keys(signatures || {})]);
        if (names.size === 0) { return null; }

        return {
            from: partial.from,
            options: [...names].map((name) => ({
                label: grammar.label(name),
                type: signatures && signatures[name] ? 'keyword' : 'variable',
            })),
            validFor: grammar.validFor,
        };
    };
}

class SignatureChipWidget extends WidgetType {
    constructor(name, signature) {
        super();
        this.name = name;
        this.value = signature.value;
        this.isOverride = signature.isOverride;
        this.displayName = signature.displayName;
    }

    eq(other) {
        return other.name === this.name
            && other.value === this.value
            && other.isOverride === this.isOverride
            && other.displayName === this.displayName;
    }

    toDOM(view) {
        const chip = document.createElement('span');
        chip.className = 'cm-signature-chip' + (this.isOverride ? ' cm-signature-chip--override' : '');
        chip.textContent = this.value || '(no value yet)';
        chip.title = `${this.displayName || this.name} — ${this.isOverride ? 'client override' : 'default value'} · click to inspect`;
        chip.dataset.placeholder = this.name;
        chip.addEventListener('mousedown', (e) => e.preventDefault());
        chip.addEventListener('click', (e) => {
            e.preventDefault();
            const handler = chipClickHandlers.get(view);
            if (handler) { handler(this.name, chip); }
        });
        return chip;
    }

    ignoreEvent() {
        return true;
    }
}

function matchedTagName(rawMatch) {
    return rawMatch.replace(/^\{\{?/, '').replace(/\}?\}$/, '');
}

function buildPillDecorations(state) {
    const signatures = state.field(signaturesField);
    const invalidSet = state.field(tagsField).invalidSet;
    const text = state.doc.toString();
    const ranges = [];
    for (const m of text.matchAll(TAG_PATTERN)) {
        if (signatures[matchedTagName(m[0])]) { continue; }
        const cls = invalidSet.has(m[0]) ? 'cm-tag-pill cm-tag-pill--unknown' : 'cm-tag-pill';
        ranges.push(Decoration.mark({ class: cls }).range(m.index, m.index + m[0].length));
    }
    return Decoration.set(ranges, true);
}

function buildChipDecorations(state) {
    const signatures = state.field(signaturesField);
    const text = state.doc.toString();
    const ranges = [];
    for (const m of text.matchAll(TAG_PATTERN)) {
        const name = matchedTagName(m[0]);
        const signature = signatures[name];
        if (!signature) { continue; }
        ranges.push(Decoration.replace({
            widget: new SignatureChipWidget(name, signature),
        }).range(m.index, m.index + m[0].length));
    }
    return Decoration.set(ranges, true);
}

function decorationField(build, atomic) {
    return StateField.define({
        create: build,
        update(decorations, tr) {
            if (tr.docChanged || tr.effects.some((e) => e.is(setSignaturesEffect) || e.is(setTagsEffect))) {
                return build(tr.state);
            }
            return decorations;
        },
        provide: (field) => atomic
            ? [EditorView.decorations.from(field), EditorView.atomicRanges.of((view) => view.state.field(field))]
            : EditorView.decorations.from(field),
    });
}

const pillField = decorationField(buildPillDecorations, false);
const chipField = decorationField(buildChipDecorations, true);

function flushPending(instance) {
    if (instance.debounceTimer === null) { return; }
    clearTimeout(instance.debounceTimer);
    instance.debounceTimer = null;
    notifyDocChanged(instance);
}

function notifyDocChanged(instance) {
    const text = instance.view.state.doc.toString();
    if (text === instance.lastSent) { return; }
    instance.lastSent = text;
    instance.dotNetRef.invokeMethodAsync('OnPromptEditorDocChanged', text).catch(() => {});
}

window.iniaPromptEditor = {
    create(host, dotNetRef, options) {
        this.destroy(host);

        const instance = { view: null, dotNetRef, debounceTimer: null, lastSent: options.value || '' };

        const view = new EditorView({
            parent: host,
            state: EditorState.create({
                doc: options.value || '',
                extensions: [
                    history(),
                    keymap.of([...completionKeymap, ...defaultKeymap, ...historyKeymap]),
                    EditorView.lineWrapping,
                    EditorView.contentAttributes.of({ 'aria-label': options.ariaLabel || '' }),
                    placeholder(options.ariaLabel || ''),
                    signaturesField.init(() => options.signatures || {}),
                    tagsField,
                    autocompletion({ override: [tagCompletionsFor(grammarFor(options.tagGrammar))], icons: false }),
                    pillField,
                    chipField,
                    EditorView.updateListener.of((update) => {
                        if (!update.docChanged) { return; }
                        if (instance.debounceTimer !== null) { clearTimeout(instance.debounceTimer); }
                        instance.debounceTimer = setTimeout(() => {
                            instance.debounceTimer = null;
                            notifyDocChanged(instance);
                        }, DEBOUNCE_MS);
                    }),
                    EditorView.domEventHandlers({
                        blur: () => { flushPending(instance); },
                    }),
                ],
            }),
        });

        instance.view = view;
        chipClickHandlers.set(view, (name, chipEl) => {
            const hostRect = host.getBoundingClientRect();
            const chipRect = chipEl.getBoundingClientRect();
            dotNetRef.invokeMethodAsync(
                'OnPromptEditorChipClicked',
                name,
                chipRect.left - hostRect.left,
                chipRect.bottom - hostRect.top,
            ).catch(() => {});
        });
        instances.set(host, instance);
    },

    setValue(host, value) {
        const instance = instances.get(host);
        if (!instance) { return; }
        const current = instance.view.state.doc.toString();
        if (current === value) { return; }
        instance.lastSent = value;
        instance.view.dispatch({ changes: { from: 0, to: current.length, insert: value } });
    },

    setSignatures(host, signatures) {
        const instance = instances.get(host);
        if (!instance) { return; }
        instance.view.dispatch({ effects: setSignaturesEffect.of(signatures || {}) });
    },

    setTags(host, tags) {
        const instance = instances.get(host);
        if (!instance) { return; }
        instance.view.dispatch({
            effects: setTagsEffect.of({
                available: (tags && tags.available) || [],
                invalidSet: new Set((tags && tags.invalid) || []),
            }),
        });
    },

    destroy(host) {
        const instance = instances.get(host);
        if (!instance) { return; }
        if (instance.debounceTimer !== null) { clearTimeout(instance.debounceTimer); }
        chipClickHandlers.delete(instance.view);
        instance.view.destroy();
        instances.delete(host);
    },
};
