# Phase 0 — the foundation, as it was proved

Every picture in this folder was taken by a harness in `tools/` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application to draw one of
its own windows, and the application writes the picture itself. This matters: a picture taken from
outside the process comes back mirrored for a right-to-left window, and the mirrored version of an
Arabic screen is a lie about the screen. The pictures are what the owner sees.

| Picture | Harness | What it shows |
|---|---|---|
| `phase0-01-wizard-step1.png` | `smoke-flow.ps1` | The first-run wizard's first step: the shop's name and what it will be used for |
| `phase0-02-wizard-step2.png` | `smoke-flow.ps1` | The wizard's second step, with the administrator's name and password |
| `phase0-03-wizard-step3.png` | `smoke-flow.ps1` | The wizard's last step, and the button that finishes the setup |
| `phase0-04-login.png` | `smoke-flow.ps1` | The login screen that comes after the setup |
| `phase0-05-shell.png` | `smoke-flow.ps1` | The shell of a shop that has been set up |
| `phase0-05-shell-window.png` | `smoke-flow.ps1` | The same shell, drawn as a window |
| `phase0-shell.png` | `smoke-shell.ps1` | The shell of an already-configured shop, signed into |
| `phase0-firstrun.png` | `smoke-launch.ps1` | The very first window a brand-new shop sees |

These harnesses used to write into throwaway session folders that belong to one work session and
are gone with it. They now write here, beside the phase folders the other harnesses use, so that
every picture the repository leans on is in the repository.

> **The four harnesses that write here have not been run since the application began drawing its
> own windows.** They are older than that change: they look for the wizard and the sign-in window
> by their English titles, and the application starts in Arabic, and they photograph a window from
> outside the process, which a locked desktop cannot do. The destination has been fixed; the
> harnesses themselves still have to be brought up to date. The [Phase 9
> report](../../PHASE-9-V1.0.1.md) records this as open work.
>
> The pictures the [Phase 0 report](../../PHASE-0-FOUNDATION.md) leans on were taken by those
> harnesses when they still worked, and were left in a session folder that is gone with that
> session. The folder is here so that the next run puts them back where the report can point at
> them.
