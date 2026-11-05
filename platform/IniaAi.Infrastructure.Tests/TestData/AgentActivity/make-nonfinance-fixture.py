#!/usr/bin/env python3
"""Derive the non-finance fixture: the reference pack with every run-data channel that
carries finance vocabulary swapped to a clinical-trial domain. Structure, timestamps,
uuids, and record ordering are preserved byte-for-byte apart from the swaps."""
import json
import re
import sys

SRC = sys.argv[1]
DST = sys.argv[2]

NEUTRAL_SKILL_TEXT = (
    "Base directory for this skill: /workspaces/home-VqN0S9/skills/skills/adverse-event-summary\n\n"
    "# Adverse Event Summary - Extraction Skill\n\n## Goal\n\n"
    "Extract the specified adverse event categories for the requested trial cohorts, "
    "cross-tabulated against the reported totals, as a single structured output."
)
NEUTRAL_CHECKLIST = (
    "Checklist:\n1. [ ] Read docs/manifest.json\n2. [ ] Convert PDFs to text\n"
    "3. [ ] Locate adverse event tables, appendices, enrollment summary\n"
    "4. [ ] Cross-check cohorts vs assessment_year\n5. [ ] View table pages as images\n"
    "6. [ ] Write out/ae-01-event-counts.json\n7. [ ] Write out/ae-02-routing.json\n"
    "8. [ ] Write out/ae-03-* section files"
)

SENTENCE_SWAPS = [
    ("Reconciliation ties exactly for both periods.",
     "Cross-tabulation ties exactly for both cohorts."),
    ("PDF page 49 = Statements of Operations, page 48 = Cash Flows. Let me find note pages too.",
     "PDF page 49 = Adverse Event Tables, page 48 = Enrollment Summary. Let me find appendix pages too."),
    ("Now let's view the income statement page as an image to confirm layout.",
     "Now let's view the adverse event table page as an image to confirm layout."),
    ("Now the section files: net sales, D&A, impairments, exceptionals, COGS/SG&A.",
     "Now the section files: enrollment, dosing, adverse events, discontinuations, demographics."),
    ("Now the reconciliation file:", "Now the cross-tabulation file:"),
]

WORD_SWAPS = [
    ("Amazon-2024-Annual-Report-NEW", "Atlas-2024-Trial-Protocol-NEW"),
    ("Amazon-2022-Annual-Report-NEW", "Atlas-2022-Trial-Protocol-NEW"),
    ("amazon2024", "atlas2024"),
    ("amazon2022", "atlas2022"),
    ("income-statement-line-items", "adverse-event-summary"),
    ("CONSOLIDATED STATEMENTS OF OPERATIONS", "SUMMARY OF ADVERSE EVENTS"),
    ("STATEMENTS OF OPERATIONS", "ADVERSE EVENT TABLES"),
    ("STATEMENTS OF CASH FLOWS", "ENROLLMENT TABLES"),
    ("Net product sales", "Screened participants"),
    ("is-03-da", "ae-03-dosing"),
    ("Statements of Operations", "Adverse Event Tables"),
    ("Statements of Cash Flows", "Enrollment Tables"),
    ("Statements of Comprehensive", "Listing of Concomitant"),
    ("impairment", "withdrawal"),
    ("Impairment", "Withdrawal"),
    ("restructur", "discontinu"),
    ("goodwill", "cohort-b"),
    ("Goodwill", "Cohort-B"),
    ("payroll", "dosing"),
    ("stock-based compensation expense by", "protocol deviation listing by"),
    ("stock-based compensation.*consist", "protocol deviation.*consist"),
    ("discontinued operat", "terminated site"),
    ("divestiture", "amendment"),
    ("divest", "amend"),
    ("litigation", "deviation"),
    ("settlement", "resolution"),
    ("natural disaster", "site closure"),
    ("severance", "titration"),
    ("sever", "titrat"),
    ("is-01-face-lines", "ae-01-event-counts"),
    ("is-02-routing", "ae-02-routing"),
    ("is-03-net-sales", "ae-03-enrollment"),
    ("is-03-impairments", "ae-03-withdrawals"),
    ("is-03-exceptionals", "ae-03-serious-events"),
    ("is-03-cogs-sgna", "ae-03-demographics"),
    ("is-04-reconciliation", "ae-04-crosstab"),
    ("Income Statement", "Adverse Event Table"),
    ("income statement", "adverse event table"),
    ("Net Sales", "Enrollment"),
    ("net sales", "enrollment"),
    ("Operating income", "Total reported events"),
    ("operating income", "total reported events"),
    ("Cost of Goods Sold", "Mild Events"),
    ("Selling, General", "Moderate Events"),
    ("accru", "tabulat"),
    ("charge", "record"),
    ("expense", "instance"),
    ("million", "participant"),
    ("billion", "screening"),
    ("USD, in millions", "participants, by cohort"),
]


def swap_text(text):
    for old, new in SENTENCE_SWAPS:
        text = text.replace(old, new)
    for old, new in WORD_SWAPS:
        text = text.replace(old, new)
    return text


out_lines = []
for line in open(SRC, encoding="utf-8"):
    if not line.strip():
        continue
    rec = json.loads(line)
    if rec.get("type") == "envelope":
        rec["skills"] = ["skills:adverse-event-summary"]
    elif rec.get("type") == "tool_call":
        rec["inputTruncated"] = swap_text(rec.get("inputTruncated") or "")
    elif rec.get("type") == "message":
        inner = rec.get("message") or {}
        payload = inner.get("message") if isinstance(inner, dict) else None
        content = payload.get("content") if isinstance(payload, dict) else None
        if isinstance(content, list):
            for block in content:
                if not isinstance(block, dict):
                    continue
                if block.get("type") == "text":
                    text = block.get("text") or ""
                    if text.startswith("Base directory for this skill"):
                        block["text"] = NEUTRAL_SKILL_TEXT
                    elif text.startswith("Checklist:"):
                        block["text"] = NEUTRAL_CHECKLIST
                    else:
                        block["text"] = swap_text(text)
                elif block.get("type") == "thinking":
                    block["thinking"] = "redacted for fixture"
                elif block.get("type") == "tool_use":
                    block["input"] = json.loads(swap_text(json.dumps(block.get("input", {}))))
                elif block.get("type") == "tool_result":
                    block["content"] = json.loads(swap_text(json.dumps(block.get("content", ""))))
    out_lines.append(json.dumps(rec, separators=(",", ":")))

open(DST, "w", encoding="utf-8").write("\n".join(out_lines) + "\n")
print(f"wrote {DST}: {len(out_lines)} records")
