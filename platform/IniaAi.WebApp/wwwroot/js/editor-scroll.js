window.iniaEditorScroll = (function () {
    const rootCssPx = (name, fallback) =>
        parseInt(getComputedStyle(document.documentElement).getPropertyValue(name), 10) || fallback;

    // Resolves once the element has held its place for a few frames, so a smooth scroll and any
    // layout it sets off are over.
    const settledInPlace = (element, isCurrent) => new Promise((resolve) => {
        const started = performance.now();
        let lastTop = Number.NaN;
        let stillFrames = 0;
        const check = () => {
            if (!isCurrent()) {
                resolve();
                return;
            }
            const top = element.getBoundingClientRect().top;
            stillFrames = Math.abs(top - lastTop) < 1 ? stillFrames + 1 : 0;
            lastTop = top;
            if (stillFrames >= 5 || performance.now() - started > 2000) {
                resolve();
                return;
            }
            requestAnimationFrame(check);
        };
        requestAnimationFrame(check);
    });

    // A pinned bar's height is known only once it pins: its buttons wrap on a narrow window. An element
    // in a block card that the pinned section bar, or another block's pinned bar, still covers is moved
    // below it, unless a newer scroll or the user's own scrolling came first.
    const MANUAL_SCROLL_EVENTS = ['wheel', 'touchstart', 'pointerdown', 'keydown'];

    // CSS places a pinned bar below the bars above it, so nothing covers an element inside one.
    const PINNED_BAR = '.section-stuck .section-sticky-bar, .block-stuck .block-header';

    const pinnedBarsOver = (element) => {
        const sectionBar = element.closest('.section-edit-shell.section-stuck')?.querySelector('.section-sticky-bar');
        const blockBars = element.closest('.block-pin-scope')?.querySelectorAll('.block-stuck .block-header') ?? [];
        return [sectionBar, ...blockBars].filter((bar) => bar && !element.contains(bar));
    };

    // Moves the element below the pinned bars over it, and below `floor` when one is given.
    const moveBelowPinnedBars = (element, floor = 0) => {
        if (element.closest(PINNED_BAR)) {
            return false;
        }
        const pinnedBars = pinnedBarsOver(element);
        if (pinnedBars.length === 0 && floor === 0) {
            return false;
        }
        const clearance = Math.max(floor, ...pinnedBars.map((bar) => bar.getBoundingClientRect().bottom))
            + rootCssPx('--editor-sticky-gap', 16);
        if (element.getBoundingClientRect().top >= clearance - 1) {
            return false;
        }
        element.style.scrollMarginTop = `${clearance}px`;
        element.scrollIntoView({ behavior: 'instant', block: 'start' });
        element.style.scrollMarginTop = '';
        return true;
    };

    let scrollGeneration = 0;
    const clearStickyHeaders = async (element, floor = 0) => {
        const generation = ++scrollGeneration;
        if (!element.closest('.block-pin-scope') || !element.closest('.editor-block-card')) {
            return;
        }
        const isCurrent = () => generation === scrollGeneration;
        const cancel = () => {
            if (isCurrent()) {
                scrollGeneration++;
            }
        };
        MANUAL_SCROLL_EVENTS.forEach((type) => window.addEventListener(type, cancel, { passive: true }));
        try {
            for (let pass = 0; pass < 2; pass++) {
                await settledInPlace(element, isCurrent);
                if (!isCurrent() || !moveBelowPinnedBars(element, floor)) {
                    return;
                }
            }
        } finally {
            MANUAL_SCROLL_EVENTS.forEach((type) => window.removeEventListener(type, cancel));
        }
    };

    return { rootCssPx, moveBelowPinnedBars, clearStickyHeaders };
})();

window.scrollToElement = (elementId) => {
    const element = document.getElementById(elementId);
    if (element) {
        const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        element.scrollIntoView({
            behavior: reduceMotion ? 'instant' : 'smooth',
            block: 'start',
            inline: 'nearest'
        });
        iniaEditorScroll.clearStickyHeaders(element);
    }
}

// Scrolls a workflow editor block card into view and flashes it: a short card to the middle, a
// tall one to its top below the sticky section header. The card may still be rendering after its
// section opened, so it is looked for until it appears.
window.revealEditorBlock = (blockId) => new Promise((resolve) => {
    const started = performance.now();
    const find = () => {
        const element = document.querySelector(`.editor-block-card#${CSS.escape(blockId)}`);
        if (!element && performance.now() - started < 1500) {
            setTimeout(find, 50);
            return;
        }

        if (element) {
            const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            const tall = element.getBoundingClientRect().height > window.innerHeight * 0.6;
            element.scrollIntoView({
                behavior: reduceMotion || document.hidden ? 'instant' : 'smooth',
                block: tall ? 'start' : 'center'
            });
            element.classList.remove('editor-block-flash');
            void element.offsetWidth;
            element.classList.add('editor-block-flash');
            setTimeout(() => element.classList.remove('editor-block-flash'), 1600);
            iniaEditorScroll.clearStickyHeaders(element);
        }

        resolve();
    };
    find();
});

// Pins the open section's header, and in any block list the header of the block being scrolled through:
// under the section's header when the list sits in an open section, under the app bar when it does not. A
// header that pins keeps the height it had in the page, so the content under it does not move when its
// compact bar takes over.
window.iniaStickyHeaders = (function () {
    const stops = new WeakMap();
    const appBarHeight = () => iniaEditorScroll.rootCssPx('--mud-appbar-height', 64);
    const stickyGap = () => iniaEditorScroll.rootCssPx('--editor-sticky-gap', 16);

    const holdHeight = (slot, pinned) => {
        slot.style.minHeight = pinned ? `${slot.offsetHeight}px` : '';
    };

    function observeSection(sentinel, shell) {
        if (!sentinel || !shell) {
            return;
        }
        unobserve(sentinel);
        const card = shell.querySelector('.section-edit-card');
        const observer = new IntersectionObserver(
            (entries) => {
                for (const entry of entries) {
                    const pinned = !entry.isIntersecting;
                    if (shell.classList.contains('section-stuck') !== pinned) {
                        holdHeight(card, pinned);
                        shell.classList.toggle('section-stuck', pinned);
                    }
                }
            },
            { rootMargin: `-${appBarHeight() + stickyGap()}px 0px 0px 0px`, threshold: [0] });
        observer.observe(sentinel);
        stops.set(sentinel, () => observer.disconnect());
    }

    function observeBlocks(scope) {
        if (!scope) {
            return;
        }
        unobserve(scope);
        const shell = scope.closest('.section-edit-shell');
        const sectionBar = shell?.querySelector('.section-sticky-bar');

        let frame = 0;
        const pinBlocks = () => {
            frame = 0;
            const sectionPinned = shell?.classList.contains('section-stuck') ?? false;
            if (sectionPinned) {
                scope.style.setProperty('--editor-block-pin-offset', `${sectionBar.offsetHeight}px`);
            }
            const canPin = !shell || sectionPinned;
            const pinLine = appBarHeight() + stickyGap() + (sectionPinned ? sectionBar.offsetHeight : 0);
            const slots = [...scope.querySelectorAll('.block-sticky-slot')];
            const pinned = slots.map((slot) => {
                const blockCard = slot.parentElement.getBoundingClientRect();
                return canPin && blockCard.top < pinLine && blockCard.bottom > pinLine;
            });
            slots.forEach((slot, index) => {
                if (slot.classList.contains('block-stuck') !== pinned[index]) {
                    holdHeight(slot, pinned[index]);
                    slot.classList.toggle('block-stuck', pinned[index]);
                }
            });
            (shell ?? scope).classList.toggle('has-stuck-block', pinned.includes(true));
        };
        const schedule = () => {
            if (!frame) {
                frame = requestAnimationFrame(pinBlocks);
            }
        };

        const barResizes = new ResizeObserver(schedule);
        if (sectionBar) {
            barResizes.observe(sectionBar);
        }
        const blockChanges = new MutationObserver(schedule);
        blockChanges.observe(scope, { childList: true, subtree: true });
        document.addEventListener('scroll', schedule, { capture: true, passive: true });
        window.addEventListener('resize', schedule, { passive: true });
        schedule();

        stops.set(scope, () => {
            barResizes.disconnect();
            blockChanges.disconnect();
            document.removeEventListener('scroll', schedule, { capture: true });
            window.removeEventListener('resize', schedule);
            cancelAnimationFrame(frame);
        });
    }

    function unobserve(target) {
        if (!target) {
            return;
        }
        const stop = stops.get(target);
        if (stop) {
            stop();
            stops.delete(target);
        }
    }

    // Tab and Shift+Tab scroll the field they reach only just into the window, which leaves it
    // under the app bar or a pinned bar; it is moved below them. Fields reached by pointer stay
    // where the pointer found them. Block headers pin again after a scroll, so a field in a block
    // card is measured again once the page settles.
    let tabbing = false;
    let scrolledSinceTab = false;
    document.addEventListener('keydown', (event) => {
        tabbing = event.key === 'Tab';
        scrolledSinceTab = false;
    }, true);
    document.addEventListener('pointerdown', () => { tabbing = false; }, true);
    window.addEventListener('scroll', () => { scrolledSinceTab = true; }, { capture: true, passive: true });
    document.addEventListener('focusin', (event) => {
        const element = event.target;
        if (!tabbing || !(element instanceof Element) || !element.closest('.section-edit-shell, .block-pin-scope')) {
            return;
        }
        requestAnimationFrame(() => {
            if (!element.isConnected) {
                return;
            }
            const floor = appBarHeight();
            if (iniaEditorScroll.moveBelowPinnedBars(element, floor) || scrolledSinceTab) {
                iniaEditorScroll.clearStickyHeaders(element, floor);
            }
        });
    });

    return { observeSection, observeBlocks, unobserve };
})();
