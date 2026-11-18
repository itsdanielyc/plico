# Preserve PDF versions in the library

The active version keeps the document’s ordinary path under `PDFs/`. Previous bytes live under `.plico/versions/<document-id>/` and use opaque filenames. Catalog schema v15 records the SHA-256, page count, date and operation for each entry. Restoring a version adds the displaced current PDF to history. Changing the active file updates its index and file stamp while retaining its catalog ID, metadata, tags and review state.

Page edits and replacements prepare and verify a complete PDF before writing an intent with both fingerprints. Under the library write lease and PDF read gate, Plico moves the active file to the history slot and claims the active path exclusively. SQLite commits the history row and current text together. On reopen, a verified installed file completes the transaction. If nothing occupies the active path, Plico restores the displaced bytes. If another file occupies the path, Plico preserves both files and reports their locations.

Backups copy the recorded history and verify its hashes. Trash keeps history with the document ID, and permanent purge removes its files. An encrypted original can be exported from its history. It cannot become the active searchable PDF without unlocking it.
