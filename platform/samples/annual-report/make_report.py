"""Writes annual-report.pdf, the document the sample workflows read.

Usage: python3 make_report.py

The company and every figure are invented. Each figure that expected.json names appears once, on
one page, so a citation can only point to that page. The script writes the same bytes on every run.
"""

import argparse
import pathlib

PAGE_WIDTH = 595
PAGE_HEIGHT = 842
LEFT = 72
TOP = 770
LINE = 18

PAGES = [
    [
        ("title", "Larkspur Freight Ltd"),
        ("heading", "Annual Report 2025"),
        ("text", ""),
        ("text", "Larkspur Freight Ltd is a fictional company. Inia AI wrote this report"),
        ("text", "as a sample document, and every name and figure in it is invented."),
        ("text", ""),
        ("text", "Contents"),
        ("text", "Page 2  Chair's review"),
        ("text", "Page 3  Income statement"),
        ("text", "Page 4  Balance sheet"),
        ("text", "Page 5  Our people"),
    ],
    [
        ("heading", "Chair's review"),
        ("text", ""),
        ("text", "2025 was a year of steady growth for Larkspur Freight. We opened two"),
        ("text", "new depots in the north and renewed our largest rail contract."),
        ("text", ""),
        ("text", "Revenue for the year rose to EUR 48.6 million, up from EUR 44.1 million"),
        ("text", "in 2024, as volumes on our coastal routes recovered."),
        ("text", ""),
        ("text", "We expect demand to stay firm in 2026, and we plan to add six"),
        ("text", "electric trucks to the fleet before the summer."),
    ],
    [
        ("heading", "Income statement"),
        ("text", ""),
        ("text", "For the year ended 31 December 2025"),
        ("text", ""),
        ("text", "Operating costs grew more slowly than sales, because fuel prices fell"),
        ("text", "in the second half of the year."),
        ("text", ""),
        ("text", "Net profit for 2025 was EUR 5.9 million, compared with EUR 4.7 million"),
        ("text", "for 2024. The board proposes a dividend of 12 cents per share."),
    ],
    [
        ("heading", "Balance sheet"),
        ("text", ""),
        ("text", "At 31 December 2025"),
        ("text", ""),
        ("text", "Total assets at the end of 2025 were EUR 112.4 million. Most of the"),
        ("text", "increase came from the two new depots and the vehicles bought for them."),
        ("text", ""),
        ("text", "Borrowings stayed below half of total equity throughout the year."),
    ],
    [
        ("heading", "Our people"),
        ("text", ""),
        ("text", "At the end of 2025 the company employed 1,284 people, most of them"),
        ("text", "drivers and depot staff."),
        ("text", ""),
        ("text", "Every new driver completes four weeks of training before their first"),
        ("text", "route, and staff turnover fell for the third year in a row."),
    ],
]

SIZES = {"title": 24, "heading": 18, "text": 12}


def escape(text):
    return text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def content(lines, number):
    parts = []
    y = TOP
    for style, text in lines:
        font = "F2" if style in ("title", "heading") else "F1"
        size = SIZES[style]
        if text:
            parts.append(f"BT /{font} {size} Tf {LEFT} {y} Td ({escape(text)}) Tj ET")
        y -= LINE + (size - 12)
    parts.append(f"BT /F1 10 Tf {PAGE_WIDTH // 2 - 10} 40 Td (Page {number}) Tj ET")
    return "\n".join(parts).encode("latin-1")


def build():
    objects = []

    def add(body):
        objects.append(body)
        return len(objects)

    catalog = add(None)
    pages = add(None)
    regular = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
    bold = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>")
    kids = []
    for number, lines in enumerate(PAGES, start=1):
        stream = content(lines, number)
        stream_id = add(b"<< /Length %d >>\nstream\n" % len(stream) + stream + b"\nendstream")
        kids.append(add(
            b"<< /Type /Page /Parent %d 0 R /MediaBox [0 0 %d %d] /Resources << /Font << /F1 %d 0 R /F2 %d 0 R >> >> /Contents %d 0 R >>"
            % (pages, PAGE_WIDTH, PAGE_HEIGHT, regular, bold, stream_id)))
    objects[catalog - 1] = b"<< /Type /Catalog /Pages %d 0 R >>" % pages
    objects[pages - 1] = b"<< /Type /Pages /Kids [%s] /Count %d >>" % (
        b" ".join(b"%d 0 R" % kid for kid in kids), len(kids))

    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for index, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += b"%d 0 obj\n" % index + body + b"\nendobj\n"
    xref = len(out)
    out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
    for offset in offsets:
        out += b"%010d 00000 n \n" % offset
    out += b"trailer\n<< /Size %d /Root %d 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objects) + 1, catalog, xref)
    return bytes(out)


if __name__ == "__main__":
    argparse.ArgumentParser(description=__doc__.split("\n\n")[0]).parse_args()
    target = pathlib.Path(__file__).with_name("annual-report.pdf")
    target.write_bytes(build())
    print(f"Wrote {target}")
