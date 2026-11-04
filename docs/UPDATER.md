# Auto-update — how the programme updates itself, and how a release is made

StoreFlow checks **GitHub Releases** for a newer version of itself. The update is always the
shopkeeper's decision: the programme only *notifies*, and the shopkeeper presses **Update now**.
Nothing is ever installed silently.

> The repository has to stay **public**. The check is anonymous — the programme sends no GitHub
> credentials — and a private repository answers `404` to every anonymous client. The banner would
> then never appear and no installer could be downloaded. The repository this programme is
> published from is public.

---

## 1. What the shopkeeper sees

| Situation | What happens |
| --- | --- |
| A newer release is published | A banner appears under the command bar with the version, and two buttons: **Update now** and **Later**. |
| **Update now** | The installer is downloaded, checked against the SHA-256 published with it, and started. The programme closes so the installer can replace its files. |
| **Later** | The banner goes away. The shop can ask again from **Settings → About this programme**, at any time. |
| No newer release | Nothing appears at all. The check is quiet. |
| The shop is offline, or the release feed cannot be reached | Nothing appears. The programme never shows an error the shopkeeper cannot act on, and never waits for the internet. |

The check runs **in the background** once the shell is open. The programme does not wait for it.

---

## 2. How a release is made

The release is made by **pushing a tag**. The workflow
(`.github/workflows/release.yml`) does the rest, and the programme finds it by itself.

```powershell
# 1. Bump the version in src\StoreFlow.Desktop\StoreFlow.Desktop.csproj
#    <Version>1.0.1</Version>

# 2. Commit and tag
git add -A
git commit -m "release: 1.0.1"
git tag v1.0.1
git push origin main --tags
```

The tag must be lowercase `v` and the version. The workflow refuses to build a tag that does not
match the version in the project, because a release built from the wrong version is a release the
programme cannot compare itself against.

The workflow then:

1. restores and builds the solution in Release,
2. **runs the whole test suite** — the job fails, and nothing is published, if any test fails,
3. checks that the tag matches the version in the project,
4. builds the installer with `installer\build-installer.ps1`,
5. computes the installer's SHA-256 and writes it beside the installer as
   `StoreFlow-<version>-x64.msi.sha256`,
6. publishes a **published** (not draft) GitHub release called `StoreFlow Desktop v<version>`,
   carrying the installer and its checksum.

Every shop's copy notices the new release the next time it starts.

### The release must be published, not drafted

The programme reads the **latest published** release. A draft release is invisible to it, so a
release left as a draft never reaches any shop. This is different from the sibling project
`ua-windows-invoices`, where the workflow has to write a manifest into the draft and the draft is
published by hand afterwards; here the workflow publishes the release itself, so there is no second
step to forget.

### The assets are named the way the programme expects

- `StoreFlow-<version>-x64.msi` — the installer;
- `StoreFlow-<version>-x64.msi.sha256` — the hash of that installer, in the usual
  `Get-FileHash` format.

---

## 3. What the update is protected by

The installer is **not** signed with a Windows Authenticode certificate, so Windows SmartScreen
warns on the first run. The update itself is protected by the **SHA-256 published beside the
installer**: the programme downloads both, hashes what it downloaded, and refuses to start an
installer whose hash does not match. A download that was altered or truncated is discarded and the
installed programme is left exactly as it was.

That proves the download arrived **intact**. It does not prove **who published it** — that would
need a signature whose private key lives in the repository's secrets. Writing that down here
rather than pretending the hash is more than it is.

---

## 4. Failure behaviour

| Situation | Behaviour |
| --- | --- |
| No update available | Nothing appears. The programme carries on. |
| Offline, or the feed cannot be reached | The check fails quietly. The programme carries on. Nothing is written to the shop's data. |
| The GitHub API refuses the request (rate limit) | The same as offline: no banner, no error, no waiting. |
| The release carries no installer | No update is offered, and the reason is written to the programme's own log. |
| The download does not match its checksum | The download is discarded, the installer is **not** started, and the shop is told that the update could not be installed. The installed programme is untouched. |
| The download is interrupted | The partial file is discarded. The installed programme is untouched. |
| The installer cannot be started | The download is discarded and the shop is told. The installed programme is untouched. |
| The shop's data | Never touched. The ledger, the catalogue and the backups live in `%APPDATA%\StoreFlow`, which the installer and the updater both leave alone. |

---

## 5. Checking it by hand

The live feed can be looked at without the programme:

```powershell
Invoke-RestMethod -Uri 'https://api.github.com/repos/ahmedshaban022/UA-Retail-Store--Windows/releases/latest' `
    -Headers @{ 'User-Agent' = 'StoreFlow-Desktop' } |
    Select-Object tag_name, @{n='assets';e={ ($_.assets | ForEach-Object { $_.name }) -join ', ' }}
```

The repository is public, so this answers without a token. In the programme itself, the same
answer is shown under **Settings → About this programme → Check for updates**, together with the
version that is installed.

---

## 6. The version of record

`src\StoreFlow.Desktop\StoreFlow.Desktop.csproj` holds the version:

```xml
<Version>1.0.0</Version>
```

The installer passes the version it is building into the assembly
(`-p:Version`, `-p:AssemblyVersion`, `-p:FileVersion`), so a released build reports the version it
is, and a development build reports the project's version. There is one answer, and it is the
answer the update check compares against the published release.
