# Publishing MatterDesk to GitHub

The repository has no remote yet, and the commits carry a placeholder author. These are the exact steps
to put it on github.com under your own account and attach the CLI binaries to a release.

## 1. Create the repository

On github.com: **New repository** → name `matterdesk`, **Public**, no README / .gitignore / licence
(the repo already has them). Or with the CLI: `gh repo create matterdesk --public --source=. --remote=origin --push`
(then skip to step 4).

## 2. Add the remote

```bash
cd matterdesk
git remote add origin https://github.com/<user>/matterdesk.git
```

## 3. Fix the commit author

Every commit is authored as `Luis Escobar <luis@example.com>`. Rewrite them to the identity GitHub knows
(the email must be one verified on your account for the commits to count as yours):

```bash
git -c user.name="Luis Escobar" -c user.email="<your-github-email>" \
  rebase -r --root --exec 'git commit --amend --no-edit --reset-author'
```

Equivalent, if you prefer `git filter-repo` (`pip install git-filter-repo`):

```bash
git filter-repo --force --email-callback 'return b"<your-github-email>"' \
                         --name-callback  'return b"Luis Escobar"'
git remote add origin https://github.com/<user>/matterdesk.git   # filter-repo removes remotes
```

Check with `git log --format='%h %an <%ae> %s'` before pushing.

## 4. Push

```bash
git push -u origin main
```

## 5. Create the release and attach the CLI zips

Build the three single-file executables and zip each one (the zips are not committed; `*.zip` is ignored):

```bash
for rid in win-x64 linux-x64 osx-arm64; do
  dotnet publish src/MatterDesk.Cli -c Release -r $rid -o /tmp/cli/$rid
done
(cd /tmp/cli/win-x64   && zip -q ../matterdesk-cli-win-x64.zip   matterdesk.exe)
(cd /tmp/cli/linux-x64 && zip -q ../matterdesk-cli-linux-x64.zip matterdesk)
(cd /tmp/cli/osx-arm64 && zip -q ../matterdesk-cli-osx-arm64.zip matterdesk)
```

Then on github.com: **Releases → Draft a new release**, tag `v0.1.0` (create it on `main`), title
`MatterDesk 0.1.0`, attach the three zips, publish. Or:

```bash
git tag v0.1.0 && git push origin v0.1.0
gh release create v0.1.0 /tmp/cli/matterdesk-cli-*.zip --title "MatterDesk 0.1.0" \
  --notes "Local client: matterdesk demo | matters | search | documents | tools | call | mcp (stdio MCP bridge)."
```

## 6. Point the installers at the release

`connectors/install.sh` and `connectors/install.ps1` default to
`https://github.com/<your-user>/matterdesk/releases/latest/download/`, which resolves to the assets of the
latest release. Replace `<your-user>` in both scripts (and in `connectors/README.md` if you mention the URL)
with your GitHub user name, commit, and push. Anyone can then run:

```bash
curl -fsSL https://raw.githubusercontent.com/<user>/matterdesk/main/connectors/install.sh | bash
```

```powershell
irm https://raw.githubusercontent.com/<user>/matterdesk/main/connectors/install.ps1 | iex
```

Users who already have a zip can bypass the release entirely with
`MATTERDESK_RELEASE_URL=https://<any-host>/<folder>/` pointing at a directory that holds the zips.
