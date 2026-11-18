# ADR 005 • Release candidate verification

Accepted 2026-11-18

Each candidate runs locked restore, solution tests, and both portable and installed launch checks in one job per supported runtime. The archive workflow uploads the packages after the checks pass. GitHub release publishing finds a successful archive run for the exact tagged commit, checks the version and package names, and publishes the downloaded artifacts with checksums.

The v0.15.0 candidate took 578 seconds of accumulated job time in the source matrix and 873 seconds in the archive matrix. The source matrix spent 214 seconds in solution tests. Combining the matrices shares checkout, SDK setup, and locked restore. The candidate’s portable and installed apps exercise the complete native journey. [Source matrix](https://github.com/itsdanielyc/plico/actions/runs/44228219708) · [Archive matrix](https://github.com/itsdanielyc/plico/actions/runs/44228219765)

Pushes to main and pull requests use the source CI workflow. After publishing, fresh runners download the released packages and repeat the checksum, portable, and installed checks.
