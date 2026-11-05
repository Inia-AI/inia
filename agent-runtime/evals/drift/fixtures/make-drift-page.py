# Generates drift-page.pdf, the one-page document the pdf-read drift prompt stages.
# Run through the toolbelt so the PDF is reproducible byte for byte (reportlab's
# invariant mode fixes the creation date and document id):
#   docker run --rm -v "$PWD/evals/drift/fixtures:/f" --entrypoint /opt/venv/bin/python \
#     agent-runtime:dev /f/make-drift-page.py
from pathlib import Path

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

OUT = Path(__file__).with_name("drift-page.pdf")
NUMBER = "583274"

c = canvas.Canvas(str(OUT), pagesize=A4, invariant=1, pageCompression=0)
width, height = A4
c.setTitle("SDK drift fixture")
c.setFont("Helvetica", 14)
c.drawString(72, height - 90, "SDK drift fixture page (agent-runtime evals/drift).")
c.drawString(72, height - 112, "This page carries exactly one six-digit number, printed below.")
c.setFont("Helvetica-Bold", 20)
c.drawString(72, height - 180, "The number printed on this page is:")
c.setFont("Helvetica-Bold", 72)
c.drawString(72, height - 280, NUMBER)
c.setFont("Helvetica", 12)
c.drawString(72, 72, "Generated with reportlab from the toolbelt; no third-party content.")
c.showPage()
c.save()
print("wrote", OUT)
