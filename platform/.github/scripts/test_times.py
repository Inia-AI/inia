"""Check each test project's run time against its budget and list the slow tests.

Usage: test_times.py <results directory> <budgets file> [<more budgets files>...]

Reads the xUnit TRX reports under the results directory. A project fails when it runs longer than
its budget in the budgets file, or has no budget. Every test over the report threshold is listed in
the job summary without failing. Exit code 1 means a project failed its budget.
"""

import glob
import json
import os
import sys
import xml.etree.ElementTree as ET
from datetime import datetime

TRX = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
REPORT_THRESHOLD_SECONDS = 2.0


def parse_time(value):
    # TRX writes seven fractional digits; Python reads at most six.
    head, offset = value[:-6], value[-6:]
    if "." in head:
        whole, fraction = head.split(".")
        head = f"{whole}.{fraction[:6]}"
    return datetime.fromisoformat(head + offset)


def parse_duration(value):
    hours, minutes, seconds = value.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def read_reports(results_directory):
    projects = {}
    slow_tests = []
    for path in glob.glob(os.path.join(results_directory, "**", "*.trx"), recursive=True):
        root = ET.parse(path).getroot()
        first_test = root.find(".//t:UnitTest", TRX)
        times = root.find("t:Times", TRX)
        if first_test is None or times is None:
            continue

        project = os.path.basename(first_test.get("storage").replace("\\", "/")).removesuffix(".dll")
        elapsed = (parse_time(times.get("finish")) - parse_time(times.get("start"))).total_seconds()
        projects[project] = projects.get(project, 0.0) + elapsed

        class_of = {test.get("id"): test.find("t:TestMethod", TRX).get("className") for test in root.iterfind(".//t:UnitTest", TRX)}
        for result in root.iterfind(".//t:UnitTestResult", TRX):
            duration = result.get("duration")
            if duration and parse_duration(duration) > REPORT_THRESHOLD_SECONDS:
                test_class = class_of.get(result.get("testId"), "").split(".")[-1]
                test_name = result.get("testName").split(".")[-1]
                slow_tests.append((parse_duration(duration), project, f"{test_class}.{test_name}"))

    return projects, sorted(slow_tests, reverse=True)


def main(results_directory, budgets_path, *more_budgets_paths):
    budgets = {}
    for path in (budgets_path, *more_budgets_paths):
        with open(path, encoding="utf-8") as budgets_file:
            budgets.update(json.load(budgets_file))

    projects, slow_tests = read_reports(results_directory)
    if not projects:
        print(f"No TRX report under {results_directory}.")
        return 1

    failures = []
    lines = ["### Test time per project", "", "| Project | Seconds | Budget |", "|---|---:|---:|"]
    for project, seconds in sorted(projects.items(), key=lambda entry: -entry[1]):
        budget = budgets.get(project)
        if budget is None:
            failures.append(f"{project} has no budget. Add it to {budgets_path}, at twice its slowest time on a CI runner.")
        elif seconds > budget:
            failures.append(f"{project} took {seconds:.0f} s, over its {budget} s budget. Find what made it slower, or raise the budget in {budgets_path} and say why in the pull request.")
        lines.append(f"| {project} | {seconds:.0f} | {budget if budget is not None else 'none'} |")

    lines += ["", f"### Tests over {REPORT_THRESHOLD_SECONDS:.0f} s ({len(slow_tests)})", ""]
    lines += [f"- {seconds:.1f} s: {name} ({project})" for seconds, project, name in slow_tests]
    if failures:
        lines += ["", "### Failed"] + [f"- {failure}" for failure in failures]

    summary = "\n".join(lines) + "\n"
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as summary_file:
            summary_file.write(summary)
    print(summary)

    for failure in failures:
        print(f"::error::{failure}")
    return 1 if failures else 0


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(*sys.argv[1:]))
