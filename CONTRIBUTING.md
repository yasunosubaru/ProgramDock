# Contributing to ProgramDock

Thanks for helping improve ProgramDock.

## Development setup

1. Install the .NET 8 SDK on Windows 10/11.
2. Clone the repository.
3. Restore and build:

```powershell
dotnet restore .\ProgramDock.sln
dotnet build .\ProgramDock.sln -c Release -p:Platform=x64
```

## Pull requests

- Keep changes focused and explain the user-facing behavior.
- Do not commit `bin/`, `obj/`, `artifacts/`, local settings, or personal catalog files.
- Add or update tests when a bug can be reproduced outside the WPF UI.
- Run the Release build before requesting review.
- Update `CHANGELOG.md` for user-visible changes.

## Reporting bugs

Please include the ProgramDock version, Windows version, scan configuration, reproduction steps, and relevant sanitized logs. Do not include private program paths or personal data in public reports.

By contributing, you agree that your contributions are licensed under the repository's MIT License.
