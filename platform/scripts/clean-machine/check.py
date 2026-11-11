"""Checks a running install the way a newcomer would. It runs each sample workflow of samples/ on the
document that the sample's expected.json names, and compares the run with that file. It checks each
figure and the page it cites, the text of each block's part of the report, each point of a chart,
and each value of a data output.

Usage:
  python3 scripts/clean-machine/check.py [--url http://localhost:8080] [--samples security-samples]

It signs in as the first admin, the address in INIA_ADMIN_EMAIL (superadmin@example.com when unset, as
the stack seeds it), with the password in INIA_ADMIN_PASSWORD. The --password option also takes it,
but leaves it in the shell history. Each run calls the model once
with the install's own key. Exit code 0 when every report matches, 1 when one does not, 2 when the
install cannot be reached or a run fails.
"""

import argparse
import json
import os
import pathlib
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "samples"
EXPECTED_FILE = "expected.json"
DEFAULT_ADMIN_EMAIL = "superadmin@example.com"
TENANT = "IniaAI"
FINISHED = {"Draft", "Confirmed"}
STOPPED = {"Failed", "Cancelled"}
PARTS = {"agent-runtime": "the agent runtime"}
RUN_TIMEOUT_SECONDS = 900
POLL_SECONDS = 5
FACT = re.compile(r'<fact id="([^"]+)">(.*?)</fact>', re.S)
TAG = re.compile(r"<[^>]+>")


class InstallError(Exception):
    pass


def collapsed(text):
    return " ".join(text.split())


def normalized(text):
    return collapsed(text).lower()


def samples(root=SAMPLES, without=frozenset()):
    """Each sample folder under root that holds an expected answer, with that answer, in name order.
    A sample whose answer names a part in "needs" that the install runs without stays out."""
    found = [(path.parent, json.loads(path.read_text(encoding="utf-8"))) for path in sorted(root.glob(f"*/{EXPECTED_FILE}"))]
    return [(folder, expected) for folder, expected in found if not set(expected.get("needs", [])) & set(without)]


def document_path(folder, expected):
    return folder / expected["document"]


def document_name(expected):
    return pathlib.PurePosixPath(expected["document"]).name


def paragraphs(report):
    for element in report.get("value", []):
        yield from element.get("paragraphs", [])


def facts(report):
    for paragraph in paragraphs(report):
        citations = {item["id"]: item.get("sources", []) for item in paragraph.get("citations") or []}
        for fact_id, text in FACT.findall(paragraph.get("content") or ""):
            yield text, citations.get(fact_id, [])


def document_sources(sources, document):
    return [source for source in sources if source.get("type") == "Document" and source.get("documentName") == document]


def figure_sources(report, figure, document):
    """Whether the report states the figure's value, and the sources it cites for it in the document."""
    matches = [sources for text, sources in facts(report) if normalized(text) == normalized(figure["value"])]
    return bool(matches), [source for sources in matches for source in document_sources(sources, document)]


def problems(report, expected):
    """One line for each part of the expected answer that the report does not match."""
    return figure_problems(report, expected) + output_problems(report, expected)


def figure_problems(report, expected):
    """One line for each expected figure the report does not state, or does not cite to its page."""
    found = []
    document = document_name(expected)
    for figure in expected.get("figures", []):
        stated, cited = figure_sources(report, figure, document)
        on_page = [source for source in cited if source.get("filePageNumber") == figure["page"]]
        if not stated:
            found.append(f"{figure['name']}: no figure reads {figure['value']}")
        elif any(not source.get("unverified") for source in on_page):
            continue
        elif on_page:
            found.append(f"{figure['name']}: the citation of {figure['value']} to page {figure['page']} is marked unverified")
        elif cited:
            found.append(f"{figure['name']}: {figure['value']} cites page {cited[0].get('filePageNumber')} of {document}, not page {figure['page']}")
        else:
            found.append(f"{figure['name']}: {figure['value']} has no citation to {document}")
    return found


def output_problems(report, expected):
    """One line for each expected block whose part of the report is missing, does not say a text,
    still says a text it should not, or charts another point. Texts compare with their case."""
    parts = {paragraph.get("replacementTag"): paragraph for paragraph in paragraphs(report)}
    found = []
    for output in expected.get("outputs", []):
        part = parts.get(output["tag"])
        if part is None:
            found.append(f"{output['tag']}: the report has no part from this block")
            continue
        content = part.get("content") or ""
        text = collapsed(TAG.sub("", content))
        found += [f'{output["tag"]}: it does not say "{says}"' for says in output.get("says", []) if collapsed(says) not in text]
        found += [f'{output["tag"]}: it still says "{lacks}"' for lacks in output.get("lacks", []) if lacks in content]
        found += chart_problems(output, part)
    return found


def chart_problems(output, part):
    shown = {(point.get("series"), point.get("x")): point.get("y") for point in (part.get("chartPayload") or {}).get("chartData") or []}
    found = []
    for point in output.get("chart", []):
        key = (point["series"], point["x"])
        if key not in shown:
            found.append(f"{output['tag']}: the chart has no point for {point['series']} in {point['x']}")
        elif shown[key] != point["y"]:
            found.append(f"{output['tag']}: the chart shows {point['series']} for {point['x']} as {shown[key]}, not {point['y']}")
    return found


def data_problems(data, expected):
    """One line for each expected data output the run does not have, and each value it holds that
    differs."""
    outputs = {output.get("name"): output.get("data") for output in data.get("value", [])}
    found = []
    for name, values in expected.get("data", {}).items():
        if name not in outputs:
            found.append(f"{name}: the run has no data output with this name")
            continue
        output = outputs[name]
        if not isinstance(output, dict):
            found.append(f"{name}: the data output is not a JSON object")
            continue
        for key, value in values.items():
            if key not in output:
                found.append(f"{name}: it has no {key}")
            elif output[key] != value:
                found.append(f"{name}: {key} is {output[key]}, not {value}")
    return found


def cited_pages(report, expected):
    """The page id of each expected figure's citation, to open its image."""
    pages = {}
    for figure in expected.get("figures", []):
        _, cited = figure_sources(report, figure, document_name(expected))
        for source in cited:
            if source.get("filePageNumber") == figure["page"] and source.get("pageId"):
                pages[figure["name"]] = source["pageId"]
    return pages


class Install:
    def __init__(self, url, password, email=DEFAULT_ADMIN_EMAIL):
        self._api = url.rstrip("/") + "/api/v1"
        self._token = self._sign_in(email, password)

    def _request(self, method, path, body=None, content_type=None, token=True):
        headers = {"X-Tenant-Id": TENANT}
        if token:
            headers["Authorization"] = f"Bearer {self._token}"
        if content_type:
            headers["Content-Type"] = content_type
        request = urllib.request.Request(self._api + path, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.status, response.headers.get("Content-Type", ""), response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.headers.get("Content-Type", ""), error.read()
        except urllib.error.URLError as error:
            raise InstallError(f"Cannot reach {self._api}: {error.reason}. Start the install, or pass its address with --url.") from error
        except OSError as error:
            raise InstallError(f"The connection to {self._api} failed: {error}. Check that the install is running.") from error

    def _json(self, method, path, body=None, content_type=None):
        status, _, data = self._request(method, path, body, content_type)
        if status >= 400:
            raise InstallError(f"{method} {path} answered {status}: {data[:300].decode(errors='replace')}")
        return json.loads(data)

    def _sign_in(self, email, password):
        form = urllib.parse.urlencode({"grant_type": "password", "username": email, "password": password, "scope": "api"}).encode()
        status, _, data = self._request("POST", "/connect/token", form, "application/x-www-form-urlencoded", token=False)
        if status != 200:
            raise InstallError(f"The first admin, {email}, could not sign in ({status}). Check the address and the password.")
        return json.loads(data)["access_token"]

    def run_sample(self, folder, expected):
        boundary = uuid.uuid4().hex
        pdf = document_path(folder, expected).read_bytes()
        metadata = json.dumps({"workflowTypeId": expected["workflowTypeId"]})
        body = b"".join([
            f'--{boundary}\r\nContent-Disposition: form-data; name="metadata"\r\nContent-Type: application/json\r\n\r\n{metadata}\r\n'.encode(),
            f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="{document_name(expected)}"\r\nContent-Type: application/pdf\r\n\r\n'.encode(),
            pdf,
            f"\r\n--{boundary}--\r\n".encode(),
        ])
        execution = self._json("POST", "/workflow-executions/submit", body, f"multipart/form-data; boundary={boundary}")
        return execution["id"]

    def wait(self, execution_id, deadline):
        while True:
            state = self._json("GET", f"/workflow-reports/{execution_id}").get("workflowState")
            if state in FINISHED:
                return
            if state in STOPPED:
                raise InstallError(f"The sample run {execution_id} ended as {state}. Its block logs say why.")
            if time.monotonic() >= deadline:
                raise InstallError(f"The sample run {execution_id} did not finish in {RUN_TIMEOUT_SECONDS // 60} minutes.")
            time.sleep(POLL_SECONDS)

    def report(self, execution_id):
        return self._json("GET", f"/workflow-reports/{execution_id}/elements")

    def data(self, execution_id):
        return self._json("GET", f"/workflow-reports/{execution_id}/data")

    def page_image_opens(self, page_id):
        status, content_type, data = self._request("GET", f"/pages/{page_id}/image")
        return status == 200 and content_type.startswith("image/") and len(data) > 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--url", default="http://localhost:8080", help="the address of the install (default: %(default)s)")
    parser.add_argument("--email", default=os.environ.get("INIA_ADMIN_EMAIL") or DEFAULT_ADMIN_EMAIL, help="the first admin's address (default: INIA_ADMIN_EMAIL, else %(default)s)")
    parser.add_argument("--password", default=os.environ.get("INIA_ADMIN_PASSWORD"), help="the first admin's password")
    parser.add_argument("--without", action="append", default=[], choices=sorted(PARTS), help="a part the install runs without; the samples that need it stay out")
    parser.add_argument("--samples", type=pathlib.Path, default=SAMPLES, help="the folder of samples to run (default: samples/)")
    args = parser.parse_args(argv)
    if not args.password:
        print("Give the first admin's password with --password or INIA_ADMIN_PASSWORD.")
        return 2

    for _, expected in samples(args.samples):
        for part in sorted(set(expected.get("needs", [])) & set(args.without)):
            print(f"SKIP {expected['workflowTypeId']}: it needs {PARTS[part]}, which this install runs without.")
    found_samples = samples(args.samples, args.without)
    if not found_samples:
        print(f"No folder of {os.path.relpath(args.samples, ROOT)}/ holds an {EXPECTED_FILE}, so there is nothing to check.")
        return 2
    try:
        install = Install(args.url, args.password, args.email)
    except InstallError as error:
        print(error)
        return 2

    status = 0
    # One run at a time: two runs that upload the same file at once can store its pages twice.
    for folder, expected in found_samples:
        try:
            execution_id = install.run_sample(folder, expected)
            print(f"Started the run {execution_id} of the sample workflow {expected['workflowTypeId']}.")
            install.wait(execution_id, time.monotonic() + RUN_TIMEOUT_SECONDS)
            report = install.report(execution_id)
            found = problems(report, expected) + data_problems(install.data(execution_id), expected)
            for name, page_id in cited_pages(report, expected).items():
                if not install.page_image_opens(page_id):
                    found.append(f"{name}: the image of the cited page does not open")
        except InstallError as error:
            print(f"FAIL {expected['workflowTypeId']}: {error}")
            status = 2
            continue
        for line in found:
            print(f"FAIL {expected['workflowTypeId']}: {line}")
        if found:
            status = max(status, 1)
        else:
            print(f"PASS {expected['workflowTypeId']} matches {os.path.relpath(folder / EXPECTED_FILE, ROOT)}.")
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
