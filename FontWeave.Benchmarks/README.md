# FontWeave.Benchmarks

In-process benchmark harness for the FontWeave library over the pinned `bench-inputs/`
corpus. Each scenario is the complete per-invocation work the corresponding
`FontWeave.Cli` command performs — open chain, operation, and the output copy into a
memory buffer — measured against the library's public API only (`FontWeave` has no
`InternalsVisibleTo` for this project). Nothing in `FontWeave/` product source is
touched.

## Corpus

The corpus is external and **not in Git**: `bench-inputs/` stays untracked next to the
repository root. The harness never hard-codes absolute paths; a corpus directory is
resolved in this order:

1. `--inputs <dir>` on the command line,
2. the `FONTWEAVE_BENCH_INPUTS` environment variable (also set automatically by the
   runner so BenchmarkDotNet's child processes inherit it),
3. an upward walk (up to 12 levels) from the program directory / current directory
   looking for `bench-inputs/`.

The pinned default list (used when `--fonts` is not given) is exactly the seven files
of the pinned corpus; `--fonts a,b,c` replaces it for one run (`.ttc` entries route to
the TTC benchmark set). Corpus files are read fully into memory before measurement;
they are never written.

## Running

```sh
dotnet build FontWeave.Benchmarks -c Release

# smoke validation: every scenario, one warmup + N timed rounds, single thread
dotnet FontWeave.Benchmarks/bin/Release/net10.0/FontWeave.Benchmarks.dll smoke
dotnet ... smoke --rounds 3 <file1> <file2> ...

# real benchmarks (BenchmarkDotNet + MemoryDiagnoser); short run:
dotnet FontWeave.Benchmarks/bin/Release/net10.0/FontWeave.Benchmarks.dll \
    --filter "*" --job short
# single scenario / single font:
dotnet ... --filter "*SubsetRetain*" --job short
dotnet ... --filter "*TtcScenario*" --job short
# non-default corpus:
dotnet ... --inputs D:/corpus --fonts "a.sfnt,b.ttc" --filter "*" --job short
```

BenchmarkDotNet 0.15.8 runs each benchmark class in its default child-process
toolchain; `--job short` is the documented short-run configuration. Output artifacts
land in `BenchmarkDotNet.Artifacts/` (gitignored, run-local).

`list` prints the resolved corpus directory and the active labels.

## Scenarios

Every scenario opens the same public chains the CLI opens and, where the command
writes a file, copies the compiled sfnt into a fresh uninitialized buffer
(`GC.AllocateUninitializedArray` + `CompiledFont.TryCopyTo`), mirroring
`FontWeave.Cli`'s output-to-memory shape.

| Scenario | CLI mirror | FontWeave public entry chain |
|---|---|---|
| `OpenFace` | `inspect` (container level) | `FontFile.Parse` → `GetFace(0)` → enumerate `face.Tables` (directory view only) |
| `ReadAll` | `inspect` / `validate` (typed, whole face) | `FontSource.TryCopy` → `SfntFaces.TryGetFace` → `FontInspection.TryValidate(face, requestedTag: null)` — the full typed per-table read walk |
| `EmitRoundtrip` | (subset write path, whole domain) | open chain → `SubsetOperation.TryPrepare` with `ExplicitGlyphSet` root = **all** GIDs, `Mode = RetainGids` → `Font.TryCopyTo(buffer)` |
| `SubsetRetain` | `subset --mode retain --gids 0..min(512,numGlyphs)-1` | same chain, root = first `min(512, numGlyphs)` GIDs |
| `SubsetCompact` | `subset --mode compact` (same root) | same chain, `Mode = CompactRemap` |
| `SubsetCodepoints` | `subset --codepoints ...` | typed cmap read (`CmapContext`/`CmapView`/`CmapTable.TryGetMappings`, GlobalSetup) → `SubsetGlyphRoot { Kind = UnicodeCodepoints }` → `SubsetOperation` (retain) |
| `EditAndEmit` | `edit-rebuild --record N --text ...` | first UTF-16 record with editable `NameId` ∈ {1,2,4,16,17} (GlobalSetup) → `MetadataEditOperation.TryPrepare(face, NameEditPlan)` → `TryCopyTo(buffer)` |
| `TtcOpen` | (collection parse) | `FontFile.Parse` over the TTC: every face directory parsed + overlap-validated, both faces' tables enumerated |
| `TtcSplitFace0/1` | `name-replace --face N` (the CLI's TTC-capable write chain) | `FontFile.Parse` → `GetFace(i)` → `NameTable.Parse` → `NameEditor.Replace` → `SfntWriter.Prepare(DropSignature, PreserveExplicit)` → standalone sfnt buffer |

Uniform subset options everywhere: `PreserveLayout = true` (S2a raw layout retention
in retain mode; the S2b rewrite in compact mode) and `DropDigitalSignature = true`.
These are explicit CLI-visible caller choices; the corpus carries layout tables and
DSIG, so the scenarios measure the retention path, not the default refusal.

Two input-preparation predicates are replicated harness-side because the product keeps
them internal (registered in the harness report, no product change): the
Unicode-semantic cmap record rule (`platformId == 0 || (3, 0/1/10)`) used to derive
the codepoint root, and the UTF-16 name-record rule (`(0, ≤4)` or `(3, 0/1/2/6/10)`)
used to pick the edit ordinal. Derived roots follow the product's request contracts:
strictly ascending, duplicates-free, no surrogates, no variation selectors; the
codepoint root takes mapped ASCII printable scalars first, then stride-selected
additional mapped scalars above `U+007E` up to 512 total.

### Return convention

Benchmark methods return a positive number (emitted sfnt bytes, or the consumed read
extent for the read scenarios) or a **negative** `-(ReadStatus)` when the product
issues a finite refusal. Refusals are defined product behavior, not harness errors.

## Known refusal map (pinned corpus, retain/compact + PreserveLayout + DropSignature)

| Font | ReadAll | EmitRoundtrip | SubsetRetain | SubsetCompact | SubsetCodepoints | EditAndEmit |
|---|---|---|---|---|---|---|
| roboto (TT, 3387 glyphs) | Done | Done | Done | refused `UnsupportedOperation` (VORG on a non-CFF face; RC2/SC §3) | Done | Done |
| source-sans (TT, 2478) | Done | Done | Done | refused `UnsupportedOperation` (BASE BaseCoordFormat2 reference; RF-S2b-4) | Done | Done |
| opensans (TT VF, 1150) | Done | Done | Done | Done | Done | Done |
| source-serif (CFF2 VF, 1464) | Done | Done | Done | refused `UnsupportedOperation` (BASE fmt2) | Done | Done |
| noto COLR/SVG (TT, 42352) | Done | refused `CmapFormatUnsupported` (cmap format 14 UVS, OQ-S0-9) | refused (same) | refused (same) | refused (same; derivation yields the empty set) | Done |
| fontations CBDT (TT, 4) | refused `UnsupportedVersion` (CBDT/CBLC 2.0 vs 3.0; post 3.0 UnsupportedOperation — first-failure status, full report retained) | Done | Done | refused `UnsupportedVersion` | Done (2 mapped scalars) | Done |
| cambria-math.ttc (2 faces) | — | — | — | — | — | TtcOpen / both splits Done |

`EmitRoundtrip` is deliberately the retain-GIDs identity-domain roundtrip: compact
remap — the widest typed rebuild — refuses on four of six sfnt faces under the frozen
policy matrix (above), so the "recompile-and-emit" scenario pins the whole-domain form
that the broadest corpus admits; the extra compact rebuild work is covered by
`SubsetCompact` where the product admits it. The typed operation chain
(`SubsetOperation`/`MetadataEditOperation`/`FontInspection` typed validation)
additionally refuses TTC faces outright (`SfntDirectoryView.Validate` rejects
collection faces), which is why the TTC split scenarios run the legacy write chain —
the same chain the CLI uses for `--face` writes.

## Methodology notes

- **JIT warmup.** BenchmarkDotNet runs Pilot/Warmup/Actual iterations per benchmark in
  a dedicated process; the smoke runner does one warmup call before its timed rounds.
  Tiered JIT and first-call costs never reach the measurements.
- **Single thread.** One thread, no concurrency; FontWeave's budget accounting and
  this harness assume serial callers.
- **Fonts stay resident.** Corpus bytes are read once per parameter set (GlobalSetup)
  and stay referenced for the whole run; every measured invocation re-copies the
  snapshot via `FontSource.TryCopy`/`FontFile.Parse`, which is part of the open chain
  under measurement.
- **Allocation counts.** MemoryDiagnoser reports managed allocations per operation
  (inclusive of the output buffer); it does not count native/unmanaged memory
  (there is none in FontWeave's managed snapshot model). The smoke runner reports
  `GC.GetTotalAllocatedBytes(precise: true)` deltas — process-lifetime counters,
  so background allocations between calls can add noise that BDN's per-op column
  does not have.
- **Refusal rows.** A refused (font, scenario) pair measures the
  decide-whole-before-compile prefix (admission + policy decisions), not subset
  throughput. Read the table above before comparing numbers; never compare a Done row
  against a refused row.
- **In-process scope.** All measurements are in-process against the built
  `FontWeave.dll` (Release); no CLI subprocess is timed. BDN's process-level
  isolation between benchmarks of one class still applies (default toolchain).
- **Derivation is outside the timed region.** GID roots, the codepoint root and the
  edit ordinal are fixed in GlobalSetup, exactly like a CLI caller fixing arguments
  before a command.

## Project shape

- `Program.cs` — modes: `bench` (default, BenchmarkSwitcher), `smoke`, `list`.
- `Corpus.cs` / `BenchmarkHost.cs` — corpus resolution and input preparation.
- `Scenarios.cs` — the ten scenarios, shared by the smoke runner and BDN.
- `Benchmarks.cs` — BDN classes: `[MemoryDiagnoser]`, `[ParamsSource]` over font names.
- `SmokeRunner.cs` — single-shot validation runner (Stopwatch + CPU time +
  `GC.GetTotalAllocatedBytes` deltas).

`net10.0`, `TreatWarningsAsErrors=true`, `CheckForOverflowUnderflow=true`,
`IsPackable=false` — matching the repository's FontWeave projects.
