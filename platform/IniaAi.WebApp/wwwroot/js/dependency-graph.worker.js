// Lays out dependency graphs for dependency-graph.js off the main thread.
// The first message names the ELK bundle; every later one is { id, graph } and is answered with
// { id, graph } or { id, error }.
let elk = null;

// A dedicated worker hears only the page that made it, but say so: anything from another origin is dropped.
function fromThisOrigin(event) {
    return !event.origin || event.origin === self.location.origin;
}

// Only a script served by this app is ever loaded.
function sameOriginScript(value) {
    try {
        const url = new URL(String(value), self.location.href);
        return url.origin === self.location.origin ? url.href : null;
    } catch {
        return null;
    }
}

self.onmessage = (event) => {
    if (!fromThisOrigin(event)) {
        return;
    }

    const message = event.data || {};
    if (message.elkUrl) {
        const elkUrl = sameOriginScript(message.elkUrl);
        if (!elkUrl) {
            self.postMessage({ ready: false, error: "The layout script must come from this app." });
            return;
        }

        try {
            // Without a document the bundle takes itself for ELK's own worker script and exports no
            // in-thread worker, so ELK cannot be constructed here. A stand-in for the one check fixes that.
            self.document = {};
            importScripts(elkUrl);
            elk = new self.ELK();
            delete self.document;
            self.onmessage = handleLayout;
            self.postMessage({ ready: true });
        } catch (error) {
            self.postMessage({ ready: false, error: String(error && error.message || error) });
        }
    }
};

function handleLayout(event) {
    if (!fromThisOrigin(event)) {
        return;
    }

    const { id, graph } = event.data || {};
    elk.layout(graph)
        .then((result) => self.postMessage({ id, graph: result }))
        .catch((error) => self.postMessage({ id, error: String(error && error.message || error) }));
}
