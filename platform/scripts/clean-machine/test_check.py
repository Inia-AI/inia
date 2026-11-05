import contextlib
import copy
import http.server
import io
import json
import pathlib
import re
import socket
import threading
import time
import unittest
import urllib.parse
from unittest import mock

import check

HERE = pathlib.Path(__file__).parent
EXPECTED_BY_TYPE = {expected["workflowTypeId"]: expected for _, expected in check.samples()}
EXPECTED = EXPECTED_BY_TYPE["SampleAnnualReport"]
REPORT = json.loads((HERE / "testdata" / "report.json").read_text(encoding="utf-8"))
SHOWCASE_EXPECTED = EXPECTED_BY_TYPE["SampleBlockShowcase"]
SHOWCASE_REPORT = json.loads((HERE / "testdata" / "showcase-report.json").read_text(encoding="utf-8"))
SHOWCASE_DATA = json.loads((HERE / "testdata" / "showcase-data.json").read_text(encoding="utf-8"))
AGENT_REPORT = json.loads((HERE / "testdata" / "agent-report.json").read_text(encoding="utf-8"))


def paragraph(report):
    return report["value"][0]["paragraphs"][0]


def citation(report, citation_id):
    return next(item for item in paragraph(report)["citations"] if item["id"] == citation_id)


def part(report, tag):
    return next(item for item in check.paragraphs(report) if item["replacementTag"] == tag)


class StubInstall(http.server.ThreadingHTTPServer):
    """An install that answers the check's requests from the recorded runs. Each run's state comes
    from STATES by workflow type, and a run that is not there finishes as a draft."""

    STATES = {}

    def __init__(self):
        super().__init__(("127.0.0.1", 0), StubHandler)
        self.runs = {}
        self.requests = []
        self.usernames = []
        threading.Thread(target=self.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True).start()

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server_address[1]}"


class StubHandler(http.server.BaseHTTPRequestHandler):
    RECORDED = {
        "SampleAnnualReport": (REPORT, {"value": []}),
        "SampleBlockShowcase": (SHOWCASE_REPORT, SHOWCASE_DATA),
        "SampleAgentRun": (AGENT_REPORT, {"value": []}),
    }

    def log_message(self, *args):
        pass

    def answer(self, body, content_type="application/json"):
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        self.server.requests.append(("POST", self.path))
        body = self.rfile.read(int(self.headers["Content-Length"])).decode(errors="replace")
        if self.path.endswith("/connect/token"):
            self.server.usernames.append(urllib.parse.parse_qs(body)["username"][0])
            self.answer({"access_token": "token"})
            return
        workflow_type = re.search(r'"workflowTypeId": "([^"]+)"', body).group(1)
        execution_id = f"run-{workflow_type}"
        self.server.runs[execution_id] = workflow_type
        self.answer({"id": execution_id})

    def do_GET(self):
        self.server.requests.append(("GET", self.path))
        if self.path.startswith("/api/v1/pages/"):
            self.answer(b"image", "image/png")
            return
        execution_id, _, view = self.path.removeprefix("/api/v1/workflow-reports/").partition("/")
        workflow_type = self.server.runs[execution_id]
        report, data = self.RECORDED[workflow_type]
        if view == "elements":
            self.answer(report)
        elif view == "data":
            self.answer(data)
        else:
            self.answer({"workflowState": StubInstall.STATES.get(workflow_type, "Draft")})


def run_check(install):
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        status = check.main(["--url", install.url, "--password", "unused"])
    return status, output.getvalue()


class CheckTests(unittest.TestCase):
    def test_a_sample_that_needs_a_part_the_install_lacks_is_left_out(self):
        kept = [expected["workflowTypeId"] for _, expected in check.samples(without={"agent-runtime"})]
        self.assertNotIn("SampleAgentRun", kept)
        self.assertIn("SampleAnnualReport", kept)
        self.assertIn("SampleAgentRun", [expected["workflowTypeId"] for _, expected in check.samples()])

    def test_the_expected_report_passes(self):
        self.assertEqual([], check.problems(REPORT, EXPECTED))

    def test_a_missing_figure_fails_and_names_it(self):
        report = copy.deepcopy(REPORT)
        paragraph(report)["content"] = paragraph(report)["content"].replace('<fact id="c1">EUR 5.9 million</fact>', "unknown")

        self.assertEqual(["Net profit for 2025: no figure reads EUR 5.9 million"], check.problems(report, EXPECTED))

    def test_a_wrong_value_fails_and_names_the_figure(self):
        report = copy.deepcopy(REPORT)
        paragraph(report)["content"] = paragraph(report)["content"].replace("EUR 112.4 million", "EUR 121.4 million")

        self.assertEqual(["Total assets at the end of 2025: no figure reads EUR 112.4 million"], check.problems(report, EXPECTED))

    def test_a_citation_to_another_page_fails_and_names_both_pages(self):
        report = copy.deepcopy(REPORT)
        citation(report, "c0")["sources"][0]["filePageNumber"] = 3

        self.assertEqual(
            ["Revenue for 2025: EUR 48.6 million cites page 3 of annual-report.pdf, not page 2"],
            check.problems(report, EXPECTED))

    def test_an_unverified_citation_fails(self):
        report = copy.deepcopy(REPORT)
        citation(report, "c3")["sources"][0]["unverified"] = True

        self.assertEqual(
            ["Employees at the end of 2025: the citation of 1,284 to page 5 is marked unverified"],
            check.problems(report, EXPECTED))

    def test_a_figure_written_with_other_spacing_or_case_still_passes(self):
        report = copy.deepcopy(REPORT)
        paragraph(report)["content"] = paragraph(report)["content"].replace("EUR 48.6 million", "eur  48.6 Million")

        self.assertEqual([], check.problems(report, EXPECTED))

    def test_a_connection_the_install_drops_is_an_install_error(self):
        with socket.create_server(("127.0.0.1", 0)) as server:
            def drop_one():
                connection, _ = server.accept()
                connection.close()

            threading.Thread(target=drop_one, daemon=True).start()
            with self.assertRaises(check.InstallError):
                check.Install(f"http://127.0.0.1:{server.getsockname()[1]}", "unused")

    def test_the_cited_pages_are_the_expected_ones(self):
        self.assertEqual(4, len(check.cited_pages(REPORT, EXPECTED)))


class ShowcaseTests(unittest.TestCase):
    def test_the_expected_showcase_report_passes(self):
        self.assertEqual([], check.problems(SHOWCASE_REPORT, SHOWCASE_EXPECTED))

    def test_an_output_that_does_not_say_a_phrase_fails_and_names_the_phrase(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        summary = part(report, "summary")
        summary["content"] = summary["content"].replace("in euros", "in US dollars")

        self.assertEqual(['summary: it does not say "The report states its figures in euros."'], check.problems(report, SHOWCASE_EXPECTED))

    def test_a_missing_output_fails_and_names_it(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        for element in report["value"]:
            element["paragraphs"] = [item for item in element["paragraphs"] if item["replacementTag"] != "netMargin"]

        self.assertEqual(["netMargin: the report has no part from this block"], check.problems(report, SHOWCASE_EXPECTED))

    def test_a_renamed_block_still_passes(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        part(report, "summary")["title"] = "Overview"

        self.assertEqual([], check.problems(report, SHOWCASE_EXPECTED))

    def test_a_chart_point_with_another_value_fails_and_names_the_point(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        points = part(report, "figuresChart")["chartPayload"]["chartData"]
        next(point for point in points if point["x"] == "2025" and point["series"] == "Revenue")["y"] = 48.7

        self.assertEqual(
            ["figuresChart: the chart shows Revenue for 2025 as 48.7, not 48.6"],
            check.problems(report, SHOWCASE_EXPECTED))

    def test_a_chart_without_a_point_fails_and_names_the_point(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        chart = part(report, "figuresChart")["chartPayload"]
        chart["chartData"] = [point for point in chart["chartData"] if point["series"] != "Net profit" or point["x"] != "2024"]

        self.assertEqual(
            ["figuresChart: the chart has no point for Net profit in 2024"],
            check.problems(report, SHOWCASE_EXPECTED))

    def test_the_expected_showcase_data_passes(self):
        self.assertEqual([], check.data_problems(SHOWCASE_DATA, SHOWCASE_EXPECTED))

    def test_a_data_value_that_differs_fails_and_names_it(self):
        data = copy.deepcopy(SHOWCASE_DATA)
        data["value"][0]["data"]["revenue_growth_percent"] = 10.3

        self.assertEqual(
            ["figures_and_ratios: revenue_growth_percent is 10.3, not 10.2"],
            check.data_problems(data, SHOWCASE_EXPECTED))

    def test_a_summary_without_the_company_name_in_capitals_fails(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        summary = part(report, "summary")
        summary["content"] = summary["content"].replace("LARKSPUR FREIGHT LTD", "Larkspur Freight Ltd")

        self.assertEqual(
            ['summary: it does not say "LARKSPUR FREIGHT LTD grew its revenue by 10.2% in 2025, and its net margin was 12.1%."'],
            check.problems(report, SHOWCASE_EXPECTED))

    def test_a_page_that_keeps_its_markers_and_footer_fails(self):
        report = copy.deepcopy(SHOWCASE_REPORT)
        page = part(report, "pageTwoText")
        page["content"] = f"<page2(annual-report.pdf)>{page['content']}\n        Page 2\n</page2(annual-report.pdf)>"

        self.assertEqual(
            ['pageTwoText: it still says "<page"', 'pageTwoText: it still says "Page 2"'],
            check.problems(report, SHOWCASE_EXPECTED))

    def test_a_data_output_that_is_not_an_object_fails(self):
        data = copy.deepcopy(SHOWCASE_DATA)
        data["value"][0]["data"] = ["not", "an", "object"]

        self.assertEqual(
            ["figures_and_ratios: the data output is not a JSON object"],
            check.data_problems(data, SHOWCASE_EXPECTED))

    def test_a_data_output_without_a_value_names_the_value(self):
        data = copy.deepcopy(SHOWCASE_DATA)
        del data["value"][0]["data"]["net_margin_percent"]

        self.assertEqual(
            ["figures_and_ratios: it has no net_margin_percent"],
            check.data_problems(data, SHOWCASE_EXPECTED))

    def test_a_missing_data_output_fails_and_names_it(self):
        self.assertEqual(
            ["figures_and_ratios: the run has no data output with this name"],
            check.data_problems({"value": []}, SHOWCASE_EXPECTED))


class SampleTests(unittest.TestCase):
    def test_every_sample_with_an_expected_answer_is_checked(self):
        self.assertEqual(
            ["SampleAgentRun", "SampleAnnualReport", "SampleBlockShowcase"],
            [expected["workflowTypeId"] for _, expected in check.samples()])



class MainTests(unittest.TestCase):
    def setUp(self):
        self.install = StubInstall()
        self.addCleanup(self.install.server_close)
        self.addCleanup(self.install.shutdown)
        self.addCleanup(StubInstall.STATES.clear)

    def test_matching_samples_print_a_pass_line_each_and_exit_0(self):
        status, output = run_check(self.install)

        self.assertEqual(0, status)
        self.assertIn("PASS SampleAnnualReport matches samples/annual-report/expected.json.", output)
        self.assertIn("PASS SampleBlockShowcase matches samples/block-showcase/expected.json.", output)
        self.assertIn("PASS SampleAgentRun matches samples/agent-run/expected.json.", output)

    def test_a_stopped_run_fails_its_sample_and_the_other_sample_still_passes(self):
        StubInstall.STATES["SampleBlockShowcase"] = "Failed"

        status, output = run_check(self.install)

        self.assertEqual(2, status)
        self.assertIn("FAIL SampleBlockShowcase: The sample run run-SampleBlockShowcase ended as Failed.", output)
        self.assertIn("PASS SampleAnnualReport matches samples/annual-report/expected.json.", output)

    def test_the_next_sample_starts_after_the_last_run_is_read(self):
        run_check(self.install)

        requests = self.install.requests
        self.assertLess(
            requests.index(("GET", "/api/v1/workflow-reports/run-SampleAgentRun/data")),
            requests.index(("POST", "/api/v1/workflow-executions/submit"), 2))

    def test_a_sample_the_install_lacks_a_part_for_is_skipped_and_named(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            status = check.main(["--url", self.install.url, "--password", "unused", "--without", "agent-runtime"])

        self.assertEqual(0, status)
        self.assertIn("SKIP SampleAgentRun: it needs the agent runtime, which this install runs without.", output.getvalue())
        self.assertEqual(2, self.install.requests.count(("POST", "/api/v1/workflow-executions/submit")))
        self.assertNotIn(("GET", "/api/v1/workflow-reports/run-SampleAgentRun"), self.install.requests)

    def test_no_sample_with_an_expected_answer_fails(self):
        with mock.patch.object(check, "samples", return_value=[]):
            status, output = run_check(self.install)

        self.assertEqual(2, status)
        self.assertIn("No folder of samples/ holds an expected.json", output)

    def test_the_check_signs_in_as_the_first_admin_the_install_seeded(self):
        check.Install(self.install.url, "unused")
        check.Install(self.install.url, "unused", email="owner@example.com")

        self.assertEqual(["superadmin@example.com", "owner@example.com"], self.install.usernames)

    def test_a_run_that_finished_is_read_once_even_after_the_deadline(self):
        install = check.Install(self.install.url, "unused")
        self.install.runs["run-SampleAnnualReport"] = "SampleAnnualReport"

        install.wait("run-SampleAnnualReport", time.monotonic() - 1)


if __name__ == "__main__":
    unittest.main()
