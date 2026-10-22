---
name: tidy-format-spec
description: Tidy the LibRed on-disk format spec (src/LibRed/docs/format/*.md) down to what the format IS, stripping the record of how facts were found while keeping status markers. Use when asked to tidy/clean up the format docs, remove research-record cruft, or run the periodic format-spec cleanup.
---

# Tidy the format spec down to what the format is

The files under `src/LibRed/docs/format/` are LibRed's reference for the on-disk Jet 4 / ACE
format. Over time they re-accumulate the *record of how each fact was found* mixed into the facts
themselves. This skill is the periodic pass that strips that back out.

## The governing rule

**The docs state what the format IS — not how it was verified, or what it was verified against.**

### REMOVE (research record — how/against-what a fact was found)
- Fixture / sample file names: `Northwind.accdb`, `WideTable.accdb`, `complex1.accdb`, `[Order Details]`, a "generated 200-column table", "~150 MB file", etc.
- Corpus counts and sample sizes: "in 754 of 1,676 tables", "33 carry 0, 5 carry 1", "17 of 17 extractable", "no order departs in more than 47 of 193 sample values".
- Methodology: "verified by diffing before/after a `CREATE TABLE`", "pages touched", "the tests check it after every insert", "read back what LibRed produced", whole-file-diff descriptions.
- Test / probe names: `ComplexDuplicateValueProbeTest`, "see the probe below", "What the probes settled".
- Dated sweeps: "as of 2026-..", "a sweep in ..".
- Historical / self-referential corrections: "was a live bug", "an earlier revision said", "previously thought", "used to say". REMOVE these **unless** a reader would naturally reach the wrong reading on their own — then restate it as a positive RULE, not as history (e.g. the "fold between bytes" note in page-03-04).

### KEEP (do not strip)
- **Verified / inferred / unverified / assumed / "measured" / "not reconciled with ACE" status markers.** Bare `(verified)` and `inferred` tags STAY — a fact's reliability is part of what is known about the format. This is the #1 thing people wrongly strip. The prior passes keep lines like "Measured both ways." verbatim.
- Every offset, size, structure, and mechanic.
- Code symbol names that describe how a format rule is enforced (`FindPageWithRoom`, `TryAppend`, `RowCodec`, `LongValueStore`, …).
- The brief Provenance skeleton in README (mdbtools consulted upstream; write-side checked against Access via OLE DB) — in its trimmed form; don't expand or delete it.
- A note that the spec *disagrees* with mdbtools on a point (that's a format statement), but not "we corroborated against mdbtools".

**When unsure whether a phrase is a format fact or a research record, KEEP it**, and surface it to the user as a judgment call rather than deleting unilaterally. Large illustrative tables (e.g. the collation departure table, the recomputation step table) are the user's call — ask; do not gut them.

## Worked examples (study these first)

Run `git show <hash> -- src/LibRed/docs/format/` on each — the commit message defines the task and the diff shows the exact standard:
- `56951944` — the original pass ("Tidy the format spec down to what the format is").
- `33b74a7b` — the second pass; also resolved two contradictions (chain stamp, `colCount`).

## Process

1. **Calibrate.** Read the two worked-example commits above (message + diff).
2. **Scope the drift.** Most new violations are material added since the last tidy commit:
   `git diff 33b74a7b --stat -- src/LibRed/docs/format/` (use the latest tidy commit). The heaviest files are usually `system-catalog.md`, `page-03-04-index-btree.md`, `page-00-database.md`, `page-05-usage-maps.md`.
3. **Edit.** Apply the rule above. A useful locator grep for candidates:
   `(?i)\.accdb|northwind|in \d+ of \d+|corpus|pages touched|diffing|was a (live )?bug|earlier revision|a sweep|as of 20|ProbeTest|read.?back`
   Then read each hit in context — many `.accdb`/`.mdb` mentions are legitimate file-format references, and many `measured`/`verified` hits are status markers to keep.
4. **Resolve contradictions against the code, not by guessing.** The passes also fix contradictions they surface (e.g. the same field described two ways in two files). The source of truth is `src/LibRed/LibRed.Core/` — read the actual read/write code (`RowCodec`, `LongValueStore`, `TableDefinition`, the `Formats/` offset constants) and make the docs match it. The README `appendix-structures.md` is the most likely to carry a stale restatement.
5. **Keep the spec-sync rule in mind** (CLAUDE.md): any claim must be verifiable against real files or ACE; this pass does not invent facts, only removes the research record around them.

## How to run it efficiently

- This is a large pass (10+ files). Split the files into groups and delegate each group to a
  parallel subagent (Agent tool), giving each the rule above and the worked-example commit hashes
  to study. Keep the findings/edits; review each file's `git diff` yourself afterwards.
- **Do the shell-heavy / judgment-heavy files yourself** rather than via an agent. Subagents
  running `git show`/`git diff` can trigger permission popups for the user; the main session's git
  reads are pre-allowed. Use `Read`/`Grep`/`Edit`, not shell text tools (the repo hooks deny
  `cat`/`grep`/`git grep` through the shell).
- The functional/doc change needs no build or test run — it's docs only.

## Suggested file groups (by drift weight)
- README.md + appendix-structures.md
- page-00 / page-01 / page-02a / page-02b
- page-02c / page-02d / page-02e / page-03-04 / page-05 / page-08 / page-09
- long-values / data-types / system-catalog (heaviest)

## Finishing
- Review the full `git diff -- src/LibRed/docs/format/`; confirm no offsets/sizes/facts changed, only the research-record wrapper.
- Flag borderline large tables and any contradictions to the user before committing.
- Commit with a message in the style of `56951944` / `33b74a7b`, listing any contradictions resolved.
