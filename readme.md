# LaunchFast

A native macOS GUI for setting up, running, and deploying [fastlane](https://fastlane.tools)
lanes for Flutter apps. LaunchFast removes the command-line friction of fastlane —
`bundle exec fastlane …`, sourcing env files, remembering lane names, match, secrets —
and turns building and shipping iOS + Android apps into a point-and-click flow.

Built with Avalonia on .NET 10.

## Features

- **Launcher grid** — Android-Studio-style projects/recents grid; opens any Flutter root
  and detects its iOS/Android fastlane setups (and launcher icons).
- **Lane runner** — runs lanes with **live streaming output** and supports interactive
  prompts (2FA, passphrases) via the process backend.
- **Keychain secrets** — per-project secrets stored in the macOS login Keychain;
  resolved into the lane environment at run time.
- **Per-lane store version** — surfaces the current App Store Connect / Google Play
  version for release lanes, refreshed on demand and degrading gracefully when offline
  or uncredentialed.

## Build / run / test

```sh
# Build the app and unit/UI tests
dotnet build LaunchFast.slnx

# Run the unit + headless UI test suites
dotnet test LaunchFast.slnx

# Launch the app
dotnet run --project src/LaunchFast.App

# Integration smoke suite (owner's Mac only — drives real fastlane / Keychain).
# These Assert.Ignore when prerequisites are missing; they are NOT run in CI.
dotnet test IntegrationTests/IntegrationTests.slnx
```

The SDK version is pinned via `global.json`; packages use Central Package Management.

## Roadmap

This repo builds sub-project #1 first:

1. **Launcher + detect + run existing lanes** ← current.
2. Lane scaffolding for projects with no fastlane (generate Fastfiles / Matchfile / env).
3. Deeper match / code-signing management.
4. Release-to-prod checklist.
5. Richer multi-project organisation.

## More

- [`claude.md`](claude.md) — goals, architecture, and conventions (single source of truth).
- [`PROGRESS.md`](PROGRESS.md) — the live phase-by-phase checklist.

## License

[MIT](license.txt) — © 2026 JABTech (Brandt Vavasour).
