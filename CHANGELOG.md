# Changelog

All notable changes to ProgramDock are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project uses semantic versioning.

## [1.0.0] - 2026-09-25

### Added

- Windows x64 self-contained and framework-dependent builds.
- Fixed-disk and custom-directory program scanning.
- Installed-program discovery through the Windows uninstall registry.
- Shortcut-based Direct Use classification using the user's Desktop and Documents shortcut folder.
- Search, favorites, recent programs, hidden programs, tray mode, global hotkey, and manual category overrides.
- High-density eight-column paged launcher grid.
- Debounced background search for large local catalogs.
- Native Settings and release documentation.
- Local data migration from the pre-1.0 development profile.

### Security and privacy

- Runs as a standard user process without requesting administrator privileges.
- Executes a program only after an explicit user action.
- Provides a confirmation step for script and batch-file launches.
- Keeps the local catalog and settings on the user's machine.
