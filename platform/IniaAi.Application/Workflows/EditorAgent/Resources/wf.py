"""Looks things up in the staged workflow files and prints compact answers.

Usage: python3 wf.py [--docs DIR] <command> [arguments]

  find TEXT [--field prompt|name|any]   blocks whose prompts, names or any field contain TEXT
  block REF [REF ...]                   a block's essentials: type, section, model settings,
                                        documents, RAG, inputs, length and format lines, prompts
  block REF --full                      the same with the prompts in full
  section REF                           a section's blocks in execution order (REF: number or title)
  readers REF                           what reads a block, variable or document class
  feeds REF                             what a block reads
  report ID [REF] [--full]              a staged past report: its header and every block's status,
                                        or one block of it with all its period and variant copies:
                                        each copy's output and the prompt as sent
  find --report ID TEXT                 blocks of a staged report whose output, prompt or error holds TEXT
  docs ID                               a staged report's input and output documents: files, pages, sizes
  grep ID TEXT                          the pages of a report's documents that hold TEXT, with page numbers
  reports                               the list of past reports
  explain                               every block type and what it does when the workflow runs
  explain TYPE [SETTING]                a block type's settings and what each does, or one setting in full;
                                        TYPE may also be Block, Section or Workflow for their own fields

REF is a StableBlockId, a tag, or a block name (exact, else a unique part of one).
Every command loads the files once; run several in one call: wf.py block A; wf.py block B
"""

import gzip
import json
import os
import re
import sys

PROMPT_KEYS = ["SystemPromptTemplate", "UserPromptTemplate", "PromptTemplate", "TextTemplate", "InputTemplate"]
NAME_KEYS = ["Name", "ReplacementTag"]
PROMPT_CAP = 3000
SNIPPET = 70
FIND_LIMIT = 40
OUTPUT_CAP = 1500
REPORT_OUTPUT_CAP = 6000
REPORT_MATCH_LIMIT = 12
GREP_LIMIT = 40
PAGE_MARKER = r"^=== %s page (\d+)(?: \(([^)]*)\))? ===$"
SCHEMA_FILE = "workflow.schema.json"
SKIPPED_SETTINGS = {"ExecutionState", "Type"}
NODE_DEFINITIONS = {"block": "Block", "section": "Section", "workflow": "Workflow"}
SUMMARY_CAP = 170


def page_marker(document):
    # Each document's page lines carry a marker drawn when it was staged, so a line of the
    # document's own text that reads like a page break is not taken for one.
    marker = document.get("pageMarker")
    return re.compile(PAGE_MARKER % re.escape(marker)) if marker else None
RESPONSE_KEYS = ["RawResponse", "MarkdownResponse", "JsonResponse"]
SENT_PROMPT_KEYS = ["UserPrompt", "Prompt", "UserPromptPreview"]
SHAPE_PATTERN = re.compile(
    r"\b(words?|sentences?|paragraphs?|bullets?|bullet points?|concise|brief|briefly|short|shorter|length|lengthy|"
    r"limit|maximum|at most|no more than|up to \d+|characters|format|markdown|table|json|headings?|tone|succinct|verbose)\b",
    re.IGNORECASE)
SHAPE_LINES = 10
RAG_TYPES = {
    0: "whole document", 1: "top N pages", 2: "top pages per filter, with neighbours", 3: "top page",
    4: "top page and following pages", 5: "pages from an index", 6: "keyword pages", 7: "exact pages",
    8: "table of contents", 10: "auto-detected table of contents", 11: "search agent",
}


class Staged:
    def __init__(self, docs):
        self.docs = docs
        self._workflow = None
        self._map = None

    def path(self, name):
        return os.path.join(self.docs, name)

    @property
    def workflow(self):
        if self._workflow is None:
            with open(self.path("workflow.json"), encoding="utf-8-sig") as handle:
                self._workflow = json.load(handle)
        return self._workflow

    @property
    def dependency_map(self):
        if self._map is None:
            try:
                with open(self.path("dependency-map.json"), encoding="utf-8") as handle:
                    self._map = json.load(handle)
            except OSError:
                self._map = {"blocks": [], "inputs": [], "edges": []}
        return self._map

    def sections(self):
        found = self.workflow.get("Sections") or self.workflow.get("Children") or []
        return sorted(found, key=lambda section: section.get("Order", 0))

    def blocks(self):
        for section in self.sections():
            ordered = sorted(section.get("Blocks") or [], key=lambda block: block.get("Order", 0))
            for position, block in enumerate(ordered):
                yield section, position, block

    def resolve(self, ref):
        ref = ref.strip().strip("{}")
        everything = list(self.blocks())
        for test in (
            lambda b: b.get("StableBlockId") == ref,
            lambda b: b.get("ReplacementTag") == ref,
            lambda b: (b.get("Name") or "").lower() == ref.lower(),
            lambda b: (b.get("ReplacementTag") or "").lower() == ref.lower(),
            lambda b: ref.lower() in (b.get("Name") or "").lower(),
        ):
            hits = [entry for entry in everything if test(entry[2])]
            if hits:
                return hits
        return []

    def block_of(self, node_id):
        for _, _, block in self.blocks():
            if block.get("StableBlockId") == node_id:
                return block
        return None

    def name_of(self, node_id):
        for _, _, block in self.blocks():
            if block.get("StableBlockId") == node_id:
                return block.get("Name") or block.get("ReplacementTag") or node_id
        for node in self.dependency_map.get("inputs", []):
            if node.get("id") == node_id:
                return "{%s} (%s)" % (node.get("tag"), plain_kind(node.get("kind")))
        return node_id


def plain_kind(kind):
    return re.sub(r"(?<!^)([A-Z])", r" \1", kind or "input").lower()


def section_title(section):
    return section.get("Title") or section.get("Name") or "untitled"


def one_line(text):
    return re.sub(r"\s+", " ", text or "").strip()


def snippet(text, match_at, length):
    start = max(0, match_at - SNIPPET // 2)
    end = min(len(text), match_at + length + SNIPPET // 2)
    return ("…" if start > 0 else "") + one_line(text[start:end]) + ("…" if end < len(text) else "")


def heading(section, position, block):
    library = ", Library" if block.get("SharedComponentId") else ""
    return "%s.%s [%s] %s  tag %s  id %s  (section %s%s)" % (
        section.get("Order", 0), position, block.get("Type"), block.get("Name"),
        block.get("ReplacementTag") or "-", block.get("StableBlockId") or "-", section_title(section), library)


def strings_of(value, prefix=""):
    if isinstance(value, str):
        yield prefix, value
    elif isinstance(value, dict):
        for key, item in value.items():
            if key in ("ExecutionState", "Id"):
                continue
            yield from strings_of(item, (prefix + "." if prefix else "") + key)
    elif isinstance(value, list):
        for item in value:
            yield from strings_of(item, prefix)


def find(staged, text, field):
    needle = text.lower()
    hits = 0
    for section, position, block in staged.blocks():
        props = block.get("Properties") or {}
        if field == "name":
            fields = [(key, block.get(key) or "") for key in NAME_KEYS]
        elif field == "prompt":
            fields = [(key, props.get(key) or "") for key in PROMPT_KEYS]
        else:
            fields = [(key, block.get(key) or "") for key in NAME_KEYS] + list(strings_of(props))
            fields += list(strings_of(block.get("RagSettings") or [], "RagSettings"))
        matches = []
        for key, value in fields:
            at = value.lower().find(needle)
            if at >= 0:
                matches.append("%s: %s (%d hits)" % (key, snippet(value, at, len(text)), value.lower().count(needle)))
        if matches:
            hits += 1
            if hits <= FIND_LIMIT:
                print("%s.%s %s [%s] id %s" % (section.get("Order", 0), position, block.get("Name"), block.get("Type"), block.get("StableBlockId")))
                for line in matches[:3]:
                    print("    " + line)
    if hits == 0:
        print('No block %s contains "%s".' % ({"name": "name", "prompt": "prompt"}.get(field, "field"), text))
    elif hits > FIND_LIMIT:
        print("... and %d more blocks. Narrow the text." % (hits - FIND_LIMIT))
    else:
        print("%d blocks." % hits)


def model_line(block):
    props = block.get("Properties") or {}
    block_type = block.get("Type")
    if block_type == "Agent" or "PromptTemplate" in props:
        return "model: %s · effort: %s · thinking: decided by the agent" % (
            props.get("Model") or "default", props.get("Effort") or "not set")
    if "UserPromptTemplate" not in props and "SystemPromptTemplate" not in props:
        return None
    override = props.get("ModelIdOverride")
    model = override or ("advanced model (default)" if props.get("ShouldUseAdvancedModel") else "simple model (default)")
    thinking = "on, budget %s" % props.get("ThinkingBudget") if props.get("ShouldUseThinkingMode") else "off"
    extra = " · max tokens %s" % props["MaxTokens"] if props.get("MaxTokens") else ""
    return "model: %s (ModelIdOverride %s) · effort: %s · thinking: %s%s" % (
        model, override or "not set", props.get("Effort") or "not set", thinking, extra)


def rag_lines(block):
    lines = []
    for rag in block.get("RagSettings") or []:
        kind = rag.get("Type")
        label = RAG_TYPES.get(kind, kind) if isinstance(kind, int) else kind
        parts = [str(label)]
        if rag.get("Keywords"):
            parts.append("keywords " + ", ".join(rag["Keywords"]))
        for key in ("NumberOfPagesInput", "ExactIndexesInput", "StartIndexInput", "EndIndexInput", "QueryTemplate", "TopicsQuery"):
            if rag.get(key):
                parts.append("%s %s" % (key.replace("Input", "").replace("Template", ""), one_line(str(rag[key]))[:80]))
        if rag.get("ShouldSearchOverviews"):
            parts.append("searches overviews")
        if rag.get("ShouldIncludeNeighboringPages"):
            parts.append("with neighbouring pages")
        lines.append("; ".join(parts))
    return lines


def edges(staged, node_id, direction):
    key, other = ("to", "from") if direction == "in" else ("from", "to")
    return [edge for edge in staged.dependency_map.get("edges", []) if edge.get(key) == node_id]


def identity(tag, block_id):
    """A block's own tag and id, the id a link names; a copy gives the id of the block it was copied from."""
    return "(tag %s, id %s)" % (tag, block_id) if tag else "(id %s)" % block_id


def edge_line(staged, edge, other):
    """The block at the other end with its own tag and id, then the tags the edge carries, labelled as
    what the reader reads, so a reader is never taken to own the tag it reads or linked by another id."""
    tags = ", ".join("{%s}" % tag for tag in edge.get("tags") or [])
    kind = edge.get("kind") or ""
    node = edge.get(other)
    block = staged.block_of(node)
    label = staged.name_of(node)
    if block is not None:
        label += " " + identity(block.get("ReplacementTag"), node)
    carried = ""
    if tags:
        carried = (" reads %s" if other == "to" else ", read as %s") % tags
    return label + carried + ("" if kind == "Data" else " (%s)" % plain_kind(kind))


def shape_lines(props):
    found = []
    for key in PROMPT_KEYS:
        for number, line in enumerate((props.get(key) or "").splitlines(), 1):
            if SHAPE_PATTERN.search(line):
                found.append("%s L%d: %s" % (key.replace("Template", ""), number, one_line(line)[:160]))
    return found


def capped(text, full):
    if full or len(text) <= PROMPT_CAP:
        return text
    return text[:PROMPT_CAP] + "\n[%d more characters: add --full]" % (len(text) - PROMPT_CAP)


def show_block(staged, section, position, block, full):
    props = block.get("Properties") or {}
    print("== " + heading(section, position, block))
    model = model_line(block)
    if model:
        print(model)
    classes = [str(name) for name in block.get("SupportedDocumentClasses") or [] if name]
    if classes:
        print("documents: " + ", ".join(classes))
    for line in rag_lines(block):
        print("RAG: " + line)
    reads = [edge_line(staged, edge, "from") for edge in edges(staged, block.get("StableBlockId"), "in")]
    if reads:
        print("reads: " + "; ".join(reads))
    read_by = [edge_line(staged, edge, "to") for edge in edges(staged, block.get("StableBlockId"), "out")]
    if read_by:
        print("read by: " + "; ".join(read_by))
    shape = shape_lines(props)
    if shape:
        print("length and format lines:")
        for line in shape[:SHAPE_LINES]:
            print("  " + line)
        if len(shape) > SHAPE_LINES:
            print("  ... %d more" % (len(shape) - SHAPE_LINES))
    for key in PROMPT_KEYS:
        text = props.get(key)
        if text:
            print("--- %s (%d chars, %d words)" % (key, len(text), len(text.split())))
            print(capped(text, full))
    other = [(key, value) for key, value in props.items()
             if key not in PROMPT_KEYS and key not in ("ExecutionState", "Type") and isinstance(value, str) and value and "Preview" not in key]
    for key, value in other[:8]:
        print("%s: %s" % (key, one_line(value)[:200]))
    response = (block.get("BlockResponse") or {}).get("MarkdownResponse")
    if response:
        print("--- test-run output (%d words)" % len(response.split()))
        print(capped(response, full) if full else response[:OUTPUT_CAP] + ("…" if len(response) > OUTPUT_CAP else ""))
    else:
        print("test-run output: none in the editor")


def block(staged, refs, full):
    for ref in refs:
        hits = staged.resolve(ref)
        if not hits:
            print('No block matches "%s". Try: wf.py find "%s" --field name' % (ref, ref))
        elif len(hits) > 1 and not any(entry[2].get("StableBlockId") == ref for entry in hits) and len(hits) > 3:
            print('%d blocks match "%s":' % (len(hits), ref))
            for section, position, found in hits[:15]:
                print("  " + heading(section, position, found))
        else:
            for section, position, found in hits:
                show_block(staged, section, position, found, full)


def section(staged, ref):
    sections = staged.sections()
    chosen = [s for s in sections if str(s.get("Order")) == ref.strip()] or \
        [s for s in sections if ref.lower() in section_title(s).lower()]
    if not chosen:
        print('No section matches "%s". Sections: %s' % (ref, "; ".join(
            "%s. %s" % (s.get("Order"), section_title(s)) for s in sections)))
        return
    for current in chosen[:3]:
        blocks = sorted(current.get("Blocks") or [], key=lambda b: b.get("Order", 0))
        print("== %s. %s (%d blocks)" % (current.get("Order"), section_title(current), len(blocks)))
        for position, found in enumerate(blocks):
            props = found.get("Properties") or {}
            words = sum(len((props.get(key) or "").split()) for key in PROMPT_KEYS)
            tested = " · tested" if (found.get("BlockResponse") or {}).get("MarkdownResponse") else ""
            print("  %s.%s [%s] %s  tag %s  id %s  · prompt %d words%s" % (
                current.get("Order"), position, found.get("Type"), found.get("Name"),
                found.get("ReplacementTag") or "-", found.get("StableBlockId"), words, tested))


def node_ids(staged, ref):
    ids = [entry[2].get("StableBlockId") for entry in staged.resolve(ref)]
    tag = ref.strip().strip("{}")
    ids += [node.get("id") for node in staged.dependency_map.get("inputs", []) if node.get("tag") == tag]
    return ids


def links(staged, ref, direction):
    ids = node_ids(staged, ref)
    if not ids:
        print('Nothing matches "%s".' % ref)
        return
    for node_id in ids[:5]:
        found = edges(staged, node_id, direction)
        other = "to" if direction == "out" else "from"
        verb = "read by" if direction == "out" else "reads"
        print("== %s %s %d:" % (staged.name_of(node_id), verb, len(found)))
        for edge in found:
            print("  " + edge_line(staged, edge, other))


class Run:
    """A staged past report: its header and its executed workflow."""

    def __init__(self, staged, report_id):
        self.id = report_id
        self.header = None
        self.workflow = None
        self.missing = False
        try:
            with open(staged.path("report-%s.md" % report_id), encoding="utf-8") as handle:
                self.header = handle.read().strip()
        except OSError:
            self.missing = True
            return
        path = staged.path("report-%s.workflow.json" % report_id)
        if os.path.exists(path):
            with open(path, encoding="utf-8-sig") as handle:
                self.workflow = json.load(handle)
        elif os.path.exists(path + ".gz"):
            with gzip.open(path + ".gz", "rt", encoding="utf-8-sig") as handle:
                self.workflow = json.load(handle)

    def blocks(self):
        if not self.workflow:
            return
        sections = sorted(self.workflow.get("Sections") or self.workflow.get("Children") or [], key=lambda s: s.get("Order", 0))
        for section in sections:
            ordered = sorted(section.get("Blocks") or [], key=lambda block: block.get("Order", 0))
            for position, block in enumerate(ordered):
                yield section, position, block

    def variants(self):
        return {block.get("Variant") for _, _, block in self.blocks() if block.get("Variant")}


def output_of(block):
    response = block.get("BlockResponse") or {}
    for key in RESPONSE_KEYS:
        if response.get(key):
            return response[key]
    return ""


def sent_prompt_of(block):
    state = (block.get("Properties") or {}).get("ExecutionState") or {}
    for key in SENT_PROMPT_KEYS:
        if state.get(key):
            return state[key]
    return ""


def status_of(block):
    state = block.get("ExecutionState") or {}
    return state.get("Status") or "NotExecuted", state.get("Error") or ""


def run_heading(section, position, block):
    variant = " · variant %s" % block["Variant"] if block.get("Variant") else ""
    template = " · copy of %s" % block["TemplateStableBlockId"] if block.get("TemplateStableBlockId") else ""
    status, _ = status_of(block)
    return "%s.%s [%s] %s  tag %s  id %s  (section %s%s%s) · %s · output %d words" % (
        section.get("Order", 0), position, block.get("Type"), block.get("Name"), block.get("ReplacementTag") or "-",
        block.get("StableBlockId") or "-", section_title(section), variant, template, status, len(output_of(block).split()))


def run_matches(staged, run, ref):
    """The run's blocks for REF: the block and every copy of it, by source link, then by tag and variant suffix."""
    ref = ref.strip().strip("{}")
    lower = ref.lower()
    everything = list(run.blocks())
    template_ids = {ref}
    try:
        template_ids |= {block.get("StableBlockId") for _, _, block in staged.blocks() if block.get("StableBlockId") and (
            (block.get("ReplacementTag") or "").lower() == lower or (block.get("Name") or "").lower() == lower)}
    except OSError:
        pass
    suffixed = {lower + "_" + variant.lower() for variant in run.variants()}

    def linked(block):
        return block.get("StableBlockId") in template_ids or block.get("TemplateStableBlockId") in template_ids

    def tagged(block):
        tag = (block.get("ReplacementTag") or "").lower()
        return tag == lower or (block.get("TemplateTag") or "").lower() == lower or tag in suffixed

    def named(block):
        return (block.get("Name") or "").lower() == lower

    for test in (lambda b: linked(b) or tagged(b) or named(b),
                 lambda b: lower in (b.get("Name") or "").lower() or lower in (b.get("ReplacementTag") or "").lower()):
        hits = [entry for entry in everything if test(entry[2])]
        if hits:
            return hits
    return []


def capped_to(text, cap, full):
    if full or len(text) <= cap:
        return text
    return text[:cap] + "\n[%d more characters: add --full]" % (len(text) - cap)


def report(staged, report_id, ref, full):
    run = Run(staged, report_id)
    if run.missing:
        print("Report %s is not staged. Write out/open-reports.json as {\"ids\": [\"%s\"]} and end the answer." % (report_id, report_id))
        return
    if run.workflow is None:
        print(run.header)
        return
    if ref:
        hits = run_matches(staged, run, ref)
        if not hits:
            print('No block of the report matches "%s". Try: wf.py find --report %s "%s"' % (ref, report_id, ref))
            return
        if len(hits) > REPORT_MATCH_LIMIT:
            print('%d blocks of the report match "%s"; name one:' % (len(hits), ref))
            for section, position, found in hits[:40]:
                print("  " + run_heading(section, position, found))
            return
        for section, position, found in hits:
            print("== " + run_heading(section, position, found))
            _, error = status_of(found)
            if error:
                print("error: " + one_line(error))
            print("--- output")
            print(capped_to(output_of(found), REPORT_OUTPUT_CAP, full) or "(none)")
            prompt = sent_prompt_of(found)
            if prompt:
                print("--- prompt as sent")
                print(capped_to(prompt, PROMPT_CAP, full))
        return
    print(run.header)
    print()
    current = None
    for section, position, found in run.blocks():
        if section is not current:
            current = section
            print("== %s. %s" % (section.get("Order", 0), section_title(section)))
        status, error = status_of(found)
        error = " · error: " + one_line(error)[:120] if error else ""
        print("  %s.%s [%s] %s %s · %s · %d words%s" % (
            section.get("Order", 0), position, found.get("Type"), found.get("Name"),
            identity(found.get("ReplacementTag"), found.get("TemplateStableBlockId") or found.get("StableBlockId")),
            status, len(output_of(found).split()), error))


def find_in_report(staged, report_id, text):
    run = Run(staged, report_id)
    if run.missing or run.workflow is None:
        report(staged, report_id, None, False)
        return
    needle = text.lower()
    hits = 0
    for section, position, found in run.blocks():
        fields = [("output", output_of(found)), ("prompt as sent", sent_prompt_of(found)), ("error", status_of(found)[1])]
        matches = []
        for key, value in fields:
            at = value.lower().find(needle)
            if at >= 0:
                matches.append("%s: %s (%d hits)" % (key, snippet(value, at, len(text)), value.lower().count(needle)))
        if matches:
            hits += 1
            if hits <= FIND_LIMIT:
                print(run_heading(section, position, found))
                for line in matches:
                    print("    " + line)
    if hits == 0:
        print('No block of report %s has "%s" in its output, prompt or error.' % (report_id, text))
    elif hits > FIND_LIMIT:
        print("... and %d more blocks. Narrow the text." % (hits - FIND_LIMIT))
    else:
        print("%d blocks." % hits)


def manifest(staged, report_id):
    try:
        with open(staged.path("report-%s.documents.json" % report_id), encoding="utf-8") as handle:
            return json.load(handle).get("documents") or []
    except OSError:
        return None


def size(count):
    if count is None:
        return "-"
    return "%.1f MB" % (count / 1048576.0) if count >= 1048576 else "%d KB" % round(count / 1024.0)


def not_staged(staged, report_id):
    if os.path.exists(staged.path("report-%s.md" % report_id)):
        print("Report %s has no staged documents." % report_id)
    else:
        print("Report %s is not staged. Write out/open-reports.json as {\"ids\": [\"%s\"]} and end the answer." % (report_id, report_id))


def list_documents(staged, report_id):
    documents = manifest(staged, report_id)
    if documents is None:
        not_staged(staged, report_id)
        return
    for role in ("input", "output"):
        group = [document for document in documents if document.get("role") == role]
        if not group:
            continue
        print("== %s documents" % role)
        for document in group:
            print("  %s (%s): %d pages, %s" % (document.get("name"), document.get("class"), document.get("pages") or 0, size(document.get("bytes"))))
            print("    original: %s" % (document.get("file") or "not staged: %s" % document.get("leftOut")))
            if document.get("text"):
                print("    text: %s (%s)" % (document["text"], size(document.get("textBytes"))))
            elif document.get("textLeftOut"):
                print("    text: not staged: %s" % document["textLeftOut"])
            else:
                print("    text: none stored, so the original is not staged")


def grep_documents(staged, report_id, text):
    documents = manifest(staged, report_id)
    if documents is None:
        not_staged(staged, report_id)
        return
    needle = text.lower()
    hits = 0
    pages_hit = 0
    searched = 0
    for document in documents:
        marker_line = page_marker(document)
        if not document.get("text") or marker_line is None:
            continue
        searched += 1
        found_in = []
        page, note, lines = None, None, []

        def flush():
            body = "\n".join(lines)
            at = body.lower().find(needle)
            if at >= 0:
                found_in.append((page, note, body.lower().count(needle), snippet(body, at, len(text))))

        with open(staged.path(document["text"]), encoding="utf-8") as handle:
            for line in handle:
                marker = marker_line.match(line.rstrip("\n"))
                if marker:
                    if page is not None:
                        flush()
                    page, note, lines = int(marker.group(1)), marker.group(2), []
                elif page is not None:
                    lines.append(line.rstrip("\n"))
            if page is not None:
                flush()
        if not found_in:
            continue
        pages_hit += len(found_in)
        print("== %s (%s, %s): %d pages" % (document.get("name"), document.get("role"), document.get("text"), len(found_in)))
        for number, extra, count, line in found_in:
            hits += 1
            if hits <= GREP_LIMIT:
                print("  p.%s%s (%d hits): %s" % (number, " (%s)" % extra if extra else "", count, line))
    if searched == 0:
        print("Report %s has no staged page text to search." % report_id)
    elif pages_hit == 0:
        print('No page of report %s\'s documents holds "%s".' % (report_id, text))
    elif hits > GREP_LIMIT:
        print("... %d pages in all. Narrow the text." % pages_hit)
    else:
        print("%d pages." % pages_hit)


def reports(staged):
    try:
        with open(staged.path("reports.md"), encoding="utf-8") as handle:
            print(handle.read().strip())
    except OSError:
        print("No reports list is staged.")


def load_schema():
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), SCHEMA_FILE), encoding="utf-8") as handle:
        return json.load(handle)


def block_types(schema):
    """Each block type's description and the definition that holds its settings."""
    properties = schema["definitions"]["BlockProperties"]
    lines = properties["properties"]["Type"].get("description", "").split("\n")
    descriptions = dict(line.split(": ", 1) for line in lines if ": " in line)
    definitions = {type_name_of(branch): branch["then"]["$ref"].split("/")[-1]
                   for branch in properties.get("allOf", [])}
    return descriptions, definitions


def type_name_of(branch):
    """A branch matches a type by name and by number; an extension's type has only its number."""
    values = branch["if"]["properties"]["Type"]["enum"]
    return next((value for value in values if isinstance(value, str)), str(values[0]))


def first_sentences(text, cap):
    text = (text or "").split("\n")[0]
    return text if len(text) <= cap else text[:cap].rsplit(" ", 1)[0] + "..."


def explain(args):
    schema = load_schema()
    descriptions, definitions = block_types(schema)
    if not args:
        for name in sorted(definitions):
            print("%s: %s" % (name, descriptions.get(name, "(no description)")))
        return 0

    wanted = args[0].lower()
    name = next((n for n in definitions if n.lower() == wanted), None)
    definition_name = definitions.get(name) if name else NODE_DEFINITIONS.get(wanted)
    if definition_name is None or definition_name not in schema["definitions"]:
        print("no block type %s; the types are: %s" % (args[0], ", ".join(sorted(definitions))))
        return 0

    definition = schema["definitions"][definition_name]
    settings = {key: value for key, value in definition.get("properties", {}).items() if key not in SKIPPED_SETTINGS}
    if len(args) > 1:
        key = next((k for k in settings if k.lower() == args[1].lower()), None)
        if key is None:
            print("%s has no setting %s; its settings are: %s" % (args[0], args[1], ", ".join(settings)))
            return 0
        print("%s.%s: %s" % (name or definition_name, key, settings[key].get("description", "(no description)")))
        return 0

    print("%s: %s" % (name or definition_name, descriptions.get(name, definition.get("description", ""))))
    for key, value in settings.items():
        print("  %s: %s" % (key, first_sentences(value.get("description"), SUMMARY_CAP) or "(no description)"))
    return 0


def main(argv):
    docs = "docs"
    if len(argv) > 2 and argv[1] == "--docs":
        docs, argv = argv[2], [argv[0]] + argv[3:]
    if len(argv) < 2 or argv[1] in ("-h", "--help", "help"):
        print(__doc__)
        return 0
    command, args = argv[1], argv[2:]
    if command == "explain":
        return explain(args)
    full = "--full" in args
    args = [arg for arg in args if arg != "--full"]
    report_id = None
    if "--report" in args:
        at = args.index("--report")
        report_id = args[at + 1] if at + 1 < len(args) else None
        args = args[:at] + args[at + 2:]
    field = "prompt"
    if "--field" in args:
        at = args.index("--field")
        field = args[at + 1] if at + 1 < len(args) else "prompt"
        args = args[:at] + args[at + 2:]
    staged = Staged(docs)
    try:
        if command == "find" and args and report_id:
            find_in_report(staged, report_id, " ".join(args))
        elif command == "find" and args:
            find(staged, " ".join(args), field)
        elif command == "block" and args:
            block(staged, args, full)
        elif command == "section" and args:
            section(staged, " ".join(args))
        elif command == "readers" and args:
            links(staged, " ".join(args), "out")
        elif command == "feeds" and args:
            links(staged, " ".join(args), "in")
        elif command == "report" and args:
            report(staged, args[0], " ".join(args[1:]) or None, full)
        elif command == "docs" and args:
            list_documents(staged, args[0])
        elif command == "grep" and len(args) > 1:
            grep_documents(staged, args[0], " ".join(args[1:]))
        elif command == "reports":
            reports(staged)
        else:
            print(__doc__)
            return 2
    except OSError as error:
        print("cannot read the staged files: %s" % error)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
