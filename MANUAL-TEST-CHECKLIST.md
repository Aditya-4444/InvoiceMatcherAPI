Start the app with `cd InvoiceMatcher` and `dotnet run`, with `data/inbox` empty. Replace `5016` if you changed the port.

1. `GET /health` returns 200 with status `Healthy`, `queueDepth` 0, `databaseReachable` true, and `lastSuccessfulRun` "none yet".
3. Copy `data/demo/inv-001.pdf`, `inv-002.pdf`, `inv-003.pdf` into `data/inbox`. `GET /invoices?status=matched` returns 3 items with matchedPONumber PO-1001, PO-1005, PO-1006, each with its sourceFileRef and attemptCount 1.
4. Copy `inv-004.pdf`. `GET /invoices?status=matched` now returns 4 items, and the new one has matchedPONumber PO-1007 with reason "Matched on Vendor+Amount".
5. Copy `inv-005.pdf`. `GET /invoices?status=exception` returns an item with reason "Ambiguous: 2 candidate POs (PO-1002, PO-1003) matched on Vendor+Amount" and matchedPONumber null.
6. Copy `inv-006.pdf`. `GET /invoices?status=exception` also returns an item with reason "Extraction returned no usable fields", sourceFileRef `inv-006.pdf`, and attemptCount 1.
7. Copy `inv-007.pdf`. `GET /invoices?status=unmatched` returns one item with reason "PO PO-9999 was not found".
8. Copy `inv-001.pdf` again under another name, such as `inv-001-copy.pdf`. `GET /invoices` still reports total 7, and the log shows "Skipping inv-001-copy.pdf".
9. Stop and restart the app. `GET /invoices` still reports total 7, and nothing is reprocessed.
10. `GET /invoices?status=bogus` returns 400.
11. `GET /health` returns 200 and `lastSuccessfulRun` now shows a timestamp.
