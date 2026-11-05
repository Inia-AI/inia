// Closes two gaps that MudBlazor 9.12 menus still have:
// - one Escape closes a menu however it was opened. On a menu opened with the pointer, focus stays on
//   its button, and MudMenu reads an Escape there as keyboard use: it moves focus to the first item and
//   leaves the menu open, so a following Enter runs that item.
// - a menu whose button is custom activator content: MudMenu wraps that button in a second focusable
//   element that it announces as the menu button, and leaves focus on the page body when Escape closes
//   the menu. The button inside is announced as the menu button instead, the wrapper leaves the tab
//   order, and Escape returns focus to the button.
(function () {
    const MENU = '.mud-menu';
    const POPOVER = '.mud-popover';
    const OPEN = 'mud-popover-open';
    const ANCHOR_PREFIX = 'popover-';
    const CONTENT_PREFIX = 'popovercontent-';
    const WRAPPER_STATE = ['aria-haspopup', 'aria-expanded', 'aria-controls'];
    const FOCUS_RETURN_TIMEOUT_MS = 1000;

    const popoverOf = (menu) => {
        const anchor = menu.querySelector(':scope > .mud-popover-cascading-value');
        return anchor ? document.getElementById(CONTENT_PREFIX + anchor.id.slice(ANCHOR_PREFIX.length)) : null;
    };

    const menuOf = (popover) => {
        const anchor = popover.id.startsWith(CONTENT_PREFIX)
            ? document.getElementById(ANCHOR_PREFIX + popover.id.slice(CONTENT_PREFIX.length))
            : null;
        return anchor?.parentElement?.matches(MENU) ? anchor.parentElement : null;
    };

    const isOpen = (menu) => popoverOf(menu)?.classList.contains(OPEN) ?? false;

    const isTopLevel = (menu) => !menu.closest(POPOVER);

    // The control a person presses to open the menu, and the wrapper MudMenu puts around custom
    // activator content when that content is a control of its own.
    const activatorOf = (menu) => {
        const own = menu.querySelector(':scope > .mud-menu-button-activator, :scope > .mud-menu-icon-button-activator');
        if (own) {
            return { control: own, wrapper: null };
        }
        const wrapper = menu.querySelector(':scope > .mud-menu-activator');
        if (!wrapper) {
            return null;
        }
        const inner = wrapper.querySelector('button, a[href], [role="button"]');
        return inner ? { control: inner, wrapper } : { control: wrapper, wrapper: null };
    };

    const setAttribute = (element, name, value) => {
        if (element.getAttribute(name) !== value) {
            element.setAttribute(name, value);
        }
    };

    const describe = (menu) => {
        const activator = activatorOf(menu);
        if (!activator?.wrapper) {
            return;
        }
        const { control, wrapper } = activator;
        const popover = popoverOf(menu);

        setAttribute(control, 'aria-haspopup', 'menu');
        setAttribute(control, 'aria-expanded', popover?.classList.contains(OPEN) ? 'true' : 'false');
        const list = popover?.querySelector('.mud-menu-list');
        if (list) {
            setAttribute(control, 'aria-controls', list.id || popover.id);
        } else {
            control.removeAttribute('aria-controls');
        }
        setAttribute(wrapper, 'tabindex', '-1');
        wrapper.removeAttribute('role');
        WRAPPER_STATE.forEach((name) => wrapper.removeAttribute(name));
    };

    const pending = new Set();
    let frame = 0;
    const describePending = () => {
        frame = 0;
        const menus = [...pending];
        pending.clear();
        menus.forEach((menu) => {
            if (menu.isConnected && isTopLevel(menu)) {
                describe(menu);
            }
        });
    };
    const queue = (menu) => {
        pending.add(menu);
        if (!frame) {
            frame = requestAnimationFrame(describePending);
        }
    };

    // MudMenu renders the menu state on the wrapper again whenever the menu opens or closes. A
    // removal is this script's own, so only a state MudMenu wrote back queues the menu again.
    new MutationObserver((records) => {
        for (const record of records) {
            const target = record.target;
            if (record.type === 'childList') {
                record.addedNodes.forEach((node) => {
                    if (node instanceof Element && node.matches(MENU)) {
                        queue(node);
                    }
                });
            } else if (target.classList.contains('mud-menu-activator')) {
                if (target.hasAttribute(record.attributeName)) {
                    queue(target.parentElement);
                }
            } else if (record.attributeName === 'class' && target.classList.contains('mud-popover')) {
                const menu = menuOf(target);
                if (menu) {
                    queue(menu);
                }
            }
        }
    }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', ...WRAPPER_STATE] });
    document.querySelectorAll(MENU).forEach(queue);

    const returnFocusWhenClosed = (menu, control) => {
        const started = performance.now();
        const check = () => {
            if (isOpen(menu)) {
                if (performance.now() - started < FOCUS_RETURN_TIMEOUT_MS) {
                    requestAnimationFrame(check);
                }
                return;
            }
            const active = document.activeElement;
            if (!active || active === document.body || active.closest(POPOVER)) {
                control.focus();
            }
        };
        requestAnimationFrame(check);
    };

    window.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || !(event.target instanceof Element)) {
            return;
        }
        const target = event.target;
        const popover = target.closest(POPOVER);
        const menu = popover ? menuOf(popover) : target.closest(MENU);
        if (!menu || !isTopLevel(menu) || !isOpen(menu)) {
            return;
        }
        const activator = activatorOf(menu);
        if (!activator) {
            return;
        }
        if (popover) {
            returnFocusWhenClosed(menu, activator.control);
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        activator.control.click();
    }, true);
})();
