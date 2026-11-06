"""Checks the pages a reader of this repository follows: README.md, CONTRIBUTING.md, SECURITY.md and
every Markdown page under docs/.

Usage:
  python3 scripts/docs/check_docs.py [tree]

- A relative link reaches a file or folder of the tree, and its #anchor a heading of its page or an
  HTML anchor such as <a id="name">.
- An absolute link goes to a host on ALLOWED_HOSTS, so no page sends a reader to a host only some
  readers can open. Add a public host to the list when a page needs it.
- On a page under docs/, each repository path named in backticks exists in the tree. A path counts
  when its first folder is a project (IniaAi.*) or a top-level folder of the tree. A path that the
  tree's .gitignore ignores is a file the reader creates, such as a settings file from its template.

Prints one line per problem and exits 1 when there is any. Run it on the tree a reader gets.
"""

import fnmatch
import functools
import pathlib
import re
import sys
import urllib.parse

TOP_PAGES = ["README.md", "CONTRIBUTING.md", "SECURITY.md"]
DOCS = "docs"
ALLOWED_HOSTS = {
    "ai.azure.com",
    "atlassian.design",
    "bunit.dev",
    "c4model.com",
    "datatracker.ietf.org",
    "design-system.service.gov.uk",
    "design.gitlab.com",
    "developers.google.com",
    "docs.aws.amazon.com",
    "dotnet.microsoft.com",
    "echarts.apache.org",
    "exa.ai",
    "github.com",
    "gitlab.com",
    "learn.microsoft.com",
    "platform.claude.com",
    "privacy.claude.com",
    "reka-ui.com",
    "tiptap.dev",
    "vitest.dev",
    "vue-i18n.intlify.dev",
    "wundergraph.com",
    "www.docker.com",
    "www.nngroup.com",
    "www.rfc-editor.org",
    "zod.dev",
}
FENCE = re.compile(r"^(```|~~~).*?^\1[ \t]*$", re.M | re.S)
LINK = re.compile(r"\[[^\]]*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)|href=\"([^\"]+)\"")
HEADING = re.compile(r"^#{1,6}[ \t]+(.+?)[ \t#]*$", re.M)
HTML_ANCHOR = re.compile(r"<a\s+(?:id|name)=\"([^\"]+)\"")
CODE_SPAN = re.compile(r"`([^`\n]+)`")
PATH = re.compile(r"[\w.-]+(?:/[\w.-]+)+/?")


def pages(tree):
    found = [tree / name for name in TOP_PAGES if (tree / name).is_file()]
    if (tree / DOCS).is_dir():
        found += sorted((tree / DOCS).rglob("*.md"))
    return found


def without_code_blocks(text):
    return FENCE.sub("", text)


def anchor(heading):
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", heading).replace("`", "").lower()
    return re.sub(r"[^\w\- ]", "", text).replace(" ", "-")


@functools.cache
def anchors(page):
    text = without_code_blocks(page.read_text(encoding="utf-8"))
    seen = {}
    result = set(HTML_ANCHOR.findall(text))
    for heading in HEADING.findall(text):
        slug = anchor(heading)
        count = seen.get(slug, 0)
        seen[slug] = count + 1
        result.add(slug if count == 0 else f"{slug}-{count}")
    return result


def link_problems(tree, page, text):
    name = page.relative_to(tree).as_posix()
    for match in LINK.finditer(text):
        target = match.group(1) or match.group(2)
        parsed = urllib.parse.urlsplit(target)
        if parsed.scheme in ("http", "https"):
            if parsed.hostname not in ALLOWED_HOSTS:
                yield f"{name}: the link to {target} goes to {parsed.hostname}, which is not on ALLOWED_HOSTS"
            continue
        if parsed.scheme or target.startswith("//"):
            continue

        destination = (page.parent / urllib.parse.unquote(parsed.path)).resolve() if parsed.path else page.resolve()
        if not destination.exists():
            yield f"{name}: the link to {target} reaches no file"
            continue
        if parsed.fragment and destination.suffix == ".md" and parsed.fragment not in anchors(destination):
            shown = destination.relative_to(tree.resolve()).as_posix()
            yield f"{name}: {shown} has no heading for the anchor #{parsed.fragment}"


def ignored_patterns(tree):
    gitignore = tree / ".gitignore"
    if not gitignore.is_file():
        return []
    lines = (line.strip() for line in gitignore.read_text(encoding="utf-8").splitlines())
    return [line.strip("/") for line in lines if line and not line.startswith(("#", "!"))]


def is_ignored(path, patterns):
    parts = path.strip("/").split("/")
    for pattern in patterns:
        if "/" in pattern:
            if fnmatch.fnmatchcase("/".join(parts), pattern):
                return True
        elif any(fnmatch.fnmatchcase(part, pattern) for part in parts):
            return True
    return False


def path_problems(tree, page, text, top_level, patterns):
    name = page.relative_to(tree).as_posix()
    for span in CODE_SPAN.findall(text):
        if not PATH.fullmatch(span):
            continue
        first = span.split("/", 1)[0]
        if not (first.startswith("IniaAi.") or first in top_level):
            continue
        if not (tree / span).exists() and not is_ignored(span, patterns):
            yield f"{name}: names {span}, which is not in the tree"


def problems(tree, checked=None):
    tree = pathlib.Path(tree)
    top_level = {entry.name for entry in tree.iterdir()}
    patterns = ignored_patterns(tree)
    found = []
    for page in checked if checked is not None else pages(tree):
        text = without_code_blocks(page.read_text(encoding="utf-8"))
        found += link_problems(tree, page, text)
        if page.relative_to(tree).parts[0] == DOCS:
            found += path_problems(tree, page, text, top_level, patterns)
    return found


def main(argv):
    tree = pathlib.Path(argv[0] if argv else ".")
    checked = pages(tree)
    found = problems(tree, checked)
    for line in found:
        print(line)
    if found:
        return 1
    print(f"docs check passed: {len(checked)} pages")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
