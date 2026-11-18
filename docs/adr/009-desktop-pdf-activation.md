# Register Plico as an alternate PDF handler

Windows setup advertises a per-user PDF ProgID, the macOS app advertises the PDF type with the Alternate rank, and the Ubuntu desktop entry declares the PDF MIME type. A launch carries local PDF paths to the existing import workflow. An already running Windows or Linux instance can receive them through a length-bounded, current-user-only pipe. macOS uses Avalonia file-activation events. Plico queues incoming paths until its window and library are ready and imports them under the same file lease used by ordinary intake.

Theme and interface language are desktop preferences stored outside the portable library. Avalonia theme dictionaries supply the light and dark palette. The desktop text catalog supplies English and French labels, with language changes applied at launch. OCR language remains a separate selection.
