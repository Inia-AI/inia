# Fixture documents (gitignored)

The binary source documents for eval fixtures live here and are **never committed** —
they are copyrighted company filings and multi-MB binaries. Everything in this
directory except this README is gitignored.

To run the greenvolt fixture, place the document here yourself:

| File | Provenance |
| --- | --- |
| `greenvolt-ar-2025.pdf` | Greenvolt Group annual report 2025 (public filing). Consolidated statement of financial position on printed page 267. Values in EUR. FY_1 comparatives restated per Note 8. Expected values in `../greenvolt/expected.json` were human-verified against this page on 2026-08-01. |

The eval suite is a **regression tripwire, not an accuracy benchmark**: expected
values pin what a known-good run extracted so a prompt or model change that breaks
extraction shows up as a diff, not to score the model's accounting skill.
