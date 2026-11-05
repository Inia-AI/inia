// Renders the workflow editor's dependency graph (Components/WorkflowEditor/Dependencies/DependencyGraphView.razor)
// in the Graph tab and in the dependency chain slide-over. The view sends a scene (cards, links, groups, all
// shaped in C#); this module lays it out with ELK in a worker, draws cards as HTML and links as SVG, and reports
// clicks back. In the slide-over it also watches the editor behind it, so a block the author clicks into becomes
// the focus, an edit redraws the chain, and Esc or a click on an empty part of the editor closes it.

const EDITOR_SELECTOR = ".workflow-editor-root";
const SLIDE_OVER_SELECTOR = ".dep-slideover";
const MIN_SLIDE_OVER_WIDTH = 360;
const MAX_SLIDE_OVER_SHARE = 0.85;
const EDIT_DELAY_MS = 700;
const RELAYOUT_DELAY_MS = 250;
// A change of width beyond this lays the graph out again for the new size; smaller ones only reframe it.
const RELAYOUT_WIDTH_CHANGE_PX = 120;
const WORKER_START_TIMEOUT_MS = 15000;
const STATUS_DELAY_MS = 150;
const DRAG_THRESHOLD_PX = 4;
const MIN_SCALE = 0.1;
const MAX_SCALE = 2;
// The opening view fits the graph only down to this scale; below it the cards are too small to read.
const READABLE_SCALE = 0.7;
// A chain or an opened section is fitted whole down to this scale before it is cropped.
const FOCUS_FIT_SCALE = 0.45;
// The map opens with the whole workflow in view down to this scale, then falls back to its start.
const MAP_FIT_SCALE = 0.35;
const ZOOM_ANIMATION_MS = 280;
const PADDING = 24;
const PART_GAP = 40;
const LARGE_GRAPH_CARDS = 120;
const TOOLTIP_TAGS = 8;
// The id prefixes DependencySceneBuilder gives section cards and open-section boxes.
const SECTION_CARD_PREFIX = "sec:";
const SECTION_BOX_PREFIX = "box:";

const CARD_SIZES = {
    block: { width: 248, height: 88 },
    section: { width: 264, height: 96 },
    stub: { width: 224, height: 40 },
    input: { width: 0, height: 38 },
};

const INTERACTIVE_SELECTOR = "button, a, input, textarea, select, label, [role=button], [role=tab], [role=menuitem], [contenteditable], .mud-input-control, .mud-chip, .mud-popover";

const ARROW_VARIANTS = ["data", "up", "down", "document", "counterparty", "gate", "muted", "issue"];

let instanceCount = 0;

export function create(canvas, host, dotnet, options) {
    return new DependencyGraph(canvas, host, dotnet, options || {});
}

class LayoutEngine {
    constructor(workerUrl, elkUrl) {
        this.elkUrl = absoluteUrl(elkUrl);
        this.pending = new Map();
        this.sequence = 0;
        this.worker = null;
        this.elk = null;
        this.ready = this.startWorker(absoluteUrl(workerUrl)).catch((error) => {
            console.warn("Dependency graph: laying out on the main thread", error);
            return this.startMainThread();
        });
    }

    startWorker(url) {
        return new Promise((resolve, reject) => {
            let worker;
            try {
                worker = new Worker(url);
            } catch (error) {
                reject(error);
                return;
            }

            const timer = setTimeout(() => {
                worker.terminate();
                reject(new Error("The layout worker did not start"));
            }, WORKER_START_TIMEOUT_MS);

            worker.onmessage = (event) => {
                const data = event.data || {};
                if (!("ready" in data)) {
                    return;
                }

                clearTimeout(timer);
                if (!data.ready) {
                    worker.terminate();
                    reject(new Error(data.error));
                    return;
                }

                worker.onmessage = (message) => this.receive(message.data || {});
                worker.onerror = () => this.failAll(new Error("The layout worker stopped"));
                this.worker = worker;
                resolve();
            };
            worker.onerror = (error) => {
                clearTimeout(timer);
                worker.terminate();
                reject(error);
            };
            worker.postMessage({ elkUrl: this.elkUrl });
        });
    }

    async startMainThread() {
        if (!window.ELK) {
            await loadScript(this.elkUrl);
        }

        this.elk = new window.ELK();
    }

    async layout(graph) {
        await this.ready;
        if (this.elk) {
            return this.elk.layout(graph);
        }

        const id = ++this.sequence;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.worker.postMessage({ id, graph });
        });
    }

    receive({ id, graph, error }) {
        const pending = this.pending.get(id);
        if (!pending) {
            return;
        }

        this.pending.delete(id);
        if (error) {
            pending.reject(new Error(error));
        } else {
            pending.resolve(graph);
        }
    }

    failAll(error) {
        for (const pending of this.pending.values()) {
            pending.reject(error);
        }

        this.pending.clear();
    }

    dispose() {
        if (this.worker) {
            this.worker.terminate();
        }

        this.failAll(new Error("disposed"));
    }
}

class DependencyGraph {
    constructor(canvas, host, dotnet, { workerUrl, elkUrl, slideOver }) {
        this.canvas = canvas;
        this.host = host;
        this.dotnet = dotnet;
        this.slideOver = !!slideOver;
        this.uid = `dep${++instanceCount}`;
        this.engine = new LayoutEngine(workerUrl, elkUrl);
        this.view = { scale: 1, x: 0, y: 0 };
        this.size = null;
        this.token = 0;
        this.scene = null;
        this.sceneKey = null;
        this.autoFit = true;
        this.positions = new Map();
        this.openGroups = new Set();
        this.lastSelected = null;
        this.highlighted = null;
        this.layoutWidth = 0;
        this.disposers = [];
        this.disposed = false;

        this.build();
        if (this.slideOver) {
            this.watchSlideOver();
            this.watchEditor();
        } else if (host.getBoundingClientRect().bottom > window.innerHeight) {
            // The tab sits below the editor's header; bring all of it into the window when it opens.
            host.scrollIntoView({ block: "nearest" });
        }
    }

    // ---- public, called from DependencyGraphView.razor ----

    async render(scene) {
        const token = ++this.token;
        this.scene = scene;
        this.highlighted = undefined;
        if (scene.mode !== "chain" || scene.focusId !== this.lastSelected) {
            this.lastSelected = null;
        }

        this.note.textContent = scene.emptyMessage || "";
        this.note.hidden = !scene.emptyMessage;

        if (!scene.cards.length) {
            this.stage.replaceChildren();
            this.size = null;
            return;
        }

        const statusTimer = setTimeout(() => {
            if (token === this.token) {
                this.status.textContent = "Laying out…";
                this.status.hidden = false;
            }
        }, STATUS_DELAY_MS);

        let laid;
        try {
            const rect = this.viewport.getBoundingClientRect();
            const aspectRatio = rect.width > 0 && rect.height > 0 ? rect.width / rect.height : 1;
            const layoutAt = async (scale) => {
                const wrapWidth = rect.width / scale;
                const columns = Math.max(1, Math.floor((wrapWidth - PADDING * 4) / (CARD_SIZES.block.width + 16)));
                const result = await this.engine.layout(toElkGraph(scene, aspectRatio, columns, wrapWidth - PADDING * 4));
                if (scene.mode === "map") {
                    packInColumns(result, scene, wrapWidth);
                }

                return result;
            };

            laid = await layoutAt(READABLE_SCALE);
            // A map wrapped for the readable scale can come out tall and narrow; re-wrap it for the
            // scale it will really be shown at, so the fit uses the width as well as the height.
            const fitScale = mapFitScale(laid, rect);
            if (scene.mode === "map" && fitScale < READABLE_SCALE && token === this.token) {
                // The scale at which the map's area matches the viewport's; wrapping changes its shape, not its area.
                const target = Math.sqrt(((rect.width - PADDING * 2) * (rect.height - PADDING * 2)) / (laid.width * laid.height));
                const rewrapped = await layoutAt(Math.min(READABLE_SCALE, Math.max(MAP_FIT_SCALE, target)));
                if (mapFitScale(rewrapped, rect) > fitScale) {
                    laid = rewrapped;
                }
            }

            this.layoutWidth = rect.width;
        } catch (error) {
            clearTimeout(statusTimer);
            if (token === this.token && !this.disposed) {
                this.status.textContent = "The graph could not be laid out.";
                this.status.hidden = false;
            }

            console.warn("Dependency graph: layout failed", error);
            return;
        }

        clearTimeout(statusTimer);
        if (token !== this.token || this.disposed) {
            return;
        }

        this.status.hidden = true;
        const focused = this.focusedControl();
        this.draw(scene, laid);
        // On the map a block arrived at from its chain stays traced, so only its own links stand out.
        this.pinnedTrace = scene.mode === "map" && scene.focusId && this.positions.has(scene.focusId) ? scene.focusId : null;
        this.highlighted = undefined;
        this.highlight(this.pinnedTrace);

        const key = `${scene.mode}|${scene.focusId || ""}`;
        const opened = scene.mode === "map" ? scene.groups.find((group) => !this.openGroups.has(group.id)) : null;
        this.openGroups = new Set(scene.groups.map((group) => group.id));
        if (opened && this.positions.get(opened.id)) {
            this.sceneKey = key;
            this.animate(() => this.zoomTo(this.positions.get(opened.id)));
        } else if (key !== this.sceneKey || this.autoFit) {
            const changed = key !== this.sceneKey && this.sceneKey !== null;
            this.sceneKey = key;
            const settle = () => this.frame(scene.mode === "chain" ? FOCUS_FIT_SCALE : MAP_FIT_SCALE);
            if (changed) {
                this.animate(settle);
            } else {
                settle();
            }
        } else {
            this.apply();
        }

        this.restoreFocus(focused, scene);
    }

    // The whole graph in view, however small (the Fit button).
    fit() {
        this.frame(MIN_SCALE);
    }

    // Fits the graph when it stays readable; otherwise keeps it readable and centres the focus,
    // or shows the top left of the graph when nothing is focused.
    frame(minScale) {
        this.autoFit = true;
        const rect = this.viewport.getBoundingClientRect();
        if (!this.size || rect.width === 0 || rect.height === 0) {
            return;
        }

        const { width, height } = this.size;
        const fitScale = Math.min(1, (rect.width - PADDING * 2) / width, (rect.height - PADDING * 2) / height);
        if (fitScale >= minScale) {
            this.view.scale = Math.max(MIN_SCALE, fitScale);
            this.view.x = (rect.width - width * this.view.scale) / 2;
            this.view.y = Math.max(PADDING, (rect.height - height * this.view.scale) / 2);
            this.apply();
            return;
        }

        this.view.scale = minScale;
        const focus = this.scene && this.scene.mode === "chain" ? this.focusBox() : null;
        if (focus) {
            this.centreOn(focus, rect);
            return;
        }

        this.view.x = width * minScale > rect.width - PADDING * 2 ? PADDING : (rect.width - width * minScale) / 2;
        this.view.y = PADDING;
        this.apply();
    }

    // Fits one box (a section just opened) at a readable size, centred.
    zoomTo(box) {
        const rect = this.viewport.getBoundingClientRect();
        if (rect.width === 0 || rect.height === 0) {
            return;
        }

        const fitScale = Math.min(1, (rect.width - PADDING * 2) / box.width, (rect.height - PADDING * 2) / box.height);
        this.view.scale = Math.max(FOCUS_FIT_SCALE, fitScale);
        this.autoFit = false;
        this.centreOn(box, rect);
    }

    // Eases a programmatic zoom; drags and wheel moves stay instant.
    animate(move) {
        clearTimeout(this.animationTimer);
        this.stage.classList.add("dep-stage--animate");
        move();
        this.animationTimer = setTimeout(() => this.stage.classList.remove("dep-stage--animate"), ZOOM_ANIMATION_MS);
    }

    // The focused block, or on the map the section card that holds it.
    focusBox() {
        if (!this.scene) {
            return null;
        }

        const byId = this.scene.focusId ? this.positions.get(this.scene.focusId) : null;
        if (byId) {
            return byId;
        }

        const card = this.scene.cards.find((candidate) => candidate.role === "focus");
        return card ? this.positions.get(card.id) : null;
    }

    dispose() {
        this.disposed = true;
        this.token++;
        for (const dispose of this.disposers.splice(0)) {
            dispose();
        }

        this.engine.dispose();
        this.canvas.replaceChildren();
    }

    // ---- DOM ----

    build() {
        this.canvas.replaceChildren();
        this.viewport = element("div", "dep-viewport");
        this.stage = element("div", "dep-stage");
        this.viewport.append(this.stage);

        const zoom = element("div", "dep-zoom");
        zoom.append(
            zoomButton("+", "Zoom in", () => this.zoomBy(1.25)),
            zoomButton("−", "Zoom out", () => this.zoomBy(0.8)));

        this.note = element("div", "dep-note");
        this.note.hidden = true;
        this.status = element("div", "dep-status");
        this.status.hidden = true;
        this.status.setAttribute("role", "status");
        this.tooltip = element("div", "dep-tooltip");
        this.tooltip.hidden = true;
        this.tooltip.setAttribute("role", "tooltip");

        this.canvas.append(this.viewport, zoom, this.note, this.status, this.tooltip);

        this.watchPanZoom();
        this.listen(this.stage, "click", (event) => this.onClick(event));
        this.listen(this.stage, "keydown", (event) => {
            if ((event.key === "Enter" || event.key === " ") && event.target.matches("[data-action]")) {
                event.preventDefault();
                this.activate(event.target);
            }
        });
        this.listen(this.stage, "focusin", (event) => this.panTo(event.target));
        // The viewport clips the stage; a browser scrolling it to show a focused card would put the
        // stage where the pan and zoom maths does not expect it, so the pan does that job instead.
        this.listen(this.viewport, "scroll", () => {
            this.viewport.scrollLeft = 0;
            this.viewport.scrollTop = 0;
        });
        this.listen(this.stage, "mouseover", (event) => this.onHover(event));
        this.listen(this.stage, "mousemove", (event) => this.moveTooltip(event));
        this.listen(this.stage, "mouseleave", () => {
            this.highlight(this.pinnedTrace || null);
            this.tooltip.hidden = true;
        });

        let relayout = 0;
        const observer = new ResizeObserver(() => {
            if (this.autoFit) {
                this.frame(this.scene && this.scene.mode === "chain" ? FOCUS_FIT_SCALE : MAP_FIT_SCALE);
            }

            const width = this.viewport.getBoundingClientRect().width;
            if (this.scene && this.layoutWidth && Math.abs(width - this.layoutWidth) > RELAYOUT_WIDTH_CHANGE_PX) {
                clearTimeout(relayout);
                relayout = setTimeout(() => this.render(this.scene), RELAYOUT_DELAY_MS);
            }
        });
        observer.observe(this.viewport);
        this.disposers.push(() => {
            observer.disconnect();
            clearTimeout(relayout);
        });
    }

    draw(scene, laid) {
        this.positions = new Map();
        this.highlighted = undefined;
        const containers = new Map([["root", { x: 0, y: 0 }]]);
        const edges = [];
        const walk = (node, offsetX, offsetY) => {
            for (const child of node.children || []) {
                const x = offsetX + (child.x || 0);
                const y = offsetY + (child.y || 0);
                this.positions.set(child.id, { x, y, width: child.width, height: child.height });
                containers.set(child.id, { x, y });
                walk(child, x, y);
            }

            for (const edge of node.edges || []) {
                edges.push(edge);
            }
        };
        walk(laid, 0, 0);

        this.size = { width: laid.width || 0, height: laid.height || 0 };
        this.stage.style.width = `${this.size.width}px`;
        this.stage.style.height = `${this.size.height}px`;

        const cardsById = new Map(scene.cards.map((card) => [card.id, card]));
        const linksById = new Map(scene.links.map((link) => [link.id, link]));
        const maxWeight = Math.max(1, ...scene.links.map((link) => link.weight || 1));

        const groupLayer = document.createDocumentFragment();
        for (const group of scene.groups) {
            const box = this.positions.get(group.id);
            if (!box) {
                continue;
            }

            const div = element("div", `dep-group dep-group--${group.kind}`);
            place(div, box);
            if (group.kind === "section") {
                const title = element("button", "dep-group__title dep-group__title--button");
                title.type = "button";
                title.dataset.action = "section";
                title.dataset.target = group.id.slice(SECTION_BOX_PREFIX.length);
                title.title = "Close this section";
                title.append(text(group.label), element("span", "dep-group__hint", "close"));
                div.append(title);
            } else {
                div.append(element("div", "dep-group__title", group.label));
            }

            groupLayer.append(div);
        }

        const svg = svgElement("svg", { class: "dep-edges", width: this.size.width, height: this.size.height });
        svg.append(this.markers());
        for (const edge of edges) {
            const link = linksById.get(edge.id);
            if (!link) {
                continue;
            }

            const offset = containers.get(edge.container) || { x: 0, y: 0 };
            const d = pathFromSections(edge.sections || [], offset);
            if (!d) {
                continue;
            }

            const variant = arrowVariant(link, cardsById);
            const path = svgElement("path", {
                class: `dep-edge dep-edge--${link.style} dep-edge--role-${link.role}${variant === "issue" ? " dep-edge--issue" : ""}`,
                d,
                "marker-end": `url(#${this.uid}-arrow-${variant})`,
                "data-id": link.id,
            });
            if (scene.mode === "map") {
                path.style.strokeWidth = `${1.2 + 5 * Math.sqrt((link.weight || 1) / maxWeight)}px`;
            }

            const hit = svgElement("path", { class: "dep-edge-hit", d, "data-link": link.id });
            svg.append(path, hit);
        }

        const cardLayer = document.createDocumentFragment();
        for (const card of scene.cards) {
            const box = this.positions.get(card.id);
            if (!box) {
                continue;
            }

            const div = this.cardElement(card, scene);
            place(div, box);
            cardLayer.append(div);
        }

        this.stage.replaceChildren(groupLayer, svg, cardLayer);
    }

    markers() {
        const defs = svgElement("defs", {});
        for (const variant of ARROW_VARIANTS) {
            const marker = svgElement("marker", {
                id: `${this.uid}-arrow-${variant}`,
                viewBox: "0 0 10 10",
                refX: "9",
                refY: "5",
                markerWidth: "7",
                markerHeight: "7",
                markerUnits: "userSpaceOnUse",
                orient: "auto-start-reverse",
            });
            marker.append(svgElement("path", { d: "M0,0 L10,5 L0,10 z", class: `dep-arrow dep-arrow--${variant}` }));
            defs.append(marker);
        }

        return defs;
    }

    cardElement(card, scene) {
        const div = element("div", `dep-card dep-card--${card.kind} dep-card--role-${card.role}`);
        div.dataset.id = card.id;
        if (card.inCycle) {
            div.classList.add("dep-card--cycle");
        }

        if (card.isUnresolved) {
            div.classList.add("dep-card--unresolved");
        }

        if (card.agentMark) {
            div.classList.add(`dep-card--agent-${card.agentMark}`);
            div.title = card.agentMark === "added" ? "The agent proposes this" : "The agent proposes a change here";
        }

        const action = cardAction(card, scene, this.slideOver);
        if (action && card.kind !== "block") {
            div.dataset.action = action.name;
            div.dataset.target = action.target;
            div.tabIndex = 0;
            div.setAttribute("role", "button");
            div.setAttribute("aria-label", action.label);
        }

        if (card.kind === "block") {
            // The card holds buttons of its own, so its action is a button too rather than a role on the card.
            const name = element(action ? "button" : "span", "dep-card__name", card.label);
            if (action) {
                name.type = "button";
                name.classList.add("dep-card__primary");
                name.dataset.action = action.name;
                name.dataset.target = action.target;
                name.setAttribute("aria-label", action.label);
            }

            const top = element("div", "dep-card__top");
            top.append(element("span", "dep-card__status"), name);
            if (card.variantCount > 0) {
                const variants = element("span", "dep-card__variants", `×${card.variantCount}`);
                variants.title = `Runs once for each of ${card.variantCount} variants`;
                top.append(variants);
            }

            const meta = element("div", "dep-card__meta");
            const chip = element("span", "block-type-chip block-type-chip--mini", card.typeLabel || "");
            if (card.hue) {
                chip.style.setProperty("--chip-hue", card.hue);
            }

            meta.append(chip, element("span", "dep-card__section", card.detail || ""));

            const foot = element("div", "dep-card__foot");
            if (card.inputCount > 0) {
                foot.append(countButton("inputs", card.id, card.inputCount, "input", card.inputsOpen));
            }

            if (card.gateCount > 0) {
                foot.append(countButton("gates", card.id, card.gateCount, "gate", card.gatesOpen));
            }

            if (card.inCycle) {
                const warning = element("span", "dep-card__warning", "cycle");
                warning.title = "This block is on a cycle: the blocks read each other, so one of them runs without the other's output.";
                foot.append(warning);
            }

            if (!this.slideOver) {
                const open = element("button", "dep-card__open", "Open in editor");
                open.type = "button";
                open.dataset.action = "open";
                open.dataset.target = card.id;
                open.setAttribute("aria-label", `${card.label}: open in editor`);
                foot.append(open);
            }

            div.append(top, meta, foot);
        } else if (card.kind === "section") {
            const top = element("div", "dep-card__top");
            top.append(element("span", "dep-card__name", card.label));
            if (card.variantCount > 0) {
                top.append(element("span", "dep-card__variants", `×${card.variantCount}`));
            }

            const meta = element("div", "dep-card__meta dep-card__meta--wrap");
            for (const line of card.summary || []) {
                meta.append(element("span", "dep-card__pill", line));
            }

            const foot = element("div", "dep-card__foot");
            foot.append(element("span", "dep-card__section", `${card.blockCount} ${card.blockCount === 1 ? "block" : "blocks"} · open`));
            if (card.inCycle) {
                foot.append(element("span", "dep-card__warning", "cycle"));
            }

            div.append(top, meta, foot);
        } else if (card.kind === "stub") {
            div.append(element("span", "dep-card__name", card.label));
        } else {
            div.append(element("span", "dep-card__name", card.label));
            if (!card.isUnresolved) {
                div.append(element("span", "dep-card__section", card.detail || ""));
            }
        }

        return div;
    }

    // ---- interaction ----

    onClick(event) {
        if (this.suppressClick) {
            this.suppressClick = false;
            return;
        }

        const card = event.target.closest(".dep-card");
        const target = event.target.closest("[data-action]") || (card && card.querySelector(".dep-card__primary"));
        if (target && this.stage.contains(target)) {
            event.stopPropagation();
            this.activate(target);
        }
    }

    // The control that has keyboard focus inside the stage, described so its replacement can be found
    // after the stage is rebuilt.
    focusedControl() {
        const active = document.activeElement;
        if (!active || !this.stage.contains(active)) {
            return null;
        }

        const card = active.closest(".dep-card");
        return {
            action: active.dataset.action || null,
            target: active.dataset.target || null,
            cardId: card ? card.dataset.id : null,
        };
    }

    // Puts keyboard focus back on the same control, else on its card, else on the focused block, so a
    // keyboard user keeps their place after every step.
    restoreFocus(focused, scene) {
        if (!focused) {
            return;
        }

        const byAction = focused.action && focused.target
            ? this.stage.querySelector(`[data-action="${CSS.escape(focused.action)}"][data-target="${CSS.escape(focused.target)}"]`)
            : null;
        const cardOf = (id) => {
            const card = id ? this.stage.querySelector(`.dep-card[data-id="${CSS.escape(id)}"]`) : null;
            return card ? card.querySelector(".dep-card__primary") || (card.tabIndex >= 0 ? card : null) : null;
        };
        const next = byAction || cardOf(focused.cardId) || cardOf(scene.focusId);
        if (next) {
            next.focus({ preventScroll: true });
        }
    }

    // Pans so a control reached with the keyboard is inside the viewport.
    panTo(target) {
        this.viewport.scrollLeft = 0;
        this.viewport.scrollTop = 0;
        if (!target.matches(":focus-visible")) {
            return;
        }

        const rect = this.viewport.getBoundingClientRect();
        const box = target.getBoundingClientRect();
        const dx = box.left < rect.left + PADDING ? rect.left + PADDING - box.left
            : box.right > rect.right - PADDING ? rect.right - PADDING - box.right
                : 0;
        const dy = box.top < rect.top + PADDING ? rect.top + PADDING - box.top
            : box.bottom > rect.bottom - PADDING ? rect.bottom - PADDING - box.bottom
                : 0;
        if (dx || dy) {
            this.autoFit = false;
            this.view.x += dx;
            this.view.y += dy;
            this.apply();
        }
    }

    activate(target) {
        this.tooltip.hidden = true;
        this.call("OnGraphAction", target.dataset.action, target.dataset.target || "");
    }

    onHover(event) {
        const card = event.target.closest(".dep-card");
        const link = event.target.closest(".dep-edge-hit");
        this.highlight(card ? card.dataset.id : this.pinnedTrace || null);
        this.tooltipContent = card ? this.cardTooltip(card.dataset.id) : link ? this.linkTooltip(link.dataset.link) : null;
        this.tooltip.hidden = !this.tooltipContent;
        if (this.tooltipContent) {
            this.tooltip.replaceChildren(...this.tooltipContent);
            this.moveTooltip(event);
        }
    }

    moveTooltip(event) {
        if (this.tooltip.hidden) {
            return;
        }

        const host = this.canvas.getBoundingClientRect();
        const width = this.tooltip.offsetWidth;
        const height = this.tooltip.offsetHeight;
        const x = Math.min(event.clientX - host.left + 14, host.width - width - 8);
        const y = Math.min(event.clientY - host.top + 14, host.height - height - 8);
        this.tooltip.style.left = `${Math.max(8, x)}px`;
        this.tooltip.style.top = `${Math.max(8, y)}px`;
    }

    cardTooltip(id) {
        const card = this.scene && this.scene.cards.find((candidate) => candidate.id === id);
        if (!card) {
            return null;
        }

        const lines = [element("div", "dep-tooltip__title", card.label)];
        if (card.kind === "block") {
            if (card.tag) {
                lines.push(element("div", "dep-tooltip__mono", `{${card.tag}}`));
            }

            lines.push(element("div", "dep-tooltip__muted", card.detail || ""));
        } else if (card.kind === "stub") {
            lines.push(element("div", "dep-tooltip__muted", `${card.detail}. Click to show them.`));
            for (const line of card.summary || []) {
                lines.push(element("div", null, line));
            }
        } else if (card.kind === "section") {
            lines.push(element("div", "dep-tooltip__muted", "Click to open this section into its blocks."));
        } else {
            lines.push(element("div", "dep-tooltip__muted", card.detail || ""));
        }

        return lines;
    }

    linkTooltip(id) {
        const link = this.scene && this.scene.links.find((candidate) => candidate.id === id);
        if (!link) {
            return null;
        }

        const lines = [element("div", "dep-tooltip__title", linkTitle(link, this.scene.mode))];
        for (const tag of (link.tags || []).slice(0, TOOLTIP_TAGS)) {
            lines.push(element("div", "dep-tooltip__mono", link.style === "document" ? tag : `{${tag}}`));
        }

        if ((link.tags || []).length > TOOLTIP_TAGS) {
            lines.push(element("div", "dep-tooltip__muted", `and ${link.tags.length - TOOLTIP_TAGS} more`));
        }

        return lines;
    }

    // Moving between a card's children fires mouseover each time; only a change of card retraces.
    highlight(id) {
        if (id === this.highlighted) {
            return;
        }

        this.highlighted = id;
        const cards = this.stage.querySelectorAll(".dep-card");
        const paths = this.stage.querySelectorAll("path.dep-edge");
        this.stage.classList.toggle("dep-stage--tracing", !!id);
        if (!id || !this.scene) {
            cards.forEach((card) => card.classList.remove("dep-trace"));
            paths.forEach((path) => path.classList.remove("dep-trace"));
            return;
        }

        // The map traces a block's direct links only; a whole-workflow reach lights up half the map.
        const direct = this.scene.mode === "map";
        const upstream = direct ? neighbours(id, this.scene.links, "targetId", "sourceId") : reach(id, this.scene.links, "targetId", "sourceId");
        const downstream = direct ? neighbours(id, this.scene.links, "sourceId", "targetId") : reach(id, this.scene.links, "sourceId", "targetId");
        cards.forEach((card) => {
            const cardId = card.dataset.id;
            card.classList.toggle("dep-trace", cardId === id || upstream.has(cardId) || downstream.has(cardId));
        });
        const linksById = new Map(this.scene.links.map((link) => [link.id, link]));
        paths.forEach((path) => {
            const link = linksById.get(path.dataset.id);
            const onTrace = link
                && (((upstream.has(link.sourceId) || link.sourceId === id) && (upstream.has(link.targetId) || link.targetId === id))
                    || ((downstream.has(link.targetId) || link.targetId === id) && (downstream.has(link.sourceId) || link.sourceId === id)));
            path.classList.toggle("dep-trace", !!onTrace);
        });
    }

    watchPanZoom() {
        let drag = null;
        this.listen(this.viewport, "pointerdown", (event) => {
            if (event.button !== 0 || event.target.closest("button, [data-action]")) {
                return;
            }

            drag = { x: event.clientX, y: event.clientY, viewX: this.view.x, viewY: this.view.y, moved: false, pointer: event.pointerId };
        });
        this.listen(window, "pointermove", (event) => {
            if (!drag || event.pointerId !== drag.pointer) {
                return;
            }

            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.moved && Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) {
                return;
            }

            if (!drag.moved) {
                drag.moved = true;
                this.viewport.classList.add("dep-viewport--dragging");
            }

            this.autoFit = false;
            this.view.x = drag.viewX + dx;
            this.view.y = drag.viewY + dy;
            this.apply();
        });
        this.listen(window, "pointerup", () => {
            if (drag && drag.moved) {
                this.suppressClick = true;
                setTimeout(() => { this.suppressClick = false; }, 0);
            }

            drag = null;
            this.viewport.classList.remove("dep-viewport--dragging");
        });
        this.listen(this.viewport, "wheel", (event) => {
            event.preventDefault();
            this.autoFit = false;
            if (event.ctrlKey || event.metaKey) {
                const rect = this.viewport.getBoundingClientRect();
                this.zoomAt(Math.exp(-event.deltaY * 0.01), event.clientX - rect.left, event.clientY - rect.top);
                return;
            }

            this.view.x -= event.deltaX;
            this.view.y -= event.deltaY;
            this.apply();
        }, { passive: false });
    }

    zoomBy(factor) {
        const rect = this.viewport.getBoundingClientRect();
        this.autoFit = false;
        this.zoomAt(factor, rect.width / 2, rect.height / 2);
    }

    zoomAt(factor, x, y) {
        const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, this.view.scale * factor));
        this.view.x = x - (x - this.view.x) * scale / this.view.scale;
        this.view.y = y - (y - this.view.y) * scale / this.view.scale;
        this.view.scale = scale;
        this.apply();
    }

    centreOn(box, rect) {
        this.view.x = rect.width / 2 - (box.x + box.width / 2) * this.view.scale;
        this.view.y = rect.height / 2 - (box.y + box.height / 2) * this.view.scale;
        this.apply();
    }

    apply() {
        this.stage.style.transform = `translate(${this.view.x}px, ${this.view.y}px) scale(${this.view.scale})`;
    }

    // ---- the slide-over and the editor behind it ----

    watchSlideOver() {
        const aside = this.host.closest(SLIDE_OVER_SELECTOR);
        if (!aside) {
            return;
        }

        this.watchSlideOverWidth(aside);
        this.listen(document, "keydown", (event) => {
            if (event.key === "Escape" && !event.defaultPrevented && !overlayOpen()) {
                this.call("OnGraphAction", "close", "");
            }
        });

        // A click on an empty part of the editor closes the slide-over; one on a block, a control or
        // anything outside the editor leaves it open.
        this.listen(document, "click", (event) => {
            const target = event.target;
            if (!(target instanceof Element) || aside.contains(target) || !target.closest(EDITOR_SELECTOR)) {
                return;
            }

            const selection = window.getSelection();
            if (target.closest(".editor-block-card, " + INTERACTIVE_SELECTOR) || (selection && !selection.isCollapsed)) {
                return;
            }

            this.call("OnGraphAction", "close", "");
        });

        const opener = document.activeElement;
        this.disposers.push(() => {
            if (opener instanceof HTMLElement && opener.isConnected
                && (!document.activeElement || document.activeElement === document.body)) {
                opener.focus({ preventScroll: true });
            }
        });
    }

    // The left edge drags to resize; the width lasts as long as the slide-over is open.
    watchSlideOverWidth(aside) {
        const handle = aside.querySelector(".dep-slideover__resize");
        if (!handle) {
            return;
        }

        const setWidth = (width) => {
            const max = Math.max(MIN_SLIDE_OVER_WIDTH, window.innerWidth * MAX_SLIDE_OVER_SHARE);
            const clamped = Math.round(Math.min(max, Math.max(MIN_SLIDE_OVER_WIDTH, width)));
            aside.style.setProperty("--dep-slideover-width", `${clamped}px`);
        };

        let start = null;
        this.listen(handle, "pointerdown", (event) => {
            event.preventDefault();
            handle.setPointerCapture(event.pointerId);
            start = { x: event.clientX, width: aside.getBoundingClientRect().width };
            aside.classList.add("dep-slideover--resizing");
        });
        this.listen(handle, "pointermove", (event) => {
            if (start) {
                setWidth(start.width + (start.x - event.clientX));
            }
        });
        const finish = () => {
            start = null;
            aside.classList.remove("dep-slideover--resizing");
        };
        this.listen(handle, "pointerup", finish);
        this.listen(handle, "pointercancel", finish);
        this.listen(handle, "keydown", (event) => {
            const step = event.key === "ArrowLeft" ? 24 : event.key === "ArrowRight" ? -24 : 0;
            if (step) {
                event.preventDefault();
                setWidth(aside.getBoundingClientRect().width + step);
            }
        });
    }

    watchEditor() {
        const inEditor = (event) => event.target instanceof Element && event.target.closest(EDITOR_SELECTOR);
        const select = (event) => {
            const card = inEditor(event) && event.target.closest(".editor-block-card[id]");
            if (!card || card.id === this.lastSelected) {
                return;
            }

            this.lastSelected = card.id;
            this.call("OnEditorBlockSelected", card.id);
        };

        let timer = 0;
        const edited = (event) => {
            if (!inEditor(event)) {
                return;
            }

            clearTimeout(timer);
            timer = setTimeout(() => this.call("OnEditorEdited"), EDIT_DELAY_MS);
        };

        // Leaving a field counts as a possible edit: a picker that sets a value from a popup outside
        // the editor fires no input or change event in it. The view skips the redraw when nothing changed.
        this.listen(document, "pointerdown", select, true);
        this.listen(document, "focusin", select);
        this.listen(document, "focusout", edited);
        this.listen(document, "change", edited);
        this.listen(document, "input", edited);
        this.disposers.push(() => clearTimeout(timer));
    }

    // ---- plumbing ----

    call(method, ...args) {
        if (this.disposed) {
            return;
        }

        this.dotnet.invokeMethodAsync(method, ...args).catch((error) => console.warn("Dependency graph:", error));
    }

    listen(target, type, handler, options) {
        target.addEventListener(type, handler, options);
        this.disposers.push(() => target.removeEventListener(type, handler, options));
    }
}

// ---- scene to ELK ----

function neighbours(id, links, from, to) {
    return new Set(links.filter((link) => link[from] === id).map((link) => link[to]));
}

function mapFitScale(laid, rect) {
    if (!laid || !laid.width || !laid.height) {
        return 1;
    }

    return Math.min(1, (rect.width - PADDING * 2) / laid.width, (rect.height - PADDING * 2) / laid.height);
}

function toElkGraph(scene, aspectRatio, columns, rowWidth) {
    const chain = scene.mode === "chain";
    const root = {
        id: "root",
        layoutOptions: {
            "elk.algorithm": "layered",
            "elk.direction": "DOWN",
            "elk.hierarchyHandling": "INCLUDE_CHILDREN",
            "elk.edgeRouting": "SPLINES",
            "elk.layered.spacing.nodeNodeBetweenLayers": chain ? "52" : "64",
            "elk.spacing.nodeNode": "20",
            "elk.spacing.edgeNode": "14",
            "elk.layered.considerModelOrder.strategy": "NODES_AND_EDGES",
            "elk.layered.nodePlacement.strategy": "BRANDES_KOEPF",
            "elk.padding": `[top=${PADDING},left=${PADDING},bottom=${PADDING},right=${PADDING}]`,
            // Unlinked parts are packed to the view's shape instead of one long row.
            "elk.aspectRatio": String(Math.max(0.3, Math.min(3, aspectRatio))),
        },
        children: [],
        edges: [],
    };

    const groups = new Map(scene.groups.map((group) => [group.id, {
        id: group.id,
        layoutOptions: {
            "elk.algorithm": "layered",
            "elk.direction": "DOWN",
            "elk.padding": "[top=36,left=12,bottom=12,right=12]",
            "elk.spacing.nodeNode": "16",
            "elk.layered.spacing.nodeNodeBetweenLayers": "40",
        },
        children: [],
    }]));
    const placed = new Set();
    const ids = new Set();

    for (const card of scene.cards) {
        const node = { id: card.id, ...cardSize(card) };
        ids.add(card.id);
        const group = card.groupId ? groups.get(card.groupId) : null;
        if (!group) {
            root.children.push(node);
            continue;
        }

        group.children.push(node);
        if (!placed.has(group.id)) {
            placed.add(group.id);
            root.children.push(group);
        }
    }

    for (const link of scene.links) {
        if (ids.has(link.sourceId) && ids.has(link.targetId) && link.sourceId !== link.targetId) {
            root.edges.push({ id: link.id, sources: [link.sourceId], targets: [link.targetId] });
        }
    }

    // An open section's blocks would otherwise sit in one long row; unseen edges stack them in rows
    // as wide as the view, in authored order.
    for (const group of scene.groups) {
        const members = group.kind === "section" ? groups.get(group.id).children : [];
        for (let i = columns; i < members.length; i++) {
            root.edges.push({ id: `grid:${group.id}:${i}`, sources: [members[i - columns].id], targets: [members[i].id] });
        }
    }

    if (!chain) {
        wrapWideLayers(root, scene, columns, rowWidth);
        // The wrapped rows keep the authored order and stay as rows, rather than spreading out under
        // the cards they read.
        Object.assign(root.layoutOptions, {
            "elk.layered.crossingMinimization.forceNodeModelOrder": "true",
            "elk.layered.nodePlacement.strategy": "SIMPLE",
        });
    }

    // A large graph (a map with many sections open) trades a little crossing reduction for speed.
    if (scene.cards.length > LARGE_GRAPH_CARDS) {
        Object.assign(root.layoutOptions, {
            "elk.edgeRouting": "POLYLINE",
            "elk.layered.thoroughness": "2",
            "elk.layered.crossingMinimization.greedySwitch.type": "OFF",
        });
    }

    return root;
}

// ELK puts every card of a layer in one row, so twenty sections that read the same inputs make a row far
// wider than the view. Each layer of a linked part that would not fit is wrapped into rows as wide as the
// view, in the scene's order, by unseen edges that hang each extra row below the one before it.
function wrapWideLayers(root, scene, columns, rowWidth) {
    const children = root.children;
    const owner = new Map();
    for (const child of children) {
        owner.set(child.id, child.id);
        for (const inner of child.children || []) {
            owner.set(inner.id, child.id);
        }
    }

    const order = new Map(children.map((child, index) => [child.id, index]));
    const part = new Map(children.map((child) => [child.id, child.id]));
    const partOf = (id) => {
        while (part.get(id) !== id) {
            id = part.get(id);
        }

        return id;
    };
    const successors = new Map(children.map((child) => [child.id, new Set()]));
    const predecessors = new Map(children.map((child) => [child.id, new Set()]));
    for (const link of scene.links) {
        const source = owner.get(link.sourceId);
        const target = owner.get(link.targetId);
        if (source && target && source !== target) {
            successors.get(source).add(target);
            predecessors.get(target).add(source);
            if (partOf(source) !== partOf(target)) {
                part.set(partOf(target), partOf(source));
            }
        }
    }

    // Longest-path layers; a cycle is broken at its first card in scene order.
    const layer = new Map();
    const waiting = new Map(children.map((child) => [child.id, predecessors.get(child.id).size]));
    const queue = children.filter((child) => waiting.get(child.id) === 0).map((child) => child.id);
    while (layer.size < children.length) {
        if (!queue.length) {
            queue.push(children.find((child) => !layer.has(child.id)).id);
        }

        const id = queue.shift();
        if (layer.has(id)) {
            continue;
        }

        let depth = 0;
        for (const source of predecessors.get(id)) {
            if (layer.has(source)) {
                depth = Math.max(depth, layer.get(source) + 1);
            }
        }

        layer.set(id, depth);
        for (const target of successors.get(id)) {
            waiting.set(target, waiting.get(target) - 1);
            if (waiting.get(target) === 0 && !layer.has(target)) {
                queue.push(target);
            }
        }
    }

    const byLayer = new Map();
    for (const child of children) {
        const key = `${partOf(child.id)}|${layer.get(child.id)}`;
        if (!byLayer.has(key)) {
            byLayer.set(key, []);
        }

        byLayer.get(key).push(child);
    }

    const gap = 20;
    const widthOf = (node) => node.children
        ? Math.min(node.children.length, columns) * (CARD_SIZES.block.width + 16) + 24
        : node.width;
    let wrapped = 0;
    for (const nodes of byLayer.values()) {
        nodes.sort((a, b) => order.get(a.id) - order.get(b.id));
        const rows = [[]];
        let width = 0;
        for (const node of nodes) {
            const next = widthOf(node) + gap;
            if (rows[rows.length - 1].length && width + next > rowWidth) {
                rows.push([]);
                width = 0;
            }

            rows[rows.length - 1].push(node);
            width += next;
        }

        for (let r = 1; r < rows.length; r++) {
            rows[r].forEach((node, j) => {
                const above = rows[r - 1][Math.min(j, rows[r - 1].length - 1)];
                root.edges.push({ id: `wrap:${wrapped++}`, sources: [above.id], targets: [node.id] });
            });
        }
    }
}

// ELK packs the unlinked parts of the map (sections no other section reads) into rows by size. The map
// reads in authored order instead: each linked part keeps its own layout, and the parts run top to bottom
// in columns, as many as fit the view's width at a readable scale.
function packInColumns(laid, scene, maxWidth) {
    const children = laid.children || [];
    if (children.length < 2) {
        return;
    }

    const owner = new Map();
    const parent = new Map();
    for (const child of children) {
        owner.set(child.id, child.id);
        parent.set(child.id, child.id);
        for (const inner of child.children || []) {
            owner.set(inner.id, child.id);
        }
    }

    const find = (id) => {
        while (parent.get(id) !== id) {
            parent.set(id, parent.get(parent.get(id)));
            id = parent.get(id);
        }

        return id;
    };
    for (const link of scene.links) {
        const source = owner.get(link.sourceId);
        const target = owner.get(link.targetId);
        if (source && target && find(source) !== find(target)) {
            parent.set(find(target), find(source));
        }
    }

    const parts = new Map();
    for (const child of children) {
        const root = find(child.id);
        if (!parts.has(root)) {
            parts.set(root, { members: [], edges: [], left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity });
        }

        const part = parts.get(root);
        part.members.push(child);
        grow(part, child.x, child.y, child.x + child.width, child.y + child.height);
    }

    if (parts.size < 2) {
        return;
    }

    const rootEdges = (laid.edges || []).filter((edge) => !edge.container || edge.container === "root");
    for (const edge of rootEdges) {
        const part = parts.get(find(owner.get(edge.sources[0])));
        if (!part) {
            continue;
        }

        part.edges.push(edge);
        for (const point of edgePoints(edge)) {
            grow(part, point.x, point.y, point.x, point.y);
        }
    }

    const items = [...parts.values()];
    const columnsWidth = (limit) => {
        let x = 0;
        let y = 0;
        let width = 0;
        for (const item of items) {
            const height = item.bottom - item.top;
            if (y > 0 && y + height > limit) {
                x += width + PART_GAP;
                y = 0;
                width = 0;
            }

            y += height + PART_GAP;
            width = Math.max(width, item.right - item.left);
        }

        return x + width;
    };

    // The shortest columns whose total width fits the view; a part wider than the view gets a column of its own.
    const available = maxWidth - PADDING * 2;
    let low = Math.max(...items.map((item) => item.bottom - item.top));
    let high = items.reduce((sum, item) => sum + item.bottom - item.top + PART_GAP, 0);
    while (high - low > 8) {
        const middle = (low + high) / 2;
        if (columnsWidth(middle) <= available) {
            high = middle;
        } else {
            low = middle;
        }
    }

    let x = PADDING;
    let y = PADDING;
    let width = 0;
    let right = 0;
    let bottom = 0;
    for (const item of items) {
        const itemHeight = item.bottom - item.top;
        if (y > PADDING && y - PADDING + itemHeight > high) {
            x += width + PART_GAP;
            y = PADDING;
            width = 0;
        }

        const dx = x - item.left;
        const dy = y - item.top;
        for (const member of item.members) {
            member.x += dx;
            member.y += dy;
        }

        for (const edge of item.edges) {
            for (const point of edgePoints(edge)) {
                point.x += dx;
                point.y += dy;
            }
        }

        width = Math.max(width, item.right - item.left);
        right = Math.max(right, x + item.right - item.left);
        bottom = Math.max(bottom, y + itemHeight);
        y += itemHeight + PART_GAP;
    }

    laid.width = right + PADDING;
    laid.height = bottom + PADDING;
}

function grow(box, left, top, right, bottom) {
    box.left = Math.min(box.left, left);
    box.top = Math.min(box.top, top);
    box.right = Math.max(box.right, right);
    box.bottom = Math.max(box.bottom, bottom);
}

function edgePoints(edge) {
    const points = [];
    for (const section of edge.sections || []) {
        points.push(section.startPoint, ...(section.bendPoints || []), section.endPoint);
    }

    return points;
}

function cardSize(card) {
    if (card.kind === "input") {
        const detail = card.isUnresolved ? 0 : (card.detail || "").length * 6 + 8;
        const width = Math.min(320, Math.max(120, card.label.length * 6.8 + detail + 36));
        return { width, height: card.isUnresolved ? 40 : CARD_SIZES.input.height };
    }

    return CARD_SIZES[card.kind] || CARD_SIZES.block;
}

// In the Graph tab a block card shows its chain and the editor stays out of the way until the reader
// asks for it with Open in editor; in the slide-over every click on a block also scrolls the editor to it.
function cardAction(card, scene, slideOver) {
    switch (card.kind) {
        case "block":
            if (scene.mode === "map" || card.role !== "focus") {
                return {
                    name: "focus",
                    target: card.id,
                    label: slideOver ? `${card.label}: centre the chain here and show it in the editor` : `${card.label}: show its dependency chain`,
                };
            }

            return slideOver ? { name: "reveal", target: card.id, label: `${card.label}: show it in the editor` } : null;
        case "section":
            return { name: "section", target: card.id.slice(SECTION_CARD_PREFIX.length), label: `${card.label}: open the section` };
        case "stub":
            return { name: "stub", target: card.id, label: `${card.label}: show them` };
        default:
            return null;
    }
}

function arrowVariant(link, cardsById) {
    const source = cardsById.get(link.sourceId);
    if (source && source.isUnresolved) {
        return "issue";
    }

    if (link.style === "gate" || link.style === "document" || link.style === "counterparty") {
        return link.style;
    }

    if (link.style === "input" || link.style === "stub") {
        return "muted";
    }

    if (link.role === "up" || link.role === "down") {
        return link.role;
    }

    return "data";
}

function linkTitle(link, mode) {
    const count = link.weight > 1 && mode === "map" ? ` · ${link.weight} links` : "";
    switch (link.style) {
        case "document":
            return `Reads documents of this class${count}`;
        case "counterparty":
            return `Reads the counterparty value this block writes${count}`;
        case "gate":
            return `Section gate: decides whether this runs${count}`;
        case "input":
            return "Reads this input";
        case "stub":
            return "More blocks this way";
        default:
            return `Reads the output${count}`;
    }
}

function pathFromSections(sections, offset) {
    let d = "";
    for (const section of sections) {
        const start = shift(section.startPoint, offset);
        const points = [...(section.bendPoints || []), section.endPoint].map((point) => shift(point, offset));
        d += `M${start.x},${start.y}`;
        if (points.length % 3 === 0 && points.length > 1) {
            for (let i = 0; i < points.length; i += 3) {
                d += `C${points[i].x},${points[i].y} ${points[i + 1].x},${points[i + 1].y} ${points[i + 2].x},${points[i + 2].y}`;
            }
        } else {
            for (const point of points) {
                d += `L${point.x},${point.y}`;
            }
        }
    }

    return d;
}

function shift(point, offset) {
    return { x: point.x + offset.x, y: point.y + offset.y };
}

function reach(start, links, fromKey, toKey) {
    const seen = new Set();
    const queue = [start];
    while (queue.length) {
        const current = queue.shift();
        for (const link of links) {
            if (link[fromKey] !== current) {
                continue;
            }

            const next = link[toKey];
            if (next !== start && !seen.has(next)) {
                seen.add(next);
                queue.push(next);
            }
        }
    }

    return seen;
}

// ---- small DOM helpers ----

function element(tag, className, content) {
    const node = document.createElement(tag);
    if (className) {
        node.className = className;
    }

    if (content !== undefined) {
        node.textContent = content;
    }

    return node;
}

function text(content) {
    return document.createTextNode(content);
}

function svgElement(tag, attributes) {
    const node = document.createElementNS("http://www.w3.org/2000/svg", tag);
    for (const [name, value] of Object.entries(attributes)) {
        node.setAttribute(name, String(value));
    }

    return node;
}

function place(node, box) {
    node.style.left = `${box.x}px`;
    node.style.top = `${box.y}px`;
    node.style.width = `${box.width}px`;
    node.style.height = `${box.height}px`;
}

function countButton(action, id, count, noun, open) {
    const button = element("button", `dep-count${open ? " dep-count--open" : ""}`, `${count} ${noun}${count === 1 ? "" : "s"}`);
    button.type = "button";
    button.dataset.action = action;
    button.dataset.target = id;
    button.setAttribute("aria-pressed", open ? "true" : "false");
    button.title = open ? `Hide the ${noun}s` : `Show the ${noun}s`;
    return button;
}

function zoomButton(label, name, onClick) {
    const button = element("button", "dep-zoom__button", label);
    button.type = "button";
    button.setAttribute("aria-label", name);
    button.title = name;
    button.addEventListener("click", onClick);
    return button;
}

function loadScript(url) {
    return new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = url;
        script.onload = resolve;
        script.onerror = reject;
        document.head.append(script);
    });
}

function absoluteUrl(url) {
    return new URL(url, document.baseURI).href;
}

// A MudBlazor dialog or menu takes Esc for itself.
function overlayOpen() {
    return !!document.querySelector(".mud-dialog-container, .mud-popover-open:not(.mud-tooltip)");
}
