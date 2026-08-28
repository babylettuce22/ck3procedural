# Applied: axe head socket correction

Addendum to the original `axe_parts_handoff.md` (which is no longer in this folder — presumably
passed on). Re-exported `axe_parts.mesh` 2026-08-26, 1,676,435 bytes.

**You were right, and my original handoff was wrong about it.** Section 3 of that document
described the head sockets' large negative Z as "correct and expected: an axe head hangs off one
side of the haft, so the centre of the head/haft join cross-section is well off the haft's own
axis." The observation was accurate; the conclusion was not. The cross-section I was taking was
the wrong part's.

## What was wrong

The socket placement sampled the **terminating** part's cross-section (the head) instead of the
**axial** part's (the haft). For swords those coincide, because a grip and a guard are both
centred on the weapon axis. For an axe head they do not.

## What changed

Only the lateral component — Blender `z` — on both members of each `haft↔head` pair. `x` and `y`
untouched. Cap sockets untouched.

I used **each haft's axis at the join height**, not its global bbox centre. On the curved hafts
those differ by up to 0.56 units (african 0.56, northern 0.35, chinese 0.34, western 0.27), so
the targets below differ slightly from the ones in your table. Your note said the rule matters
more than the numbers, and the rule says "the anchor's axis at the join height".

| family | z was | z now | moved | your target | delta |
|---|---|---|---|---|---|
| ep1_african_axe_01_a | −1.4593 | 0.0586 | 1.518 | −0.5050 | 0.564 |
| ep1_indian_axe_01_a | −3.2859 | 0.0000 | 3.286 | 0.0000 | 0.000 |
| ep1_mena_axe_01_a | −2.1790 | 0.5968 | 2.776 | 0.5968 | 0.000 |
| tgp_chinese_axe_01_a | −6.8623 | 0.3960 | 7.258 | 0.0527 | 0.343 |
| ep1_mediterranean_axe_01_a | −8.4328 | 0.4616 | 8.894 | 0.3793 | 0.082 |
| ep1_western_axe_01_a | −9.4717 | −0.1439 | 9.328 | 0.1217 | −0.266 |
| ep1_steppe_axe_01_a | −11.7989 | −0.1529 | 11.646 | −0.1529 | 0.000 |
| ep1_northern_axe_01_a | −14.3858 | 0.9101 | 15.296 | 1.2561 | −0.346 |

Say the word if you would rather have the flat bbox-centre values for consistency with something
on your side; it is a one-line change.

## Verified after

1. Self-rebuild exact for all 8 families — drift 0.0, read back from the file.
2. Every `haft↔head` socket now **≤0.064** from its haft axis (was 0.95–15.64).
3. Mating pairs still bit-identical as stored (both written from one source value, so they round
   to the same float32 and the translation stays exactly zero).
4. 20 shapes / 24 locators / textures / UV2 all unchanged.

## The check is now a script, and it clears all three libraries

`scratchpad/socket_axis_check.py` — reads a `.mesh` directly, no Blender.

    swords   worst off-axis 0.088   CLEAN
    daggers  worst off-axis 0.486   CLEAN
    axes     worst off-axis 1.113   CLEAN

So the defect was unique to axes, which is what we'd expect.

Two things the checker had to get right, both of which bit me while writing it — worth knowing if
you reimplement it:

* **Pick the axial member by smallest lateral bbox extent, not by longest span.**
  `ep1_african_axe_01_a`'s head has a long socket collar that makes it span *further* along the
  axis than its own haft, so "longest wins" picks the head and the check inverts.
* **Sample the axial member past the bevel**, using the same ≥90%-of-max-extent rule that placed
  the socket. Sampling at the raw join plane threw a false FLAG on
  `ep1_western_sword_04_a grip<->guard`: the grip's slice there is 8 points wide with its centre
  1.62 off, but 0.02 further in it is 48 points and lands exactly on the socket. I chased that as
  a real defect before proving it was the checker.

## Standing doc updated

`for_future_parts.txt` section 4 now carries the corrected centring rule with the axe
failure as the worked example, and a new section 9B/3B mandating the socket-axis check — with
the explicit warning that self-rebuild cannot catch this class of error, because both sides of
the pair are wrong by the same amount and drift stays exactly 0.0.
