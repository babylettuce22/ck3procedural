Magic — the static half of the procedural magic system.
=======================================================

This set is the part of magic that is the same in every world. Nothing in it decides what kind of
magic a world has; it is the plumbing every kind needs, so that the generated half only has to
write what is actually different about this seed.

Shipped when MapConfig.EnableMagic is true. See Emit/StaticFileWriter.cs for how sets work.

Nothing here references a generated key — no culture, faith, title, trait or spell — so it is safe
to ship with any seed, in the same way the Wilderness set is.


TWO KEYS, NOT ONE
-----------------

Shipping these files does NOT turn magic on. There are two independent switches:

    EnableMagic (the tool)   ships this folder into the mod.
    gen_magic_active (world) says this particular world actually has magic in it.

The second is a global variable, and NOTHING IN THIS SET SETS IT. It is set by the generated half,
by calling gen_magic_activate_effect, and a world that rolled Prevalence = Absent never calls it.

That split is what makes this folder safe to ship before any of the generated half exists — which
is exactly the state it is in today. Every gate below reads gen_magic_active first, so with no
generated content present the whole system is inert: no decision is shown, no widget is drawn, no
pulse does any work beyond one variable check.


THE GATE
--------

The requirement this set exists to enforce: a character who is not a practitioner must see no sign
of the system at all. One trigger answers that, and everything the generated half emits must ask it
before showing anything:

    gen_magic_practitioner_trigger        script side — decisions, interactions, events
    gen_magic_ui (scripted_gui)           GUI side — .gui widgets, via GetScriptedGui('gen_magic_ui').IsShown

Both resolve to the same condition. The scripted_gui exists because a .gui file cannot call a
scripted trigger; is_shown is the only bridge, and it is the pattern vanilla uses itself (see
common/scripted_guis/knight_permissions_sguis.txt).


THE CONTRACT WITH THE GENERATED HALF
------------------------------------

The generated half is expected to:

  1. Call gen_magic_activate_effect once, on on_game_start_after_lobby, passing the world's fuel
     kind and ledger decay rate. A world without magic simply never calls it.

  2. Mark practitioners with gen_magic_grant_effect rather than by writing the variables directly,
     so that everyone who is in the system is initialised the same way.

  3. Route every cast through gen_magic_cast_effect, so that spending, the ledger and the seams
     below all happen in one place and cannot be forgotten one spell at a time.

  4. Declare the on_action seams it wants. This set FIRES them; it does not declare them, because
     an on_action needs exactly one effect block across all files and that block belongs to the
     half that knows what should happen. Firing an on_action nobody declared is unreachable here:
     every call site is behind gen_magic_active, which only the generated half can turn on.

         gen_magic_cast_on_action                 after any cast          root = caster
         gen_magic_threshold_on_action            after the ledger moves  root = caster
         gen_magic_practitioner_year_on_action    yearly, per caster      root = practitioner
         gen_magic_practitioner_death_on_action   a practitioner dies     root = the dead


THE VARIABLE NAMESPACE
----------------------

Global:
    gen_magic_active            exists = this world has magic
    gen_magic_fuel              flag: which resource practitioners spend
    gen_magic_pressure          the ledger — how much magic has been worked in this world
    gen_magic_pressure_decay    how much of it drains per year

Character:
    gen_magic_rank              0/absent = not a practitioner, 1+ = rank on the ladder
    gen_magic_charge            fuel in hand
    gen_magic_debt              fuel spent that was not in hand
    gen_magic_exposure          how publicly known this character's practice is

Anything the generated half adds should stay inside the gen_magic_ prefix so that a save can be
read, and a bug traced, without knowing which seed produced it.


WHAT IS DELIBERATELY NOT HERE
-----------------------------

No traits, no decisions, no modifiers, no localisation. All four are per-world: the rank ladder is
named differently in every world, the spells differ in kind and not only in number, and a trait
shipped here would show up in the ruler designer of a world that has no magic in it.

The one thing that looks like it belongs here and does not is the fuel *policy* — how much a cast
costs, how fast charge returns. This set implements the accounting; the numbers come from the
generated half, which reads them off the rolled cosmology.
