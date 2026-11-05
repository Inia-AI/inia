(() => {
    // Mirrors GET_PLACEHOLDERS in IniaAi.Common/Extensions/StringExtensions.cs —
    // highlights exactly what the engine discovers as candidate placeholders.
    const TAG_PATTERN = /\{\{?[a-zA-Z0-9._\-\[\] &\/()%]+\}?\}/g;
    const FIELD_SELECTOR = '.editor-mono-field textarea.mud-input-slot, .editor-mono-field input.mud-input-slot';
    const SYNCED_STYLES = [
        'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'lineHeight', 'letterSpacing',
        'tabSize', 'textTransform', 'textIndent',
        'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft',
    ];

    const states = new WeakMap();
    const live = new Set();

    const escapeHtml = (s) => s.replace(/[&<>]/g, (c) => c === '&' ? '&amp;' : c === '<' ? '&lt;' : '&gt;');

    function buildHtml(value, invalidSet) {
        let html = '';
        let pos = 0;
        for (const m of value.matchAll(TAG_PATTERN)) {
            const cls = invalidSet && invalidSet.has(m[0]) ? 'tag-hl tag-hl--unknown' : 'tag-hl';
            html += escapeHtml(value.slice(pos, m.index))
                + '<mark class="' + cls + '">' + escapeHtml(m[0]) + '</mark>';
            pos = m.index + m[0].length;
        }
        // A textarea shows an empty last line after a trailing newline and a div does not:
        // without the extra one the backdrop scrolls one line short and the pills drift.
        return html + escapeHtml(value.slice(pos)) + (value.endsWith('\n') ? '\n' : '');
    }

    // The per-block invalid-tag list rides on a data attribute kept current by
    // InvalidReplacementTagsAlert; fields outside any block (section text inputs)
    // simply have no wrapper and stay neutral.
    function invalidAttrFor(el) {
        const carrier = el.closest('[data-invalid-tags]');
        return carrier ? (carrier.getAttribute('data-invalid-tags') || '') : '';
    }

    function syncScroll(el) {
        const st = states.get(el);
        if (!st) { return; }
        st.backdrop.scrollTop = el.scrollTop;
        st.backdrop.scrollLeft = el.scrollLeft;
    }

    function render(el) {
        const st = states.get(el);
        if (!st) { return; }
        const invalidAttr = invalidAttrFor(el);
        st.last = el.value;
        st.lastInvalid = invalidAttr;
        st.backdrop.innerHTML = buildHtml(el.value, new Set(invalidAttr ? invalidAttr.split(',') : []));
        syncScroll(el);
    }

    function measureGeometry(el) {
        const st = states.get(el);
        if (!st || !el.isConnected) { return null; }
        const host = st.backdrop.parentElement;
        if (!host) { return null; }
        const cs = getComputedStyle(el);
        const styles = {};
        for (const prop of SYNCED_STYLES) { styles[prop] = cs[prop]; }
        const hostRect = host.getBoundingClientRect();
        const rect = el.getBoundingClientRect();
        // Client rects carry ancestor transforms: a field measured while its dialog scales in
        // would get shrunken offsets that no later resize corrects.
        const scale = host.offsetWidth ? hostRect.width / host.offsetWidth : 1;
        return {
            styles,
            top: ((rect.top - hostRect.top) / scale - host.clientTop + el.clientTop) + 'px',
            left: ((rect.left - hostRect.left) / scale - host.clientLeft + el.clientLeft) + 'px',
            width: el.clientWidth + 'px',
            height: el.clientHeight + 'px',
        };
    }

    function applyGeometry(el, measured) {
        const st = states.get(el);
        if (!st || !measured) { return; }
        const bs = st.backdrop.style;
        Object.assign(bs, measured.styles);
        bs.top = measured.top;
        bs.left = measured.left;
        bs.width = measured.width;
        bs.height = measured.height;
    }

    function syncGeometry(el) {
        applyGeometry(el, measureGeometry(el));
        syncScroll(el);
    }

    function createBackdrop(el) {
        if (states.has(el)) { return false; }
        if (el.closest('.mud-select, .mud-autocomplete, .mud-picker')) { return false; }
        const host = el.closest('.mud-input');
        if (!host) { return false; }
        const backdrop = document.createElement('div');
        backdrop.className = 'tag-highlight-backdrop'
            + (el.tagName === 'INPUT' ? ' tag-highlight-backdrop--single' : '');
        backdrop.setAttribute('aria-hidden', 'true');
        host.insertBefore(backdrop, el);
        const ro = new ResizeObserver(() => syncGeometry(el));
        ro.observe(el);
        states.set(el, { backdrop, ro, last: null, lastInvalid: '' });
        live.add(el);
        el.addEventListener('input', () => render(el));
        el.addEventListener('scroll', () => syncScroll(el), { passive: true });
        return true;
    }

    // Section navigation mounts dozens of fields in one frame: batch all DOM writes,
    // then all layout reads, then all style/content writes — one reflow for the lot
    // instead of one per field.
    function attachAll(els) {
        const fresh = [];
        for (const el of els) {
            if (createBackdrop(el)) { fresh.push(el); }
        }
        if (!fresh.length) { return; }
        const measures = fresh.map(measureGeometry);
        fresh.forEach((el, i) => {
            applyGeometry(el, measures[i]);
            const st = states.get(el);
            const invalidAttr = invalidAttrFor(el);
            st.last = el.value;
            st.lastInvalid = invalidAttr;
            st.backdrop.innerHTML = buildHtml(el.value, new Set(invalidAttr ? invalidAttr.split(',') : []));
        });
        fresh.forEach(syncScroll);
    }

    function pruneAndPoll() {
        if (document.hidden) { return; }
        for (const el of live) {
            const st = states.get(el);
            if (!el.isConnected) {
                st.ro.disconnect();
                st.backdrop.remove();
                states.delete(el);
                live.delete(el);
                continue;
            }
            if (st.last !== el.value || st.lastInvalid !== invalidAttrFor(el)) { render(el); }
            if (st.backdrop.scrollTop !== el.scrollTop || st.backdrop.scrollLeft !== el.scrollLeft) { syncScroll(el); }
        }
    }

    // Scan only the added subtrees, not the whole document — CodeMirror keystrokes and
    // our own backdrop writes mutate the DOM constantly and must not trigger rescans.
    let pendingRoots = [];
    let scanScheduled = false;
    const observer = new MutationObserver((mutations) => {
        for (const m of mutations) {
            const target = m.target;
            if (target instanceof Element
                && (target.closest('.cm-editor') || target.closest('.tag-highlight-backdrop'))) {
                continue;
            }
            for (const node of m.addedNodes) {
                if (node.nodeType === 1) { pendingRoots.push(node); }
            }
        }
        if (!pendingRoots.length || scanScheduled) { return; }
        scanScheduled = true;
        requestAnimationFrame(() => {
            scanScheduled = false;
            const roots = pendingRoots;
            pendingRoots = [];
            const els = new Set();
            for (const root of roots) {
                if (!root.isConnected) { continue; }
                if (root.matches && root.matches(FIELD_SELECTOR)) { els.add(root); }
                if (root.querySelectorAll) {
                    for (const el of root.querySelectorAll(FIELD_SELECTOR)) { els.add(el); }
                }
            }
            if (els.size) { attachAll([...els]); }
        });
    });

    const start = () => {
        observer.observe(document.body, { childList: true, subtree: true });
        setInterval(pruneAndPoll, 700);
        attachAll([...document.querySelectorAll(FIELD_SELECTOR)]);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
