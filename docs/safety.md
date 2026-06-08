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
7. `SafeReplacer` moves the verified candidate to a distinct, same-directory staging path. The
   staging path is not the encoder output path.
8. `SafeReplacer` confirms that staging exists and has the expected byte length.

Only after all eight gates pass does `SafeReplacer` ask `FileRemover` to remove the original.

## Failure Guarantees And Run Controls

- Missing ffmpeg, ffprobe, or HandBrakeCLI keeps Start disabled, so processing cannot begin with an
  incomplete toolchain.
- Insufficient free space, encode failure, verification failure, or output growth stops that
  candidate before original removal and leaves the original untouched.
- A poster failure never promotes an unverified poster-bearing file. VideoTriage falls back to the
  already verified encode, which may still proceed through the size, staging, and removal gates.
- Exceptions before `FileRemover` is called leave the original untouched. Temporary artifacts can
  remain after exceptional paths and are excluded from later discovery.
- Pause is cooperative and is currently checked once, immediately after each file is discovered.
  It prevents that file from proceeding until Resume, but it does not suspend a file that is
  already probing, encoding, verifying, embedding a poster, or replacing.
- Stop requests cancellation. When an external process is active, `ProcessRunner` kills its process
  tree and waits for exit. Cleanup is best effort: poster work files and verifier stderr files have
  dedicated cleanup, and the pipeline removes an encode temp when cancellation propagates through
  its protected processing block. Other interrupted or failed paths can leave temporary files.
- Cancellation does not roll back replacements completed earlier in the run. For a file that has
  not reached original removal, cancellation leaves the original untouched.

## Partial Replacement Recovery

After removal, `SafeReplacer` renames staging to the canonical `.mp4` path. If original removal
succeeds but that final rename fails with an I/O or access error, VideoTriage attempts to preserve
the verified staging replacement as:

`<base-name>.videotriage.partial.<process-id>.mp4`

For example, `clip.mov` becomes `clip.videotriage.partial.<process-id>.mp4`. When that fallback
rename succeeds, the result is `ReplacePartial` with `OriginalRemoved` set to `true`; this is a
recoverable partial outcome, not a claim that the original still exists. If the fallback rename
also fails, the operation throws and the verified replacement may remain at its
`.videotriage.staging.<process-id>.mp4` path, requiring manual inspection. Follow
[Install VideoTriage](installation.md#recover-a-partial-replacement) to inspect and recover a
successfully preserved partial file.

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

Application diagnostics are written under `%LocalAppData%\VideoTriage\Logs`. State and audit writes
happen after replacement outcomes are known; a logging or bookkeeping concern never weakens the
verify-before-destroy ordering.
