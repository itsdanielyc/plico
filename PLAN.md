# Plico implementation plan

| # | Slice | Done when | Keel levels | Status |
| --- | --- | --- | --- | --- |
| 0 | Avalonia scaffold | Build and native window smoke pass on all three runners | L0, L4, L6 | Done |
| 1 | Local PDF inbox | Digital and image imports yield plain searchable PDFs, with catalog and error recovery tests | L1–L4, L6 | Done |
| 2 | Review and retrieval | Tags, reviewed state and text search survive restart and a copied library; visible controls pass headless and native journeys | L1–L6 | Done |
| 3 | Scan into the inbox | WIA, SANE and eSCL adapters share the import path; synthetic capture and unavailable-driver paths exercised | L3, L4, L6 | Done |
| 4 | v0.1.0 | All three released archives pass the native journey from clean extraction on the exact tagged commit | L7 | In verification |
| 5 | Faster intake | Add drag-and-drop and improve handling of repeated imports, with a measured workflow check | L1, L3, L4, L6 | Planned |

The initial release is followed by focused improvements and fresh evidence.
