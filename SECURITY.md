# Security Policy

## Supported versions

Security fixes are applied to the latest `1.x` release. Older pre-release builds are not maintained.

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability before a fix is available.

Send a private report to the repository maintainer with:

- affected ProgramDock version and build type;
- Windows version and architecture;
- reproduction steps;
- impact and any relevant logs or screenshots.

Please remove credentials, personal paths, and other sensitive data from diagnostic files before sharing them.

## Security model

ProgramDock is designed to run as a standard user process. It does not request administrator privileges and does not upload the local catalog. A program is launched only after an explicit user action; script and batch-file launches require confirmation.

Users should only run releases obtained from the official repository or an official GitHub Release asset and verify the published SHA-256 checksum when possible.
