# Phase 0 — the foundation, as it was proved

Every picture in this folder was taken by a harness in `tools/` while it drove the real
application. Nothing here is a mock-up and nothing was drawn by hand.

**The application draws its own windows.** The script asks the running application to draw one of
its own windows over the capture pipe, and the application writes the picture itself. This
matters: a picture taken from outside the process comes back mirrored for a right-to-left window,
and the mirrored version of an Arabic screen is a lie about the screen. The pictures here are
what the shopkeeper sees.

| Picture | Harness | What it shows |
|---|---|---|
| `phase0-firstrun.png` | `smoke-launch.ps1` | The very first window a brand-new shop sees: the first-run wizard, in Arabic, before anything has been written |
| `phase0-01-wizard-step1.png` | `smoke-flow.ps1` | The wizard's first step: the shop's name, its address and its telephone |
| `phase0-02-wizard-step2.png` | `smoke-flow.ps1` | The wizard's second step, with the owner's name and password |
| `phase0-03-wizard-step3.png` | `smoke-flow.ps1` | The wizard's last step, and the button that finishes the setup |
| `phase0-04-login.png` | `smoke-flow.ps1` | The sign-in screen that comes after the setup |
| `phase0-05-shell.png` | `smoke-flow.ps1` | The shell of a shop that has just been set up |
| `phase0-login.png` | `smoke-login.ps1` | The sign-in window of a shop that has been set up and closed again |
| `phase0-shell.png` | `smoke-shell.ps1` | The shell of a shop that has been set up, signed into and left running |
| `phase0-05-shell-window.png` | `smoke-shell.ps1` | The same shell, drawn as a window of its own |

These harnesses used to write into throwaway session folders that belong to one work session and
are gone with it, and they had fallen behind the programme: they looked for the wizard and the
sign-in window by their English titles while the application starts in Arabic, and they
photographed a window from outside the process, which a locked desktop cannot do. They now share
the helpers in [`tools/lib/storeflow-ui.ps1`](../tools/lib/storeflow-ui.ps1) with the rest of the
harnesses, and they write here, beside the phase folders the other harnesses use.
