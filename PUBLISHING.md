# Publishing PWFAuth to nuget.org

The package is built and verified — `artifacts/PWFAuth.1.0.0-preview.1.nupkg` plus its
symbols package. The remaining steps need your nuget.org identity, so they are yours to
run.

> **Using Trusted Publishing instead of an API key? Read `TRUSTED-PUBLISHING.md`.**
> It is the better option — no long-lived secret exists at all — and it replaces
> sections 1 and 2 below. The rest of this file (verification, repo, release
> checklist) still applies.

## Before you push — the one irreversible part

**A published version can never be deleted.** nuget.org only offers *unlist*, which hides
a version from search while `dotnet add package PWFAuth --version 1.0.0` still installs
it. That is why this first build is `1.0.0-preview.1`: prereleases are opt-in
(`dotnet add package PWFAuth --prerelease`), so a mistake here does not become the
default install for everyone. Ship `1.0.0` only once the API feels settled.

The package id `PWFAuth` was free on 2026-08-02. Ids are first-come, and once taken they
are gone — so claiming it is itself a reason not to wait too long.

## 1. Account and API key

1. Sign in at <https://www.nuget.org> with a Microsoft account.
2. Go to **API Keys → Create**.
3. Name it something like `pwfauth-publish`.
4. Scope it to **Push new packages and package versions**.
5. Glob pattern: `PWFAuth*` — this limits the damage if the key leaks.
6. Expiry: 365 days maximum. Put a reminder in your calendar.
7. Copy the key once; nuget.org never shows it again.

Keep the key out of any repository. Set it as an environment variable when you push:

```bash
setx NUGET_API_KEY "your-key-here"
```

## 2. Push

From the repository root:

```bash
dotnet nuget push artifacts/PWFAuth.1.0.0-preview.1.nupkg --api-key %NUGET_API_KEY% --source https://api.nuget.org/v3/index.json
```

The `.snupkg` symbols package is picked up automatically alongside it — do not push it
separately. Indexing takes a few minutes; the listing appears at
<https://www.nuget.org/packages/PWFAuth>.

## 3. Verify it from a clean machine

```bash
dotnet new console -o PwfSmoke && cd PwfSmoke
dotnet add package PWFAuth --prerelease
```

If that restores from nuget.org (not from a local folder), the release is real.

## 4. Publish the source repository

`RepositoryUrl` in the csproj points at `https://github.com/pwfauth/pwfauth-dotnet`, which
does not exist yet. Create it under the existing `pwfauth` organisation and push this
folder. Source Link is enabled, so once the repo is public, customers debugging into the
package step straight into the real source.

## Release checklist for future versions

1. Bump `<Version>` in `src/PWFAuth/PWFAuth.csproj`, and the `PWFAuth` package version
   both sample projects reference.
2. Update `<PackageReleaseNotes>` — this is what shows on the nuget.org version list, and
   it is the only channel through which existing users learn a security fix exists — and
   add the same entry to `CHANGELOG.md`.
3. `dotnet test tests/PWFAuth.Tests -c Release` — unit tests against a fake server, no
   network needed. The publish workflow runs them too and stops before packing on a failure.
4. `dotnet pack src/PWFAuth/PWFAuth.csproj -c Release -o ./artifacts`
5. Run both sample projects — `samples/QuickTest` (net8.0) and `samples/VbFrameworkTest`
   (VB.NET on .NET Framework 4.8.1). They install the package from `./artifacts`, so they
   exercise the real package, not the project reference.
6. Push.

Follow semantic versioning strictly. Consumers with `<PackageReference Version="1.*">`
take minor updates automatically; a breaking change in a minor version breaks builds in
the field.

## Why this matters more than it looks

Every developer who copy-pasted the old generated client still runs whatever bugs it had.
There is no channel to reach them — no version number, no notification, nothing. The
firewall-bypass fix from 2026-08-02 is a good example: it closed a real hole, and it can
never reach a single copy-pasted file.

A package changes that. `dotnet list package --outdated` surfaces it, one command applies
it, and the release notes explain it. That update channel is the product feature here, not
the convenience of `dotnet add package`.
