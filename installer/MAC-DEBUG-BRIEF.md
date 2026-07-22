# Brief for the macOS Claude — debug the 1.0.118 package SIGKILL

## Context (what changed since you last fixed this)
The Windows side now builds the macOS package with a script
(`build-mac-package.ps1`) that signs every Mach-O file with **rcodesign
(apple-codesign 0.29.0) ad-hoc, from Windows** — NOT with macOS's native
`codesign`. The package is a flat layout (no `.app` bundle) and contains
**no Info.plist anywhere** (verified by the build). rcodesign reported
`CodeSignatureFlags(ADHOC)`, identifier `LensHH`, Mach-O thin arm64.

So the open question is new: **is an rcodesign-produced ad-hoc signature,
cross-signed from Windows, actually accepted by amfid/AMFI on exec?**
That has never been validated at runtime. Everything you root-caused
before (bundle-sig -423, sticky syspolicyd deny records) may also still
be in play.

## Please determine, in order

1. **Confirm it's a SIGKILL, not a normal exit:**
   ```
   cd app
   ./LensHH.App; echo "rc=$?"
   ```
   (137 = SIGKILL from AMFI/syspolicy; anything else changes the story.)

2. **Is our Windows signature even valid on macOS?**
   ```
   codesign -dv --verbose=4 ./LensHH.App
   codesign --verify --strict --verbose=2 ./LensHH.App
   ```
   - Format must be `Mach-O thin (arm64)`, flags should show `adhoc`.
   - Does `--verify --strict` PASS or FAIL? This is the key result.

3. **Gatekeeper / quarantine state:**
   ```
   xattr ./LensHH.App
   spctl -a -vvv -t exec ./LensHH.App
   ```

4. **The actual kill reason from the log** (run, then relaunch in another
   tab, then read):
   ```
   log show --last 2m --predicate 'sender == "AppleSystemPolicy" OR sender == "amfid" OR eventMessage CONTAINS "LensHH.App"' --info --debug
   ```
   Look for `Error -423`, `adhoc`, `unknown certificate chain`, or
   `Security policy would not allow process`.

5. **THE ISOLATING EXPERIMENT — does a LOCAL re-sign fix it?**
   Re-sign the same bytes with macOS's own `codesign`, then relaunch:
   ```
   xattr -cr .
   codesign --force -s - ./LensHH.App
   ./LensHH.App; echo "rc=$?"
   ```
   - If it now RUNS → the **rcodesign-from-Windows ad-hoc signature is the
     problem**; the Windows build must produce a signature macOS accepts
     (or we sign on the Mac). This is the most likely new cause.
   - If it STILL gets killed → not the signature. Likely a **sticky
     syspolicyd deny record on this path** (did this folder reuse a path
     that was denied during the earlier test build?). Try the fresh-file
     trick on a brand-new path:
     ```
     cd .. && cat app/LensHH.App > app/LensHH.App.fresh \
       && mv app/LensHH.App.fresh app/LensHH.App \
       && chmod +x app/LensHH.App && codesign --force -s - app/LensHH.App \
       && ./app/LensHH.App; echo "rc=$?"
     ```

6. **Where was it unzipped?** Same path as the earlier `*-118-test`
   folder, or a fresh one? (Sticky deny records are per-path.)

## What to report back
- The `rc=` from steps 1 and 5 (the two launches).
- PASS/FAIL of `codesign --verify --strict` (step 2).
- The decisive log line from step 4.
- Whether the local re-sign (step 5) changed the outcome.

That tells us whether to fix the Windows signing, sign on the Mac CI
instead, or just handle the sticky-deny path issue.
