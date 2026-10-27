# Explicit live verification

This executable is intentionally outside the default solution/test command. It contacts `https://api.semojum.app`, creates one test text job and one synthetic image job, and explicitly saves those jobs. It creates a second one-page image job for immediate cancellation. AI conversion consumes the account's normal page allowance. It also creates, updates, copies and deletes only its own two test forms. Obtain authorization for that account and input file before running it.

Supply `SEMOJUM_TEST_LOGIN` and `SEMOJUM_TEST_PASSWORD` through the invoking process environment. Do not put credentials in source, command files, logs, or the output directory. The executable logs only check names, statuses and test job IDs, and logs out in `finally`.

```text
dotnet run --project tests/Semojum.Live.Tests -- --authorized-live OUTPUT_DIRECTORY ENGINE_EXE SYNTHETIC_IMAGE
```

Use the **same output directory** to resume an interrupted run. `state.json` records created jobs before polling; a timeout does not create a replacement AI job. Check an existing job's status before deciding it has failed. The image fixture used on 2026-10-27 contains Korean verification text, a two-row fruit table, and an apple/pear bar chart, with no personal or business data. The final assertions expect that chart and its reference draft.

The run verifies authentication, defaults/forms CRUD, rule search, usage, board/notification reads, local engine translation, first and repeated saves, stable IDs, manual braille, memo replies/resolution, SMJ round-trips, OCR/AI, cancellation, all visual drafts, default-choice mapping, and BRF composition. It does not post messages, change account defaults, or delete existing documents. A missing public board post is recorded as a skip, not a successful detail check. Physical UI/input, operator permissions and payment remain separate verification scopes.

## Separately authorized board verification

The following mode **writes a real secret board post that operators can see**. Obtain explicit authorization for creating, editing, attaching a test TXT file and deleting that verification post; authorization for read-only or document tests does not cover it.

```text
dotnet run --project tests/Semojum.Live.Tests -- --authorized-board OUTPUT_DIRECTORY
```

Use the same credential environment variables and the same output directory when resuming. This mode verifies title/body edits, retained attachment bytes and deletion of its own recorded post. Confirm both a subsequent 404 and removal from the user's post listing. If interrupted, inspect the recorded board state and complete cleanup of that test post; never delete unrelated posts. The authorized 2026-10-27 run completed cleanup. It is historical evidence, not a new run during the Figma screenshot review.
