# Trusted Publishing setup (nuget.org → GitHub Actions)

Trusted Publishing replaces the long-lived API key with a short-lived one that
nuget.org mints per workflow run, after verifying a signed token from GitHub. Nothing
secret is ever stored in the repository, and there is no key to rotate or leak.

## Fill the "Create" form with exactly these values

| Field | Value | Notes |
|---|---|---|
| **Policy Name** | `pwfauth-publish` | Free text; already correct. |
| **Package Owner** | `pwfauth` | Applies to **every** package owned by `pwfauth`, not just this one. |
| **CI/CD Provider** | `GitHub Actions` | Already correct. |
| **Repository Owner** | `pwfauth` | The GitHub **org** name — the part before the slash in `github.com/pwfauth/...`. |
| **Repository** | `pwfauth-dotnet` | Repo name only. Must match `RepositoryUrl` in `src/PWFAuth/PWFAuth.csproj`. |
| **Workflow File** | `publish.yml` | **File name only** — never `.github/workflows/publish.yml`. |
| **Environment** | *(leave empty)* | Only fill this if the workflow job declares `environment:`. Ours does not. |

Matching is case-insensitive.

## The workflow already exists

`.github/workflows/publish.yml` is in this repository and is what the policy above
points at. Three details in it are load-bearing:

* `permissions: id-token: write` — without it GitHub never issues the OIDC token and the
  login step fails with a permissions error.
* `uses: NuGet/login@v1` — the step that exchanges the token for a temporary key.
* The login step sits **immediately before** the push. The temporary key expires after
  **one hour**, and each token buys exactly one key, so requesting it early in a long job
  is how this breaks in practice.

## No secrets to configure

The `user:` input of the login step is your nuget.org **profile name** — not an e-mail
and not a credential. It is hard-coded to `pwfauth`, which is the account that owns the
policy, so nothing needs setting up:

```yaml
user: ${{ secrets.NUGET_USER || 'pwfauth' }}
```

If you ever publish from a different account, add a `NUGET_USER` repository secret
(**Settings → Secrets and variables → Actions**) and it takes precedence — no edit to
the workflow needed.

## Order of operations

The repository `github.com/pwfauth/pwfauth-dotnet` **does not exist yet** — the org has
`pwfauth-vbnet-examples`, `pwfauth-python-client` and `pwfauth`. Create it before the
policy can ever match:

1. Create the repo `pwfauth-dotnet` under the `pwfauth` org. **Make it public** — see the
   7-day note below.
2. Push this folder to it (`.github/workflows/publish.yml` must be on the default branch).
3. Add the `NUGET_USER` secret.
4. Create the trusted publishing policy with the values in the table.
5. Publish a GitHub Release (tag `v1.0.0-preview.1`), or run the workflow manually from
   the Actions tab.

## The 7-day activation window

A new policy can start out **temporarily active for 7 days**, mainly for private repos.
nuget.org needs GitHub's numeric repository and owner IDs to pin the policy to *this*
exact repo — that is what stops someone deleting the repo, recreating it under the same
name, and inheriting your publishing rights. Those IDs arrive with the first successful
publish, and the policy then becomes permanent.

If nothing is published within those 7 days the policy goes inactive. That is not fatal —
you can restart the window from the same screen at any time. Publishing soon after
creating the policy avoids the whole issue.

## When the policy can silently stop working

Because it is owned by an organization:

* If you are removed from the `pwfauth` org on nuget.org, the policy becomes inactive
  (and reactivates automatically if you are added back).
* If the org is locked or deleted, the policy becomes inactive.

nuget.org shows a warning in the UI in both cases — worth checking there first if a
publish suddenly fails auth.

## Verifying it worked

After the run, `https://www.nuget.org/packages/PWFAuth` should list
`1.0.0-preview.1` within a few minutes. Then, from a clean folder:

```bash
dotnet new console -o PwfSmoke && cd PwfSmoke
dotnet add package PWFAuth --prerelease
```

If that restores from nuget.org rather than a local folder, the release is real.
