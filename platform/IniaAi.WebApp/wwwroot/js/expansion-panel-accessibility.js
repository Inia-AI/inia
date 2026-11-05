// MudBlazor 9.12 renders an expansion panel header as a focusable div that opens on Enter and Space but
// announces neither a role nor whether its panel is open. Each header becomes a button that says so. Two
// kinds stay as they are: a disabled header, which the workflow map uses as a label, and a header that
// holds a control of its own, because a screen reader flattens what sits inside a button. A panel whose
// header needs a control uses DisclosurePanel instead.
(function () {
    const PANEL = 'mud-expand-panel';
    const HEADER = 'mud-expand-panel-header';
    const EXPANDED = 'mud-panel-expanded';
    const DISABLED = 'mud-disabled';
    const CONTROL = 'button, a[href], input, select, textarea, [role="button"], [tabindex]:not([tabindex="-1"])';

    const headerOf = (panel) => panel.querySelector(`:scope > div.${HEADER}`);

    const describe = (panel) => {
        const header = headerOf(panel);
        if (!header) {
            return;
        }
        if (header.classList.contains(DISABLED) || header.querySelector(CONTROL)) {
            header.removeAttribute('role');
            header.removeAttribute('aria-expanded');
            return;
        }
        const expanded = panel.classList.contains(EXPANDED) ? 'true' : 'false';
        if (header.getAttribute('role') !== 'button') {
            header.setAttribute('role', 'button');
        }
        if (header.getAttribute('aria-expanded') !== expanded) {
            header.setAttribute('aria-expanded', expanded);
        }
    };

    const pending = new Set();
    let frame = 0;
    const describePending = () => {
        frame = 0;
        const panels = [...pending];
        pending.clear();
        panels.forEach((panel) => {
            if (panel.isConnected) {
                describe(panel);
            }
        });
    };
    const queue = (panel) => {
        pending.add(panel);
        if (!frame) {
            frame = requestAnimationFrame(describePending);
        }
    };

    new MutationObserver((records) => {
        for (const record of records) {
            if (record.type === 'childList') {
                record.addedNodes.forEach((node) => {
                    if (!(node instanceof Element)) {
                        return;
                    }
                    if (node.classList.contains(PANEL)) {
                        queue(node);
                    }
                    node.querySelectorAll(`.${PANEL}`).forEach(queue);
                });
            } else if (record.target.classList.contains(PANEL)) {
                queue(record.target);
            } else if (record.target.classList.contains(HEADER) && record.target.parentElement) {
                queue(record.target.parentElement);
            }
        }
    }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class'] });
    document.querySelectorAll(`.${PANEL}`).forEach(queue);
})();
