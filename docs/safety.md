# Safety Model

VideoTriage's invariant is: an original is removed only after a smaller replacement has
passed every enabled verification check and is confirmed on disk. Bookkeeping, UI state, and prior
successful files do not relax this ordering.

## Before Original Removal

For each candidate, the pipeline completes these steps in order:

1. HandBrakeCLI encodes to a temporary candidate beside the original.
2. The verifier confirms that the candidate exists and is non-empty.
3. ffprobe confirms usable video metadata and the verifier checks duration, enabled resolution
   matching, and enabled source-audio parity.
4. When deep verification is enabled, ffmpeg performs a full decode to a null output and the
   candidate is rejected on a real decode error or unsuccessful exit.
5. When poster embedding produces a poster-bearing candidate, that candidate passes the same
   verification again. If extraction, muxing, or re-verification fails, the poster candidate is
   discarded and the already verified encode remains the replacement candidate.
6. The final candidate must still be strictly smaller than the original. An equal-size or larger
   result is discarded.
7. `ReplacementTransactionCoordinator` confirms that the original and the verified candidate both
   exist, records a `Prepared` entry in the replacement journal, and moves the candidate to a
   distinct, same-directory staging path. The staging path is not the encoder output path.

Only after all seven gates pass does `ReplacementTransactionCoordinator` ask `FileRemover` to
remove the original.

## Failure Guarantees And Run Controls

- Missing ffmpeg, ffprobe, or HandBrakeCLI keeps Start disabled, so processing cannot begin with an
  incomplete toolchain.
- Insufficient free space, encode failure, verification failure, or output growth stops that
  candidate before original removal and leaves the original untouched.
- A poster failure never promotes an unverified poster-bearing file. VideoTriage falls back to the
  already verified encode, which may still proceed through the size, staging, and removal gates.
- Exceptions before `FileRemover` is called leave the original untouched. Temporary artifacts can
  remain after exceptional paths and are excluded from later discovery.
- Pause is cooperative and is checked once, immediately before each queued file is processed.
  It prevents that file from proceeding until Resume, but it does not suspend a file that is
  already probing, encoding, verifying, embedding a poster, or replacing.
- Stop requests cancellation and takes effect at the next cancellation boundary. Cancellation-aware
  external processing is stopped; when `ProcessRunner` observes cancellation, it kills the active
  process tree and waits for exit. Cleanup is best effort: poster work files and verifier stderr
  files have dedicated cleanup, and the pipeline removes an encode temp when cancellation
  propagates through its protected processing block. Other interrupted or failed paths can leave
  temporary files.
- Once the synchronous replacement transaction has begun, Stop cannot interrupt it. The file may
  finish as a completed replacement, a `ReplacePartial`, or an exception requiring manual
  inspection. Cancellation does not roll back replacements completed earlier in the run, so review
  completion and partial outcomes before retrying.

## Partial Replacement Recovery

After removal, the coordinator records `OriginalRemoved` in the journal, renames staging to the
canonical `.mp4` path, and records `Committed`. If that final rename fails with an I/O or access
error, the verified replacement stays at its staging path:

`<base-name>.videotriage.staging.<transaction-id>.mp4`

For example, `clip.mov` leaves `clip.videotriage.staging.<transaction-id>.mp4`. The journal records
`Partial` and the result is `ReplacePartial` with `OriginalRemoved` set to `true`. This is a
recoverable partial outcome, not a claim that the original still exists. Follow
[Install VideoTriage](installation.md#recover-a-partial-replacement) to inspect and recover the
preserved file.

A crash can also interrupt a transaction between journal entries. Before each non-dry run, the
pipeline replays the folder's journal. When the original and staging both exist, staging is
deleted. When the original is gone and staging exists, staging is moved to the intended final path
(or left at staging if that path is taken) and the deletion manifest is repaired. Any other state
stops the run with a recovery-required error for manual inspection.

## Deletion Modes

- **Recycle Bin** is the default and requests recoverable Windows deletion.
- **Permanent** is an explicit hard-delete mode. The application requires a fresh confirmation
  before saving settings or starting a run with this mode. Treat the warning literally: recovery
  is not provided by VideoTriage.
- Only `FileRemover` calls the Windows permanent-delete or Recycle Bin APIs for originals.
  Temporary-file cleanup uses separate file operations.
- Replacement and removal tests are isolated with fake filesystems and a fake `FileRemover`; they
  do not delete user files or send files to the real Recycle Bin.

## Dry Run

Dry-run performs discovery, ffprobe probing, and classification only. It does not run HandBrakeCLI,
perform deep-decode verification, create or embed a poster, stage or replace a candidate, remove an
original, or persist completed-file, result-log, or deletion-manifest state.

## State And Audit

Each non-dry run creates state under the selected folder at
`<selected folder>\_videotriage_data` by default:

- `completed.jsonl` records completed replacement and selected skip outcomes.
- `results.jsonl` records terminal per-file results.
- `deletions.csv` receives a deletion record only when the replacement result reports
  `OriginalRemoved` as `true`.
- `replacement-journal.jsonl` records each replacement transaction phase for crash recovery.
- `active-run.json` records the in-progress run and is cleared when a run finishes normally.
- `run.lock` is held exclusively while a run is active, so two runs cannot share a folder.

Application diagnostics are written under `%LocalAppData%\VideoTriage\Logs`. State and audit writes
happen after replacement outcomes are known; a logging or bookkeeping concern never weakens the
verify-before-destroy ordering.
