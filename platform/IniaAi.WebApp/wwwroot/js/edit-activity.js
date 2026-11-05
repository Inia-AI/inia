// Calls a component back once the user pauses after an input, change, click, drop or blur anywhere on
// the page, so it can recheck its own state without polling. The listener sits on the document because
// dialogs, drawers and popovers render outside the component's markup, and a picker that sets a value
// from a popup fires no input in it.
const ACTIVITY_EVENTS = ['input', 'change', 'click', 'drop', 'focusout'];
const QUIET_MILLISECONDS = 400;

export function watchEditActivity(component, methodName) {
    let timer = 0;
    const onActivity = () => {
        clearTimeout(timer);
        timer = setTimeout(() => component.invokeMethodAsync(methodName).catch((error) => console.warn('Edit activity:', error)), QUIET_MILLISECONDS);
    };
    ACTIVITY_EVENTS.forEach(type => document.addEventListener(type, onActivity, true));

    return {
        dispose() {
            clearTimeout(timer);
            ACTIVITY_EVENTS.forEach(type => document.removeEventListener(type, onActivity, true));
        },
    };
}
