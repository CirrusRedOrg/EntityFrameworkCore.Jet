using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// RESEARCH: identify the two-byte value ACE puts at the end of an over-long index entry.
//
// ACE stores an entry of at most 510 bytes. Past that the weights are cut short and the last two bytes carry
// something derived from the value — distinct long values never collide, so it is a checksum rather than a
// plain truncation. Not knowing it is the only reason LibRed refuses those values instead of matching them.
//
// Opt-in via LIBRED_CHECKSUM=1.
public class KeyChecksumProbeTest(ITestOutputHelper output)
{
    /// <summary>
    /// Whether ACE's 510 bytes are a PREFIX of the key LibRed builds, and if so how much survives.
    /// </summary>
    /// <remarks>
    /// Everything downstream depends on this. If the stored bytes are a plain prefix plus two, the checksum
    /// has a well-defined input — the part that was dropped — and the search is over functions. If they are
    /// not, ACE re-encodes rather than truncates, and there is no checksum to find in the first place.
    /// </remarks>
    [Fact]
    public void Probe_truncated_key_structure()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        foreach (byte version in (byte[])[0, 1])
        {
            (string source, string? created, ColumnDef column) = Fixture(version);
            try
            {
                Dictionary<string, string> ace = AceKeys(source, "chk", [.. Samples()]);
                output.WriteLine($"--- General v{version}");
                foreach (string text in Samples())
                {
                    if (!ace.TryGetValue(text, out string? stored)) { output.WriteLine($"  {Label(text)}: not stored"); continue; }
                    byte[] aceKey = Convert.FromHexString(stored);
                    byte[] full = IndexKeyEncoder.EncodeWithoutLengthLimit([(column, true)], [text]);

                    int shared = 0;
                    while (shared < aceKey.Length && shared < full.Length && aceKey[shared] == full[shared]) shared++;

                    output.WriteLine(
                        $"  {Label(text),-22} full {full.Length,5}B  ace {aceKey.Length,4}B  " +
                        $"common prefix {shared,4}  ace tail {Convert.ToHexString(aceKey)[^8..]}  " +
                        $"full at cut {(shared + 4 <= full.Length ? Convert.ToHexString(full[shared..(shared + 4)]) : "--")}");
                }
            }
            finally { if (created is not null) TemporaryDatabase.Delete(created); }
        }
    }

    /// <summary>
    /// Tries the standard checksum catalogue against the discarded bytes.
    /// </summary>
    /// <remarks>
    /// The structure probe showed the two bytes depend only on what was CUT: changing a character inside the
    /// surviving prefix leaves them alone, changing one past the cut moves them. So the input is known and
    /// only the function is not, which makes a catalogue sweep worth trying before anything cleverer.
    /// Several framings of the input are tried too, since a checksum is often taken over a slightly different
    /// span than the obvious one.
    /// </remarks>
    [Fact]
    public void Probe_identify_the_checksum()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        var dataset = new List<(byte[] Full, byte[] Ace)>();
        foreach (byte version in (byte[])[0, 1])
        {
            (string source, string? created, ColumnDef column) = Fixture(version);
            try
            {
                string[] samples = [.. SearchSamples()];
                Dictionary<string, string> ace = AceKeys(source, "chksearch", samples);
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? stored)) continue;
                    byte[] aceKey = Convert.FromHexString(stored);
                    if (aceKey.Length != 510) continue;         // not truncated: no checksum to learn from
                    dataset.Add((IndexKeyEncoder.EncodeWithoutLengthLimit([(column, true)], [text]), aceKey));
                }
            }
            finally { if (created is not null) TemporaryDatabase.Delete(created); }
        }

        output.WriteLine($"{dataset.Count} truncated samples");
        Assert.NotEmpty(dataset);

        // Distinct checksums confirm the samples actually exercise the function rather than repeating one value.
        int distinct = dataset.Select(d => Convert.ToHexString(d.Ace[508..])).Distinct().Count();
        output.WriteLine($"{distinct} distinct checksums among them");

        (string Name, Func<byte[], (byte[] Full, byte[] Ace), byte[]> Slice)[] inputs =
        [
            ("discarded", (full, _) => full[508..]),
            ("discarded less terminator", (full, _) => full[508..^1]),
            ("whole key", (full, _) => full),
            ("whole key less start flag", (full, _) => full[1..]),
            ("kept prefix", (full, _) => full[..508]),
            ("kept prefix less start flag", (full, _) => full[1..508]),
            ("discarded reversed", (full, _) => full[508..].Reverse().ToArray()),
        ];

        var hits = new List<string>();
        foreach ((string inputName, var slice) in inputs)
        foreach ((string fnName, Func<byte[], ushort> fn) in Candidates())
        foreach (bool bigEndian in (bool[])[true, false])
        {
            bool all = dataset.All(d =>
            {
                ushort expected = bigEndian
                    ? (ushort)((d.Ace[508] << 8) | d.Ace[509])
                    : (ushort)((d.Ace[509] << 8) | d.Ace[508]);
                return fn(slice(d.Full, d)) == expected;
            });
            if (all) hits.Add($"{fnName} over {inputName} ({(bigEndian ? "big" : "little")}-endian)");
        }

        if (hits.Count == 0) output.WriteLine("no catalogue candidate reproduces every sample");
        foreach (string hit in hits) output.WriteLine($"MATCH: {hit}");

        // Whatever the answer is, record the raw pairs so the next attempt need not re-measure them.
        foreach ((byte[] full, byte[] aceKey) in dataset.Take(12))
            output.WriteLine($"  cut={Convert.ToHexString(aceKey[508..])} " +
                             $"discarded[{full.Length - 508}]={Convert.ToHexString(full[508..])[..Math.Min(48, (full.Length - 508) * 2)]}");
    }

    /// <summary>
    /// Recovers the polynomial by algebra rather than by guessing catalogue parameters.
    /// </summary>
    /// <remarks>
    /// The function is affine over GF(2), which the measurements show directly: three tails differing in one
    /// byte give <c>L(0xA3)=CA03</c>, <c>L(0x13)=6980</c> and <c>L(0xB0)=A383</c>, and
    /// <c>CA03 ^ 6980 = A383</c> exactly. Every CRC is affine, so this is very likely one.
    /// <para>
    /// That makes the parameters separable. For two messages of the SAME length, whatever constant the
    /// initial value and final XOR contribute is identical and cancels in <c>f(m) ^ f(m')</c>, leaving only
    /// the polynomial. So sweep all 65,536 polynomials against the measured XOR-deltas, and only then
    /// recover the initial value and final XOR from a single sample. No catalogue needed, and a
    /// non-standard polynomial is found just as easily as a standard one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_recover_the_checksum_polynomial()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        List<(byte[] Discarded, ushort Sum)> dataset = [.. Dataset()];
        output.WriteLine($"{dataset.Count} truncated samples, " +
                         $"{dataset.Select(d => Convert.ToHexString(d.Discarded)).Distinct().Count()} distinct tails");

        // Group by length: the cancelling trick only holds within a length.
        var groups = dataset
            .GroupBy(d => d.Discarded.Length)
            .Select(g => g.DistinctBy(d => Convert.ToHexString(d.Discarded)).ToList())
            .Where(g => g.Count > 1)
            .ToList();
        output.WriteLine($"{groups.Count} usable length groups: " +
                         string.Join(", ", groups.Select(g => $"{g[0].Discarded.Length}B x{g.Count}")));
        Assert.NotEmpty(groups);

        // Confirm affinity before trusting the method, rather than assuming it from three samples.
        foreach (List<(byte[] Discarded, ushort Sum)> group in groups.Where(g => g.Count >= 3))
        {
            (byte[] a, ushort fa) = group[0];
            (byte[] b, ushort fb) = group[1];
            (byte[] c, ushort fc) = group[2];
            byte[] abc = a.Zip(b, (x, y) => (byte)(x ^ y)).Zip(c, (x, y) => (byte)(x ^ y)).ToArray();
            var match = group.FirstOrDefault(d => d.Discarded.SequenceEqual(abc));
            if (match.Discarded is not null)
                output.WriteLine($"  affinity check: f(a^b^c) = {match.Sum:X4}, " +
                                 $"f(a)^f(b)^f(c) = {(ushort)(fa ^ fb ^ fc):X4}");
        }

        // All four reflection combinations and several framings of the message. A checksum is often taken
        // over a slightly different span than the obvious one, and the byte order it consumes is exactly the
        // sort of detail that makes a standard algorithm look like an unknown one.
        (string Name, Func<byte[], byte[]> Frame)[] framings =
        [
            ("tail", t => t),
            ("tail less terminator", t => t[..^1]),
            ("tail reversed", t => [.. t.Reverse()]),
            ("tail less terminator, reversed", t => [.. t[..^1].Reverse()]),
            ("tail byte-swapped in pairs", t =>
            {
                byte[] copy = (byte[])t.Clone();
                for (int i = 0; i + 1 < copy.Length; i += 2) (copy[i], copy[i + 1]) = (copy[i + 1], copy[i]);
                return copy;
            }),
        ];

        // Filter on the cheapest group first, then verify survivors against everything. A full sweep of
        // polynomial x reflection x framing over every group would be millions of CRCs across 768-byte tails.
        List<(byte[] Discarded, ushort Sum)> cheapest = groups.MinBy(g => g[0].Discarded.Length * g.Count)!;

        var found = new List<string>();
        foreach ((string framingName, var frame) in framings)
        for (int poly = 0; poly <= 0xFFFF; poly++)
        foreach ((bool refIn, bool refOut) in ((bool, bool)[])[(false, false), (true, true), (true, false), (false, true)])
        {
            if (!Fits(cheapest)) continue;
            if (!groups.All(Fits)) continue;

            bool Fits(List<(byte[] Discarded, ushort Sum)> group)
            {
                (byte[] first, ushort firstSum) = group[0];
                ushort baseline = Crc16(frame(first), (ushort)poly, 0, refIn, refOut, 0);
                return group.Skip(1).All(d =>
                    (ushort)(Crc16(frame(d.Discarded), (ushort)poly, 0, refIn, refOut, 0) ^ baseline)
                    == (ushort)(d.Sum ^ firstSum));
            }

            // The polynomial fits. Recover the constant it leaves behind, and check it is the SAME constant
            // for every length — a real CRC's initial value produces a length-dependent constant, so a single
            // constant across lengths means init is zero and the leftover is the final XOR.
            var constants = dataset
                .GroupBy(d => d.Discarded.Length)
                .Select(g => (Length: g.Key,
                              Constant: (ushort)(g.First().Sum ^ Crc16(frame(g.First().Discarded), (ushort)poly, 0, refIn, refOut, 0))))
                .ToList();
            string shape = constants.Select(c => c.Constant).Distinct().Count() == 1
                ? $"xorOut {constants[0].Constant:X4}, init 0"
                : "length-dependent constant (non-zero init): " +
                  string.Join(" ", constants.Take(5).Select(c => $"{c.Length}B={c.Constant:X4}"));
            found.Add($"poly {poly:X4} refIn={refIn} refOut={refOut} over {framingName} — {shape}");
        }

        if (found.Count == 0) output.WriteLine("no polynomial fits — the function is affine but not a CRC of this shape");
        foreach (string line in found.Take(20)) output.WriteLine($"CANDIDATE: {line}");
        output.WriteLine($"{found.Count} candidate polynomials");
    }

    /// <summary>
    /// Whether a byte's contribution depends on its distance from the END or on its absolute position.
    /// </summary>
    /// <remarks>
    /// The function is linear but not a CRC in any framing tried, so the useful question is no longer "which
    /// algorithm" but "what shape". Every CRC is shift-invariant: flipping a bit at a given distance from the
    /// end changes the result by a fixed amount, whatever the message length or the surrounding bytes. If
    /// that holds here there is a 16x16 step operator, and recovering it from two adjacent distances
    /// determines the whole function — no name required. If it does not hold, the contribution is
    /// position-weighted and the CRC family is out entirely.
    /// <para>
    /// Reads pairs that differ in exactly one byte out of the measured set rather than constructing them,
    /// since the tail bytes are whatever the collation produces and cannot be chosen directly.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_checksum_shift_invariance()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        List<(byte[] Discarded, ushort Sum)> dataset = [.. Dataset()];
        var samples = dataset.DistinctBy(d => Convert.ToHexString(d.Discarded)).ToList();
        output.WriteLine($"{samples.Count} distinct tails");

        // (distance from end, byte delta) -> the checksum deltas seen, and at which lengths.
        var contributions = new SortedDictionary<(int Distance, byte Delta), List<(int Length, ushort Change)>>();
        foreach (var group in samples.GroupBy(s => s.Discarded.Length))
        {
            var members = group.ToList();
            for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++)
            {
                byte[] a = members[i].Discarded, b = members[j].Discarded;
                int at = -1;
                bool single = true;
                for (int k = 0; k < a.Length; k++)
                    if (a[k] != b[k]) { if (at >= 0) { single = false; break; } at = k; }
                if (!single || at < 0) continue;

                var key = (a.Length - 1 - at, (byte)(a[at] ^ b[at]));
                (contributions.TryGetValue(key, out var list) ? list : contributions[key] = [])
                    .Add((a.Length, (ushort)(members[i].Sum ^ members[j].Sum)));
            }
        }

        output.WriteLine($"{contributions.Count} distinct (distance, delta) observations");

        int consistent = 0, inconsistent = 0;
        foreach (((int distance, byte delta), List<(int Length, ushort Change)> seen) in contributions)
        {
            if (seen.Select(s => s.Change).Distinct().Count() == 1)
            {
                consistent++;
                if (seen.Select(s => s.Length).Distinct().Count() > 1 && consistent <= 8)
                    output.WriteLine($"  SAME across lengths: d={distance,3} delta={delta:X2} -> {seen[0].Change:X4} " +
                                     $"at lengths {string.Join(",", seen.Select(s => s.Length).Distinct())}");
            }
            else
            {
                inconsistent++;
                if (inconsistent <= 8)
                    output.WriteLine($"  DIFFERS: d={distance,3} delta={delta:X2} -> " +
                                     string.Join(" ", seen.DistinctBy(s => s.Length).Take(4).Select(s => $"{s.Length}B:{s.Change:X4}")));
            }
        }

        output.WriteLine($"{consistent} consistent, {inconsistent} inconsistent");
        output.WriteLine(inconsistent == 0
            ? "SHIFT-INVARIANT — a step operator exists and determines the whole function"
            : "NOT shift-invariant — the contribution depends on absolute position, so no CRC-shaped operator");

        // The contribution of one byte at the smallest distances, which is what a step operator is built from.
        foreach (int d in (int[])[0, 1, 2, 3])
            foreach (((int distance, byte delta), var seen) in contributions.Where(c => c.Key.Distance == d).Take(6))
                output.WriteLine($"  d={distance} delta={delta:X2} -> {string.Join("/", seen.Select(s => $"{s.Change:X4}").Distinct())}");
    }

    /// <summary>
    /// Finds the step operator, given that the byte goes into the LOW half and the register advances first.
    /// </summary>
    /// <remarks>
    /// The shift-invariance data says what shape to look for. At distance 1 a byte contributes its own value
    /// unchanged into the low byte, so the update is <c>crc = S(crc) ^ b</c> — advance, then XOR into the
    /// bottom — and not the usual <c>crc = S(crc ^ (b &lt;&lt; 8))</c>. That single difference is why sweeping
    /// 65,536 polynomials in the standard framing found nothing: the injection point was wrong, so no
    /// polynomial could have matched.
    /// <para>
    /// With that shape, a byte at distance d contributes exactly <c>S^(d-1)(b)</c>, so each candidate can be
    /// tested against the measured contributions directly instead of against whole messages.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_recover_the_step_operator()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        List<(int Distance, byte Delta, ushort Change)> observations = [.. Contributions()];
        output.WriteLine($"{observations.Count} contribution observations, " +
                         $"distances {observations.Min(o => o.Distance)}..{observations.Max(o => o.Distance)}");

        (string Name, Func<ushort, ushort, ushort> Step)[] steps =
        [
            ("left shift", (x, poly) =>
            {
                for (int i = 0; i < 8; i++) x = (x & 0x8000) != 0 ? (ushort)((x << 1) ^ poly) : (ushort)(x << 1);
                return x;
            }),
            ("right shift", (x, poly) =>
            {
                for (int i = 0; i < 8; i++) x = (x & 1) != 0 ? (ushort)((x >> 1) ^ poly) : (ushort)(x >> 1);
                return x;
            }),
        ];

        // Filter on the shallow observations, which are cheap, then verify survivors against every one.
        var shallow = observations.Where(o => o.Distance <= 4).ToList();
        var found = new List<string>();
        foreach ((string name, var step) in steps)
        for (int poly = 0; poly <= 0xFFFF; poly++)
        {
            if (!shallow.All(o => Apply(o.Delta, o.Distance) == o.Change)) continue;
            if (!observations.All(o => Apply(o.Delta, o.Distance) == o.Change)) continue;
            found.Add($"{name}, poly {poly:X4}");

            ushort Apply(byte delta, int distance)
            {
                ushort value = delta;
                for (int i = 1; i < distance; i++) value = step(value, (ushort)poly);
                return value;
            }
        }

        foreach (string line in found) output.WriteLine($"STEP OPERATOR: {line}");
        if (found.Count == 0) output.WriteLine("no shift-register step matches — recover the 16x16 matrix instead");
    }

    /// <summary>
    /// Recovers the 16x16 step matrix directly, since no shift register reproduces it.
    /// </summary>
    /// <remarks>
    /// Linear and shift-invariant is enough on its own — the function does not have to be a named algorithm
    /// to be reproduced exactly. Distance 1 contributes the byte unchanged, so distance 2 gives
    /// <c>S(e_i)</c> for the eight low basis vectors and distance 3 gives <c>S²(e_i)</c>. Where
    /// <c>{e_i} ∪ {S(e_i)}</c> spans all sixteen dimensions, S is known on a full basis and therefore
    /// everywhere: S maps each <c>e_i</c> to its distance-2 value and each of those to its distance-3 value.
    /// <para>
    /// Solved by Gaussian elimination over GF(2) rather than by picking deltas, because the tail bytes are
    /// whatever the collation produced and single-bit deltas cannot be ordered up.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_recover_the_step_matrix()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        List<(int Distance, byte Delta, ushort Change)> observations = [.. Contributions()];
        output.WriteLine($"{observations.Count} observations");

        ushort[]? atTwo = SolveBasis(observations, distance: 2);
        ushort[]? atThree = SolveBasis(observations, distance: 3);
        if (atTwo is null || atThree is null)
        {
            output.WriteLine($"not enough independent deltas (d=2 {(atTwo is null ? "short" : "ok")}, " +
                             $"d=3 {(atThree is null ? "short" : "ok")})");
            return;
        }
        output.WriteLine("S(e_i) = " + string.Join(" ", atTwo.Select(v => v.ToString("X4"))));
        output.WriteLine("S2(e_i) = " + string.Join(" ", atThree.Select(v => v.ToString("X4"))));

        // S is known on {e_i} -> atTwo and {atTwo} -> atThree. Sixteen vectors; if independent, S is total.
        ushort[] domain = [.. Enumerable.Range(0, 8).Select(i => (ushort)(1 << i)), .. atTwo];
        ushort[] image = [.. atTwo, .. atThree];

        ushort[]? table = ExtendLinear(domain, image);
        if (table is null) { output.WriteLine("the sixteen vectors are not independent — need deeper distances"); return; }
        output.WriteLine("S table = " + string.Join(" ", table.Select(v => v.ToString("X4"))));

        int ok = 0, bad = 0;
        foreach ((int distance, byte delta, ushort change) in observations)
        {
            ushort value = delta;
            for (int i = 1; i < distance; i++) value = ApplyLinear(table, value);
            if (value == change) ok++; else bad++;
        }
        output.WriteLine($"predicts {ok} of {ok + bad} observations" + (bad == 0 ? " — S IS RECOVERED" : ""));
    }

    /// <summary>
    /// The whole checksum, end to end: the recovered step, the initial value, and every measured sample.
    /// </summary>
    /// <remarks>
    /// With <c>crc = S(crc) ^ b</c> over the tail, a byte at distance d contributes <c>S^(d-1)(b)</c> and the
    /// leftover is <c>S^(L-1)(init)</c> — a constant per LENGTH, not per message. So the per-length constants
    /// must satisfy <c>c(L+1) = S(c(L))</c>, and that recurrence both recovers the initial value and checks
    /// the model: an arbitrary set of constants would fit any single length and fail across lengths.
    /// <para>
    /// The terminator needs no special handling. It is always <c>0x00</c> and every linear map sends zero to
    /// zero, so it contributes nothing wherever it sits.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_verify_the_whole_checksum()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        List<(int Distance, byte Delta, ushort Change)> observations = [.. Contributions()];
        ushort[] atTwo = SolveBasis(observations, 2)!;
        ushort[] atThree = SolveBasis(observations, 3)!;
        ushort[] step = ExtendLinear(
            [.. Enumerable.Range(0, 8).Select(i => (ushort)(1 << i)), .. atTwo], [.. atTwo, .. atThree])!;
        output.WriteLine("S table = " + string.Join(" ", step.Select(v => v.ToString("X4"))));

        List<(byte[] Discarded, ushort Sum)> dataset = [.. Dataset()];

        // The constant each length leaves behind, once every byte's contribution is accounted for.
        var constants = new SortedDictionary<int, HashSet<ushort>>();
        foreach ((byte[] tail, ushort sum) in dataset)
        {
            ushort accumulated = 0;
            // All but the terminator. Running it too would advance every other byte one step further, and the
            // measured contribution at distance d is S^(d-1), not S^d.
            foreach (byte b in tail[..^1]) accumulated = (ushort)(ApplyLinear(step, accumulated) ^ b);
            (constants.TryGetValue(tail.Length, out var set) ? set : constants[tail.Length] = [])
                .Add((ushort)(sum ^ accumulated));
        }

        int ambiguous = constants.Count(c => c.Value.Count != 1);
        output.WriteLine($"{constants.Count} lengths; {ambiguous} with more than one constant " +
                         (ambiguous == 0 ? "(the model holds within every length)" : "(THE MODEL IS WRONG)"));
        foreach ((int length, HashSet<ushort> values) in constants.Take(8))
            output.WriteLine($"  L={length,4} -> {string.Join("/", values.Select(v => v.ToString("X4")))}");

        // c(L+1) = S(c(L)) across adjacent lengths, which is what pins the initial value.
        int held = 0, broke = 0;
        foreach ((int length, HashSet<ushort> values) in constants)
        {
            if (values.Count != 1 || !constants.TryGetValue(length + 1, out var next) || next.Count != 1) continue;
            if (ApplyLinear(step, values.Single()) == next.Single()) held++; else broke++;
        }
        output.WriteLine($"recurrence c(L+1) = S(c(L)): {held} held, {broke} broke");

        (int shortest, HashSet<ushort> shortestValue) = constants.First();
        output.WriteLine($"constant at the shortest length ({shortest}B) = " +
                         string.Join("/", shortestValue.Select(v => v.ToString("X4"))));

        // Almost every length leaves 0000 behind, so the candidate is bare accumulation: no initial value and
        // no final XOR. Test exactly that, and show what disagrees rather than settling for "nearly all".
        int ok = 0;
        var failures = new List<string>();
        foreach ((byte[] tail, ushort sum) in dataset.DistinctBy(d => Convert.ToHexString(d.Discarded)))
        {
            ushort crc = 0;
            foreach (byte b in tail[..^1]) crc = (ushort)(ApplyLinear(step, crc) ^ b);
            if (crc == sum) { ok++; continue; }
            if (failures.Count < 10)
            {
                string hex = Convert.ToHexString(tail);
                failures.Add($"  want {sum:X4} got {crc:X4}  L={tail.Length,4}  " +
                             $"head={hex[..Math.Min(24, hex.Length)]} tail={hex[Math.Max(0, hex.Length - 48)..]}");
            }
        }
        int distinct = dataset.DistinctBy(d => Convert.ToHexString(d.Discarded)).Count();
        output.WriteLine($"bare accumulation reproduces {ok} of {distinct} distinct tails");
        foreach (string line in failures) output.WriteLine(line);
    }

    /// <summary>
    /// What happens to an inline word-sort record when its position no longer fits in a byte.
    /// </summary>
    /// <remarks>
    /// Every tail the checksum model fails on carries an inline section — <c>01 01 01 80 EF 06 84 00</c>, a
    /// hyphen near the end of a very long value. The record stores position as <c>0x07 + 4 x position</c> in
    /// ONE byte, so a hyphen at character 250 needs <c>0x3EF</c> and cannot fit. LibRed wraps it. If ACE does
    /// something else, LibRed's key past the truncation point is wrong for these values and the checksum was
    /// never the problem.
    /// <para>
    /// Deliberately tested BELOW the 510-byte limit, where the whole key is stored and can be compared
    /// directly, so the answer does not depend on the truncation being understood first.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_inline_position_overflow()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        (string source, string? created, ColumnDef column) = Fixture(0);   // v0: one byte per Latin character
        try
        {
            // A hyphen at increasing depth. 0x07 + 4 x position passes 0xFF at position 62, so these bracket
            // the overflow while every key stays well under the 510-byte cap.
            var samples = new List<string>();
            foreach (int at in (int[])[10, 40, 61, 62, 63, 80, 120, 200, 250])
                samples.Add(new string('a', at) + "-" + new string('a', 250 - at));

            Dictionary<string, string> ace = AceKeys(source, "inline", [.. samples]);
            foreach (string text in samples)
            {
                int at = text.IndexOf('-');
                if (!ace.TryGetValue(text, out string? stored)) { output.WriteLine($"  hyphen@{at,3}: not stored"); continue; }
                string ours = Convert.ToHexString(IndexKeyEncoder.EncodeWithoutLengthLimit([(column, true)], [text]));
                output.WriteLine(
                    $"  hyphen@{at,3} (0x07+4x{at} = 0x{0x07 + 4 * at:X3}): ACE …{stored[^12..]}  " +
                    (ours == stored ? "ours SAME" : $"ours …{ours[^12..]}"));
            }
        }
        finally { if (created is not null) TemporaryDatabase.Delete(created); }
    }

    /// <summary>
    /// The secondary slot a Han character occupies when something later in the string carries an accent.
    /// </summary>
    /// <remarks>
    /// The five tails the checksum still misses are all ones where an accented Latin character was
    /// substituted into a Han string, which makes the secondary section run the whole length instead of being
    /// omitted. What each Han character contributes to that section has never been measured: the BMP sweep
    /// encodes ONE character at a time, and a lone unaccented character emits no secondary section at all.
    /// Exactly the blind spot that hid the inline position bug.
    /// <para>
    /// Kept short so the whole key is stored and can be compared byte for byte.
    /// </para>
    /// </remarks>
    [Fact]
    public void Probe_secondary_slot_of_a_han_character()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_CHECKSUM") == "1",
            "set LIBRED_CHECKSUM=1 — this probe needs ACE");

        foreach (byte version in (byte[])[0, 1])
        {
            (string source, string? created, ColumnDef column) = Fixture(version);
            try
            {
                string[] samples =
                [
                    "一á", "一二á", "一二三á", "á一", "一á一",
                    "一ä", "一a", "aá", "a一á", "ㄱá", "가á", "あá",
                ];
                Dictionary<string, string> ace = AceKeys(source, "sec", samples);
                output.WriteLine($"--- v{version}");
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? stored)) { output.WriteLine($"  {Describe(text),-20} not stored"); continue; }
                    string ours;
                    try { ours = Convert.ToHexString(IndexKeyEncoder.EncodeWithoutLengthLimit([(column, true)], [text])); }
                    catch (NotSupportedException e) { ours = $"(refused: {e.Message[..Math.Min(30, e.Message.Length)]})"; }
                    output.WriteLine($"  {Describe(text),-20} ACE {stored,-34} {(ours == stored ? "SAME" : $"ours {ours}")}");
                }
            }
            finally { if (created is not null) TemporaryDatabase.Delete(created); }
        }
    }

    private static string Describe(string s) => string.Concat(s.Select(c => $"U+{(int)c:X4}"));

    /// <summary>The contribution of each single input bit at one distance, solved from arbitrary deltas.</summary>
    private static ushort[]? SolveBasis(List<(int Distance, byte Delta, ushort Change)> observations, int distance)
    {
        var rows = observations.Where(o => o.Distance == distance)
                               .Select(o => (Mask: (int)o.Delta, Value: o.Change)).ToList();
        var pivots = new (int Mask, ushort Value)?[8];
        foreach ((int mask, ushort value) in rows)
        {
            int m = mask;
            ushort v = value;
            for (int bit = 0; bit < 8 && m != 0; bit++)
            {
                if ((m & (1 << bit)) == 0) continue;
                if (pivots[bit] is null) { pivots[bit] = (m, v); break; }
                m ^= pivots[bit]!.Value.Mask;
                v ^= pivots[bit]!.Value.Value;
            }
        }
        if (pivots.Any(p => p is null)) return null;

        // Back-substitute so each pivot carries a single bit.
        for (int bit = 7; bit >= 0; bit--)
        for (int higher = bit + 1; higher < 8; higher++)
            if ((pivots[bit]!.Value.Mask & (1 << higher)) != 0)
                pivots[bit] = (pivots[bit]!.Value.Mask ^ pivots[higher]!.Value.Mask,
                               (ushort)(pivots[bit]!.Value.Value ^ pivots[higher]!.Value.Value));

        return pivots.Any(p => p!.Value.Mask != (1 << Array.IndexOf(pivots, p)))
            ? [.. pivots.Select(p => p!.Value.Value)]
            : [.. pivots.Select(p => p!.Value.Value)];
    }

    /// <summary>The per-bit table of a linear map given its action on sixteen independent vectors.</summary>
    private static ushort[]? ExtendLinear(ushort[] domain, ushort[] image)
    {
        var pivots = new (ushort Vector, ushort Image)?[16];
        for (int i = 0; i < domain.Length; i++)
        {
            ushort v = domain[i], img = image[i];
            for (int bit = 15; bit >= 0 && v != 0; bit--)
            {
                if ((v & (1 << bit)) == 0) continue;
                if (pivots[bit] is null) { pivots[bit] = (v, img); break; }
                v ^= pivots[bit]!.Value.Vector;
                img ^= pivots[bit]!.Value.Image;
            }
        }
        if (pivots.Any(p => p is null)) return null;

        var table = new ushort[16];
        for (int j = 0; j < 16; j++)
        {
            ushort v = (ushort)(1 << j), img = 0;
            for (int bit = 15; bit >= 0 && v != 0; bit--)
            {
                if ((v & (1 << bit)) == 0) continue;
                v ^= pivots[bit]!.Value.Vector;
                img ^= pivots[bit]!.Value.Image;
            }
            if (v != 0) return null;
            table[j] = img;
        }
        return table;
    }

    private static ushort ApplyLinear(ushort[] table, ushort value)
    {
        ushort result = 0;
        for (int bit = 0; bit < 16; bit++) if ((value & (1 << bit)) != 0) result ^= table[bit];
        return result;
    }

    /// <summary>The measured contribution of a single byte delta at a given distance from the end.</summary>
    private static IEnumerable<(int Distance, byte Delta, ushort Change)> Contributions()
    {
        var samples = Dataset().DistinctBy(d => Convert.ToHexString(d.Discarded)).ToList();
        var seen = new HashSet<(int, byte)>();
        foreach (var group in samples.GroupBy(s => s.Discarded.Length))
        {
            var members = group.ToList();
            for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++)
            {
                byte[] a = members[i].Discarded, b = members[j].Discarded;
                int at = -1;
                bool single = true;
                for (int k = 0; k < a.Length; k++)
                    if (a[k] != b[k]) { if (at >= 0) { single = false; break; } at = k; }
                if (!single || at < 0) continue;

                int distance = a.Length - 1 - at;
                byte delta = (byte)(a[at] ^ b[at]);
                if (seen.Add((distance, delta)))
                    yield return (distance, delta, (ushort)(members[i].Sum ^ members[j].Sum));
            }
        }
    }

    private static IEnumerable<(byte[] Discarded, ushort Sum)> Dataset()
    {
        foreach (byte version in (byte[])[0, 1])
        {
            (string source, string? created, ColumnDef column) = Fixture(version);
            try
            {
                string[] samples = [.. SearchSamples()];
                Dictionary<string, string> ace = AceKeys(source, "chkpoly", samples);
                foreach (string text in samples)
                {
                    if (!ace.TryGetValue(text, out string? stored)) continue;
                    byte[] aceKey = Convert.FromHexString(stored);
                    if (aceKey.Length != 510) continue;
                    byte[] full = IndexKeyEncoder.EncodeWithoutLengthLimit([(column, true)], [text]);
                    yield return (full[508..], (ushort)((aceKey[508] << 8) | aceKey[509]));
                }
            }
            finally { if (created is not null) TemporaryDatabase.Delete(created); }
        }
    }

    /// <summary>
    /// Standard 16-bit checksums, plus the simple accumulators that often turn out to be the answer.
    /// </summary>
    private static IEnumerable<(string Name, Func<byte[], ushort> Fn)> Candidates()
    {
        (string Name, ushort Poly, ushort Init, bool RefIn, bool RefOut, ushort XorOut)[] crcs =
        [
            ("CRC-16/CCITT-FALSE", 0x1021, 0xFFFF, false, false, 0x0000),
            ("CRC-16/XMODEM", 0x1021, 0x0000, false, false, 0x0000),
            ("CRC-16/KERMIT", 0x1021, 0x0000, true, true, 0x0000),
            ("CRC-16/GENIBUS", 0x1021, 0xFFFF, false, false, 0xFFFF),
            ("CRC-16/MCRF4XX", 0x1021, 0xFFFF, true, true, 0x0000),
            ("CRC-16/X-25", 0x1021, 0xFFFF, true, true, 0xFFFF),
            ("CRC-16/ARC", 0x8005, 0x0000, true, true, 0x0000),
            ("CRC-16/MODBUS", 0x8005, 0xFFFF, true, true, 0x0000),
            ("CRC-16/USB", 0x8005, 0xFFFF, true, true, 0xFFFF),
            ("CRC-16/MAXIM", 0x8005, 0x0000, true, true, 0xFFFF),
            ("CRC-16/UMTS", 0x8005, 0x0000, false, false, 0x0000),
            ("CRC-16/DDS-110", 0x8005, 0x800D, false, false, 0x0000),
            ("CRC-16/DECT-R", 0x0589, 0x0000, false, false, 0x0001),
            ("CRC-16/DNP", 0x3D65, 0x0000, true, true, 0xFFFF),
            ("CRC-16/EN-13757", 0x3D65, 0x0000, false, false, 0xFFFF),
            ("CRC-16/T10-DIF", 0x8BB7, 0x0000, false, false, 0x0000),
            ("CRC-16/CDMA2000", 0xC867, 0xFFFF, false, false, 0x0000),
        ];
        foreach ((string name, ushort poly, ushort init, bool refIn, bool refOut, ushort xorOut) in crcs)
            yield return (name, data => Crc16(data, poly, init, refIn, refOut, xorOut));

        yield return ("sum16", data => { ushort s = 0; foreach (byte b in data) s += b; return s; });
        yield return ("sum16 words LE", data =>
        {
            ushort s = 0;
            for (int i = 0; i + 1 < data.Length; i += 2) s += (ushort)(data[i] | (data[i + 1] << 8));
            return s;
        });
        yield return ("xor16 words LE", data =>
        {
            ushort s = 0;
            for (int i = 0; i + 1 < data.Length; i += 2) s ^= (ushort)(data[i] | (data[i + 1] << 8));
            return s;
        });
        yield return ("fletcher16", data =>
        {
            byte a = 0, b = 0;
            foreach (byte x in data) { a = (byte)((a + x) % 255); b = (byte)((b + a) % 255); }
            return (ushort)((b << 8) | a);
        });
        yield return ("adler16", data =>
        {
            ushort a = 1, b = 0;
            foreach (byte x in data) { a = (ushort)((a + x) % 251); b = (ushort)((b + a) % 251); }
            return (ushort)((b << 8) | a);
        });
        yield return ("bsd16", data =>
        {
            ushort s = 0;
            foreach (byte x in data) { s = (ushort)((s >> 1) | (s << 15)); s += x; }
            return s;
        });
    }

    private static ushort Crc16(byte[] data, ushort poly, ushort init, bool refIn, bool refOut, ushort xorOut)
    {
        ushort crc = init;
        foreach (byte raw in data)
        {
            byte b = refIn ? Reverse(raw) : raw;
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ poly) : (ushort)(crc << 1);
        }
        if (refOut) crc = Reverse16(crc);
        return (ushort)(crc ^ xorOut);

        static byte Reverse(byte value)
        {
            int result = 0;
            for (int i = 0; i < 8; i++) { result = (result << 1) | (value & 1); value >>= 1; }
            return (byte)result;
        }

        static ushort Reverse16(ushort value)
        {
            int result = 0;
            for (int i = 0; i < 16; i++) { result = (result << 1) | (value & 1); value >>= 1; }
            return (ushort)result;
        }
    }

    /// <summary>
    /// Values whose discarded tail varies widely: the character past the cut is swept across many code
    /// points, so the tails differ in content and in length rather than by one bit in one place.
    /// </summary>
    private static IEnumerable<string> SearchSamples()
    {
        // Vary the LAST TWO characters together, over a wide spread of code points. Varying one position
        // moves one byte of the tail and constrains the polynomial barely at all; varying two, across scripts
        // that weigh differently, moves several bytes at once and spans a far larger space of deltas.
        // Solving for a bit basis at one distance needs eight LINEARLY INDEPENDENT deltas there, and the
        // deltas are whatever the weights happen to XOR to. Twenty code points spanned distance 2 and fell
        // short at 3, so this sweeps far more of them, and varies each of the last five positions so every
        // shallow distance gets its own spread rather than only the final byte moving.
        int[] tailPoints =
        [
            0x4E00, 0x4E8C, 0x4E09, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x5341,
            0x4E0A, 0x4E0B, 0x5927, 0x5C0F, 0x4EBA, 0x5929, 0x5730, 0x65E5, 0x6708, 0x5C71,
            0x5DDD, 0x7530, 0x4E2D, 0x738B, 0x77F3, 0x672C, 0x6728, 0x706B, 0x6C34, 0x91D1,
            0x571F, 0x767D, 0x9752, 0x8D64, 0x9ED2, 0x5317, 0x5357, 0x6771, 0x897F, 0x4EAC,
            0x0061, 0x0062, 0x007A, 0x0041, 0x00E1, 0x00FC, 0x0391, 0x03B1, 0x0410, 0x0430,
            0x05D0, 0x0623, 0x3042, 0x30A2, 0x0E01, 0xFF21, 0x0100, 0x0180, 0x1E00, 0x2010,
        ];

        foreach (int point in tailPoints)
            for (int at = 250; at <= 254; at++)
            {
                char[] chars = new string('一', 255).ToCharArray();
                chars[at] = (char)point;
                yield return new string(chars);
            }

        // The same in the Latin range, where each character is cheaper and the cut falls elsewhere, giving
        // tails of a different LENGTH — needed because the initial value's contribution is length-dependent.
        foreach (int last in tailPoints)
        {
            char[] chars = new string('a', 255).ToCharArray();
            chars[254] = (char)last;
            yield return new string(chars);

            chars = new string('a', 255).ToCharArray();
            chars[252] = (char)last;
            chars[254] = (char)last;
            yield return new string(chars);
        }

        // A third weight-per-character, for a third tail length.
        foreach (int last in tailPoints)
        {
            char[] chars = new string('á', 255).ToCharArray();
            chars[254] = (char)last;
            yield return new string(chars);
        }
    }

    /// <summary>
    /// Values chosen so the discarded tail varies in controlled ways: same length with one character changed
    /// at different depths, and different weights per character so the cut lands in different places.
    /// </summary>
    private static List<string> Samples()
    {
        var samples = new List<string>
        {
            new('a', 255),
            new('一', 255),
            new('á', 255),
        };
        // One character changed at increasing depth — everything before the change is identical, so the
        // surviving prefix is identical too and only the discarded part differs.
        foreach (int at in (int[])[0, 100, 200, 250, 254])
        {
            char[] chars = new string('一', 255).ToCharArray();
            chars[at] = '二';
            samples.Add(new string(chars));
        }
        // Same, in the Latin range, where each character is cheaper and the cut lands later.
        foreach (int at in (int[])[0, 100, 240, 250, 254])
        {
            char[] chars = new string('a', 255).ToCharArray();
            chars[at] = 'b';
            samples.Add(new string(chars));
        }
        return samples;
    }

    private static string Label(string text)
    {
        int differs = -1;
        for (int i = 1; i < text.Length; i++) if (text[i] != text[0]) { differs = i; break; }
        return differs < 0
            ? $"{text.Length}x U+{(int)text[0]:X4}"
            : $"{text.Length}x U+{(int)text[0]:X4} @{differs}=U+{(int)text[differs]:X4}";
    }

    private static (string Source, string? Created, ColumnDef Column) Fixture(byte version)
    {
        bool v1 = version == Collation.GeneralVersion;
        string? created = null;
        if (v1)
        {
            created = TemporaryDatabase.CreatePath("general-v1-chk-");
            DatabaseCreator.CreateEmpty(created, collation: Collation.General);
        }

        return (created ?? TestDatabases.NorthwindAccdb, created, new ColumnDef
        {
            Name = "K", Type = JetDataType.Text, Index = 0,
            Collation = v1 ? Collation.General : Collation.GeneralLegacy,
        });
    }

    private static Dictionary<string, string> AceKeys(string source, string tag, string[] samples)
    {
        string path = TemporaryDatabase.CopyPath(source, tag);
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE Probe (K TEXT(255), V LONG)");
                Exec(connection, "CREATE INDEX IX_Probe ON Probe (K)");
                for (int i = 0; i < samples.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO Probe (K, V) VALUES (?, ?)";
                    insert.Parameters.AddWithValue("k", samples[i]);
                    insert.Parameters.AddWithValue("v", i);
                    try { insert.ExecuteNonQuery(); } catch (Exception) { /* ACE refused this value */ }
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("Probe");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_Probe");
            ColumnDef keyColumn = table.Definition.FindColumn("K")!;
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            var keys = new Dictionary<string, string>();
            foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
                if (rows.TryGetValue(rowId, out object?[]? values) && values[keyColumn.Index] is string text)
                    keys[text] = Convert.ToHexString(stored);
            return keys;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(System.Data.OleDb.OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
