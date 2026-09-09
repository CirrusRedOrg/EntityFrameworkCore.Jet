# The index-key truncation checksum

*Working note, not spec. The verified layout lives in `docs/format/page-03-04-index-btree.md` §10.4; this is
how it was arrived at, kept because the reasoning is worth more than the one line it produced.*

**Status: settled.** Every question this note was opened with has an answer. Nothing here is outstanding.

## What the checksum is for

An index entry caps at 510 bytes. Past that ACE keeps the first **508** bytes of the key and replaces the rest
with a **2-byte checksum over the bytes it dropped**, big-endian. That is what stops two long values sharing a
508-byte prefix from collapsing onto one key: the tails still separate them, just not order-preservingly.

The checksum is a 16-bit LFSR-style fold built from the step vector
`0580 0F80 1B80 3380 6380 C380 8381 0383`, no initial value and no final XOR. That much was byte-verified long
ago and never in doubt: the function is affine over GF(2) and shift-invariant across 173 observations, which
made the table solvable by Gaussian elimination over the measured per-byte contributions, and the solution
predicts all 657 of them. **What was in doubt, without anyone realising, was the framing at the end of the
run** — and that is what the rest of this note is about.

## The defect

The last byte was handled as an exception:

```csharp
foreach (byte b in discarded[..^1]) crc = (crc >> 8) ^ Table[crc & 0xFF] ^ b;   // note the [..^1]
```

justified as *"the terminator is excluded … it is `0x00` anyway, and a linear map sends zero to zero."*

The second half of that sentence is the tell. **If the byte is `0x00`, skipping it and folding it are not
distinguishable** — so every measurement the rule was derived from was incapable of choosing between them. And
they were all incapable in the same way, because they were all **all-text keys**, and an ascending text key
always ends in its `0x00` terminator. It was not a bad measurement; it was a blind spot the data could not see
past.

Put a numeric column last and that byte is data. `(TEXT(255), TEXT(255), LONG)` produced keys that agreed with
ACE for 508 bytes and then disagreed in the checksum — silent, in the worst way this subsystem has: neither
engine errors, ACE writes its own key into the same index, and seeks quietly miss rows.

## How the rule was found

The first hypothesis — that ACE folds every byte with no exception — was wrong, and so was every other *slice*
hypothesis. A brute-force search over every prefix, suffix and sub-range of the whole 517-byte key found **no
span at all** whose fold reproduced ACE's value. That ruled out the entire "we are feeding the CRC the wrong
bytes" family and forced attention onto the arithmetic instead.

The answer was visible once the two columns were XORed:

| LibRed | ACE | XOR | last discarded byte |
| --- | --- | --- | --- |
| `88DD` | `60DD` | `E8` | `E8` |
| `82ED` | `69ED` | `EB` | `EB` |
| `840D` | `6D0D` | `E9` | `E9` |
| `877D` | `6D7D` | `EA` | `EA` |

The low byte never differs; the high byte differs by exactly the final discarded byte:

```
ACE = fold(discarded[..^1]) ^ (discarded[^1] << 8)
```

Four rows differing only in one nibble is a thin basis, so it was re-measured over eight values each of
`LONG`, `CURRENCY` and `DOUBLE`, spread deliberately across byte patterns. **24 of 24 held**, and the all-text
keys the original rule came from still matched, as they must — their XOR term is zero.

## Why the last byte is special: it isn't

The XOR form looks like an arbitrary finalisation quirk. It is not. Let `F(x) = (x >> 8) ^ Table[x & 0xFF]`.
Because `b << 8` lands entirely in the half that gets shifted down,

```
F(x ^ (b << 8)) = F(x) ^ b
```

so a loop that XORs each byte into the **high** half and folds at the *top* of the next iteration produces,
for three bytes:

```
i=0:  crc = b0<<8              → fold → F(0) ^ b0 = b0
i=1:  crc = b0 ^ (b1<<8)       → fold → F(b0) ^ b1
i=2:  crc = F(b0) ^ b1 ^ (b2<<8)     → loop exits, no fold
```

which is exactly `fold(b0,b1) ^ (b2<<8)`. **The special case is just where the loop ends.** Written this way
there is no exception anywhere in the algorithm:

```
crc = 0
for each byte b:
    crc ^= b << 8
    if b is not the last:  crc = (crc >> 8) ^ Table[crc & 0xFF]
```

Confirmed numerically against the shipped implementation over 12,800 random inputs at every length from 1 to
64 bytes, plus the all-zero, all-`0xFF` and leading-zero edges. The shipped code keeps the
`fold-then-XOR-the-tail` shape because it is the cheaper expression of the same function; the equivalence is
recorded in `JetIndexKeyChecksum`'s remarks so the "special case" is not mistaken for a guess again.

## The other three questions

- **Is it the last byte, or a fixed offset in the key?** The last byte of the run. All the original
  measurements shared one key length (517), which could not separate the two. Re-measured over keys of 511 to
  521 bytes, giving discarded runs of 3 to 13 bytes: the rule holds throughout, keyed to the run's end.
- **What about a very short discarded run?** The shortest possible is **3 bytes** — truncation triggers only
  above 510 and the run is `length − 508` — and it is measured. Runs of 1 or 2 bytes are unreachable, so the
  question was built on a wrong premise. `Compute` on a 1-byte span would fold nothing and return `b << 8`,
  which is consistent, but nothing can call it that way.
- **Is a discarded word-sort record really unreconstructable?** No, and it never was. A hyphen or apostrophe
  emits an inline record into the key's trailing section, which for a long value always lands in the dropped
  bytes; that case was refused on the grounds that the record is unobservable and that ACE might recompute its
  position when truncating. Measured over two mark characters at eight positions each: the position byte
  tracks where the mark actually sat (`8057`, `800F`, `83C7`, `83E7`, … as the offset moves), and the checksum
  over LibRed's reconstruction matches ACE's on **16 of 16**. The refusal is gone — it had been rejecting
  inserts ACE accepts. The record was always observable *by its consequences*; it simply could not be checked
  until the checksum's own arithmetic was known, which is the reverse of the order the original note assumed.

## Reproducing

`IndexKeyTruncationAccessTests` (in `LibRed.Core.Tests`, needs Windows + ACE) holds the shapes that matter:
all-text past the cap, numeric-tailed below and past it for each of `LONG`/`CURRENCY`/`DOUBLE`, and a dropped
word-sort record at seven mark/position combinations. The probe pattern, if you need to go further: create and
insert through ACE, read the entries back with `IndexCursor(...).RawEntries()`, and compare against
`IndexKeyEncoder.EncodeWithoutLengthLimit` — the untruncated key is what you need in order to reason about
what ACE dropped.

The one methodological lesson worth carrying: **a rule derived from inputs that cannot falsify it is not a
measurement.** "It is `0x00` anyway" should have been read as a warning that the experiment had no power,
not as a reassurance.
