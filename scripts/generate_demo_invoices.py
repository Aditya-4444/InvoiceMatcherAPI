import sys
from pathlib import Path

INVOICES = {
    "inv-001": ["Vendor: Northwind Traders", "Amount: 125.50", "InvoiceDate: 2025-01-10", "PONumber: PO-1001", "InvoiceNumber: INV-001"],
    "inv-002": ["Vendor: Woodgrove Bank", "Amount: 950.00", "InvoiceDate: 2025-01-12", "PONumber: PO-1005", "InvoiceNumber: INV-002"],
    "inv-003": ["Vendor: Adventure Works", "Amount: 1200.00", "InvoiceDate: 2025-01-15", "PONumber: PO-1006", "InvoiceNumber: INV-003"],
    "inv-004": ["Vendor: Tailspin Toys", "Amount: 88.25", "InvoiceDate: 2025-01-18", "InvoiceNumber: INV-004"],
    "inv-005": ["Vendor: Contoso Ltd", "Amount: 500.00", "InvoiceDate: 2025-01-20", "InvoiceNumber: INV-005"],
    "inv-007": ["Vendor: Fabrikam", "Amount: 300.00", "InvoiceDate: 2025-01-25", "PONumber: PO-9999", "InvoiceNumber: INV-007"],
}

UNREADABLE = b"\x00\x01\x02 this is not a pdf \xfe\xff"


def escape(text):
    return text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def build_pdf(lines):
    text = " T* ".join(f"({escape(line)}) Tj" for line in lines)
    content = f"BT /F1 12 Tf 14 TL 72 720 Td {text} ET".encode("latin-1")
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 4 0 R >> >> >>",
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        b"<< /Length %d >>\nstream\n" % len(content) + content + b"\nendstream",
    ]

    pdf = bytearray(b"%PDF-1.4\n")
    offsets = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf += b"%d 0 obj\n" % number + body + b"\nendobj\n"

    xref = len(pdf)
    pdf += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
    for offset in offsets:
        pdf += b"%010d 00000 n \n" % offset
    pdf += b"trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objects) + 1, xref)
    return bytes(pdf)


def main(target):
    target.mkdir(parents=True, exist_ok=True)
    for name, lines in INVOICES.items():
        (target / f"{name}.pdf").write_bytes(build_pdf(lines))
    (target / "inv-006.pdf").write_bytes(UNREADABLE)


if __name__ == "__main__":
    main(Path(sys.argv[1] if len(sys.argv) > 1 else "data/demo"))
