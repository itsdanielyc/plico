# ADR 008 · Structured metadata and local models

Accepted 2026-11-18

Document assignments use the existing catalog identity. Correspondent names and aliases share a unique normalized name index. Custom field definitions carry their type and choices. Values carry a typed JSON payload, with a foreign key for document links. Tag appearance uses the existing globally unique tag keys for hierarchy and colour. Sidebar parent selection includes descendants and literal search tag clauses continue to match their assigned tag.

The migration import seam binds a stable source record key to a Plico identity. It journals the intended path and published fingerprint before claiming a filename, then commits provenance and metadata with the catalog row. Recovery hashes the already published file before applying metadata. Existing file writers continue to use the library's exclusive file-operation lease. Backup and portable export carry metadata and dates.

Deadline dates are calendar days with optional lead times. The desktop counts active alarms while it is open. iCalendar exports carry stable event identifiers and display alarms for a calendar application. [iCalendar standard](https://www.rfc-editor.org/rfc/rfc5545)

Word-frequency similarity is available from the existing search index. Semantic search caches a bounded set of normalized chunk vectors per document and local model configuration. The cache key and text fingerprint invalidate changed inputs, and purge cascades through the catalog identity.

Opt-in generation and embeddings use an OpenAI-compatible loopback connection. Redirects and HTTP proxy routing are disabled. Filing proposals pass the same catalog validators as manual edits and are applied only after review. An undo receipt checks the resulting catalog state before restoring the prior values. Chat context is scoped to selected or retrieved indexed excerpts and exposes their source documents. [Ollama OpenAI compatibility](https://docs.ollama.com/api/openai-compatibility)
