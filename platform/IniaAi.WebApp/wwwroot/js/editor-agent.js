// Agent mode in the workflow editor (Components/WorkflowEditor/Agent): keeping the newest message
// in view, and the block links in the agent's answers.
window.editorAgent = (function () {
    // How close to the bottom, in pixels, still counts as reading the newest message.
    const NEAR_END_PX = 120;

    function scrollToEnd(element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    }

    // While the reader is at the newest message, content that grows (an answer arriving, steps
    // appearing) keeps it in view; once they scroll up to read, it stays where they are.
    function followEnd(element) {
        if (!element || element.editorAgentFollowing) {
            return;
        }
        element.editorAgentFollowing = true;
        let nearEnd = true;
        element.addEventListener('scroll', () => {
            nearEnd = element.scrollHeight - element.scrollTop - element.clientHeight <= NEAR_END_PX;
        }, { passive: true });
        const content = element.firstElementChild || element;
        new ResizeObserver(() => {
            if (nearEnd) {
                scrollToEnd(element);
            }
        }).observe(content);
    }

    // Answers are rendered as HTML with block links as in-page links carrying the block id; a
    // click or Enter on one asks the panel to show that block.
    function wireBlockLinks(element, panel) {
        if (!element || !panel) {
            return;
        }
        element.addEventListener('click', (event) => {
            const link = event.target.closest('a[data-block-id]');
            if (!link || !element.contains(link)) {
                return;
            }
            event.preventDefault();
            panel.invokeMethodAsync('OpenBlockLinkAsync', link.getAttribute('data-block-id'));
        });
    }

    // Ctrl+J (Cmd+J on a Mac) opens and closes Agent mode from anywhere in the editor. A field that
    // claims the keys itself marks the event handled, and the editor then leaves it alone.
    let shortcutListener = null;

    function listenForShortcut(entry) {
        stopListeningForShortcut();
        shortcutListener = (event) => {
            if (event.defaultPrevented || event.altKey || event.shiftKey || !(event.ctrlKey || event.metaKey)) {
                return;
            }
            if (event.key !== 'j' && event.key !== 'J') {
                return;
            }
            event.preventDefault();
            entry.invokeMethodAsync('ToggleFromShortcutAsync');
        };
        window.addEventListener('keydown', shortcutListener);
    }

    function stopListeningForShortcut() {
        if (shortcutListener) {
            window.removeEventListener('keydown', shortcutListener);
            shortcutListener = null;
        }
    }

    return { scrollToEnd, followEnd, wireBlockLinks, listenForShortcut, stopListeningForShortcut };
})();
