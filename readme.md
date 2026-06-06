# LaunchFast

A native macOS GUI for setting up, running, and deploying [fastlane](https://fastlane.tools)
lanes for Flutter apps. LaunchFast removes the command-line friction of fastlane —
`bundle exec fastlane …`, sourcing env files, remembering lane names, match, secrets —
and turns building and shipping iOS + Android apps into a point-and-click flow.

Built with Avalonia on .NET 10.

## Features

- **Launcher grid** — Android-Studio-style projects/recents grid; opens any Flutter root
  and detects its iOS/Android fastlane setups (and launcher icons).
- **Setup wizard** — surfaces Flutter projects with no fastlane (or a missing platform)
  and generates the complete file set (Fastfile, Appfile, Matchfile, Gemfile) from scratch,
  or merges a new lane / platform block into an existing Fastfile. Five-step guided flow
  (Platforms · iOS · Android · Lanes · Review, with iOS/Android steps shown only for the
  platforms you select) with diff preview and one-click apply (writes files + stores secrets
  in Keychain + runs `bundle install`).
- **Lane runner** — runs lanes with **live streaming output**, a preflight check
  (Gemfile/bundler) before launch, one-run-at-a-time gating, and stop. Uses a pipe-based
  process backend; interactive prompts (2FA, match passphrase) are not fully supported —
  configure credential-based auth (ASC API key, `MATCH_PASSWORD`, Play service account)
  so lanes run non-interactively.
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

1. **Launcher + detect + run existing lanes** — done.
2. **Set up fastlane from scratch / add a lane** — done (the setup wizard).
3. Deeper match / code-signing management.
4. Release-to-prod checklist.
5. Richer multi-project organisation.

## More

- [`claude.md`](claude.md) — goals, architecture, and conventions (single source of truth).
- [`PROGRESS.md`](PROGRESS.md) — the live phase-by-phase checklist.

## License

[MIT](license.txt) — © 2026 JABTech.
