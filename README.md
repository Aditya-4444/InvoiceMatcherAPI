# Invoice Matcher

Background worker and API that extract fields from invoice PDFs and match them to open purchase orders.

Prerequisite: .NET 8 SDK. Runs on Windows, macOS and Linux with no other infrastructure.

Run: `cd InvoiceMatcher`, then `dotnet run`. The API listens on http://localhost:5016 and Swagger is at `/swagger` in Development.
Startup applies migrations, imports `data/pos.csv`, and queues every PDF already in `data/inbox`. Paths and retry settings are in `appsettings.json`.
Tests: `dotnet test InvoiceMatcher.Tests` from the repository root.

Happy path: copy `data/demo/inv-001.pdf`, `inv-002.pdf`, `inv-003.pdf` into `data/inbox`, then open `/invoices?status=matched`.
Edge cases: copy `inv-005.pdf` (two open POs match) and `inv-006.pdf` (unreadable), then open `/invoices?status=exception`.
`inv-004.pdf` matches by vendor and amount, and `inv-007.pdf` references a PO that does not exist.

API: `GET /invoices?status=&page=&pageSize=` (status is pending, matched, unmatched or exception) and `GET /health` (queue depth, last successful run, database reachability). Both appear in Swagger.

Idempotency: each file is hashed with SHA-256 and the hash has a unique index. A file with known content is skipped and logged, whatever its name, so a restart or re-drop never creates a second record.
Retries: an unexpected error is retried up to 3 attempts in total with a growing delay, and the attempt count is stored. After the last failure the invoice becomes an exception that holds the last error message.
Ambiguous, unmatched and unreadable invoices are recorded straight away and are not retried.
After processing, files move out of `data/inbox`: finished files (any result) to `data/archive`, and files that fail every retry to `data/failed`. `SourceFileRef` is the file name in that folder.
