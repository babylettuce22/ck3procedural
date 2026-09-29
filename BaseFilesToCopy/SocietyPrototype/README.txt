Societies -- the static prototype for the society system.

WHAT THIS SET IS FOR
--------------------
One society, hand-written, with nothing generated about it. It exists to answer a single
question ahead of the generator: what does a "rite" actually look like in CK3, and can
membership of a society gate one end to end -- who sees it, who may perform it, and who may
be invited to it?

Everything here is deliberately named `society_*` rather than `gen_society_*`. Nothing in
this folder is written by an emitter and nothing here should ever be: when the generator
learns to write societies it will write its own keys (`gen_cult_member`, `gen_cult_rite_*`
and so on) into the mod directly, and this set becomes the reference implementation those
files are modelled on rather than a dependency of them.

WHAT IS IN IT
-------------
  common/traits/00_society_traits.txt                              membership, and the ladder
  common/activities/activity_types/00_society_rite_activity.txt    the rite
  common/activities/activity_types/00_society_errand_activity.txt  the errand (CK2 missions)
  events/society_errand_events.txt                                 the errand's four-beat chain
  common/activities/activity_group_types/00_society_activity_groups.txt  its planner category
  common/activities/intents/00_society_intents.txt                 why anyone attends
  common/activities/guest_invite_rules/00_society_invite_rules.txt who the planner offers
  common/character_interactions/00_society_interactions.txt        the recruiter's half
  common/character_interactions/00_society_powers.txt              the powers (rank 1)
  common/deathreasons/00_society_deaths.txt                        what a sacrifice reads as
  common/schemes/scheme_types/00_society_abduct_scheme.txt          taking somebody
  common/character_interaction_categories/01_society_interaction_category.txt  its menu header
  common/secret_types/00_society_secrets.txt                       being findable
  common/modifiers/00_society_modifiers.txt                        suspicion, and being known
  common/opinion_modifiers/00_society_opinions.txt                 what refusing costs
  common/scripted_guis/00_society_panel_guis.txt                   what the panel may ask
  gui/gen_society_panel.gui                                        the panel
  gui/scripted_widgets/gen_society_panel.txt                       what instantiates it
  gfx/interface/skinned/hud_maintab/maintab_gen_society.dds        the tab icon
  events/society_events.txt                                        the approach, and the rite
  localization/english/society_l_english.yml                       every string the above needs

THE PANEL
---------
CK2's society screen showed four things at once: which society you were in, what rank you held,
how much of its currency you had, and the FULL list of its powers with the ones above your rank
greyed rather than hidden. The last is what made the ladder mean anything, so this panel draws
all five rungs always and lights the one you hold.

One door: the HUD tab under Intrigue. It sets `society_panel_open`, the tab lights from it, the
window's X clears it, and Escape clears it through the X.

There was a second -- a "Take Stock of the Society" decision -- kept as a fallback for the window in
which the tab did not exist. It is gone. A decisions-panel entry that opens the same window as a
button four pixels away is a duplicate, not a fallback.

THE TAB IS THE ONE PIECE OF THIS FEATURE THAT IS NOT IN THIS FOLDER. It edits vanilla's
gui/hud.gui, which only the generator can do -- Emit/GuiWriter.cs PatchHudTabs, gated on
MapConfig.EnableSocieties. So a `--static-only --societies` run ships everything here and no tab,
because that mode never reaches GuiWriter; use `--gui-only --societies` to add it, or a full run.
Shipping the set without ever running GuiWriter now means a panel with no way to open it.

Why the tab is not a game view: vanilla's tabs call `ToggleGameViewData('intrigue_window', ...)`,
and those 43 view names are registered in the ENGINE. Nothing under common/ defines them and each
has a C++ data context behind it, so a mod cannot add one, and a tab naming an unknown view
resolves to nothing without logging. The society tab drives the society_panel_toggle scripted_gui
instead. What that costs: the panel does not close when another view opens, and the engine does
not remember its position. What it does not cost: placement, art, the lit state, or Escape --
`close_window` is an ordinary widget attribute, not a privilege of engine views.

Both meters are real and both now terminate. Dark Power is CK2's demon-worshipper currency: it
arrives from the rite and from sacrifice, and abduction spends it. Visibility is CK2's exposure
mechanic -- +5 when somebody accepts the oath, +15 when they refuse, on the theory that a person
who said no is a person who knows and owes you nothing.

Visibility does NOT feed a discovery chance, which an earlier note here promised and which would
have been the wrong mechanic. See EXPOSURE below: it decides whether a secret exists for the
Spymaster to find, and the finding is vanilla's.

THE THREE GATES
---------------
The activity is where the work is, and the reason it is worth reading is that CK3 puts the
gating in three different places:

  is_shown                        a non-member does not see the rite in the planner at all
  can_start_showing_failures_only a member who cannot hold one now is told why
  can_be_activity_guest           the engine's per-candidate veto on every proposed guest

and a fourth thing that behaves like a gate but is not one -- `guest_invite_rules`, which
BUILDS the offered list rather than filtering it, and therefore cannot be the only defence.

WHAT HAPPENS AT A RITE
----------------------
  society.0100  The Convening   the host chooses what the fortnight is spent on
  society.0101  The Room        every guest, once, on arrival
  society.0110  The Reckoning   who was asked and did not come
  society.0111  The Elevation   somebody is raised, or the host raises themselves

0100's three answers -- discipline, patronage, restraint -- are the triad a generated society
will inherit, with its targets and flavour read off its host faith rather than hardcoded. The
purpose is chosen in an event rather than in the activity's own `options` block, which is where
it eventually belongs: options are picked at planning time and show in the planner, so the host
would commit before travelling. That is the better mechanic and deliberately not the first one.

Restraint is not a null choice. A secret society's safest move is to disperse having done
nothing, and when membership becomes a secret that is the branch nobody outside ever hears
about -- the other two each leave somebody with a reason to talk.

HOW TO TEST IT
--------------
  0. In the console:  event society.9001
     Swears the player and every adult courtier into the society at staggered Standing, and
     toasts how many were sworn. A zero means the court is empty or all children, which is a
     different fault from the guest list being broken. Everything below can be done without it,
     one courtier at a time, which is why it exists.
  0b. In the console: event society.9003
     Grants 100 Dark Power, which is exactly the cost of one abduction. Nothing else in the set
     grants any except the rite and the sacrifice, so without this, testing a power that SPENDS
     meant arranging two or three killings first.
  1. In the console:  event society.0001
     Somebody in your court turns out to have been a member the whole time and makes the
     offer. Accept, and you hold the Society Member trait with Standing at 10.
  2. Inspect the courtier who approached you -- they are a member too, at 40, which puts them
     on the second rung. That is two members, which is the minimum a rite needs.
  3. "Hold the Rite" is now in your activity planner. It is NOT in the planner of any
     character without the trait -- switch to one and confirm, because that is the property
     the whole set exists to demonstrate.
  4. Open the guest list. Two tabs -- "Sworn in Our Court" and "Sworn Elsewhere" -- both arriving
     UNTICKED. Tick them and they fill with members and nobody else, however large your court is.
     They were one rule under `defaults` at first, which pre-ticked it and made the guest list
     look like something that filled itself in; see the note above guest_invite_rules for why
     `defaults` is the wrong key for a rite.
  5. Start it, then complete it. Every attendee gains 8 Standing; the host gains 20.
  6. Open the panel from the tab under Intrigue.
     It should name your rung, light that one row of the five, show both meters, mark the rite
     Available or "Standing 50", and list every member it can see with their own rung beside them
     -- each row's rung asked of THAT character, not of you.
  7. Check the tab behaves: it is absent entirely for a non-member, lit while the panel is up,
     unlit after closing with the X or with Escape, and it toggles rather than only opening.

If the rite is greyed for want of Standing, press society.9002 twice. It is +25 a press, which
walks the breakpoints one at a time so the modifiers arriving and the tooltip turning green are
both visible; a single jump to the top would show neither.

Use "Offer the Oath" on any other courtier to grow the membership -- it fires the same
society.0001 at them with you as the recruiter, so there is one set of odds in the set rather
than two that can drift apart. The interaction is auto_accept and the decision is taken inside
the event by the person asked, so without something reporting back, the button appears to do
nothing -- which is exactly how it was first reported. That report is now an event rather than a
toast: society.0002 on acceptance, society.0003 on refusal.

society.0003 is the one worth pressing twice. A refusal leaves somebody walking around knowing
what they were asked, and it is the only place in the set where the player is asked what to do
about that -- pay to make the memory convenient, recovering 10 of the 15 exposure, or let them
carry it. Both options charge the opinion hit and the full +15 first, via
society_oath_refused_effect, so the tooltips show the whole sum rather than half of it.

EXPOSURE, AND WHAT VISIBILITY IS FOR
------------------------------------
Visibility used to be a number that went up and never did anything. It now terminates:

  15   society_under_suspicion    -0.5 prestige, and a SECRET is created
  25   society_highly_suspect     -1 prestige, -1 piety, and the liege is told
  40   independent rulers stop denying it, once

The secret is the point. CK2's danger was not a discovery roll -- it was the Court Chaplain
running a JOB, hunting for people already carrying the mark. CK3 has that job: the Spymaster's
task_find_secrets. So the meter does not roll against anything; it decides whether there is
anything on the board for that job to find. Being found is entirely vanilla's, and society.0300
to 0302 are what arrives afterwards.

society.0301 is where the roster finally costs something. Everything else in this set makes
membership an asset -- a guest list, an agent pool, a rank ladder. Here a member is offered
another member as the price of their own skin, and the one who is sold is never told by whom.

Decay below 15 removes the secret again, matching the modifiers. That is deliberate and departs
from CK2, whose marks were permanent: we SHOW the number, so a panel reading Exposure 4 beside a
findable secret is a panel the player stops trusting.

WHAT IS DELIBERATELY MISSING
----------------------------
No secret type, so nothing about this is actually secret yet -- a non-member cannot be in the
room, but nor can they discover who was. That is the next piece, and it is where CK3 gives
the most for free: vanilla's `secret_witch` already has discovery, blackmail hooks and
exposure, and a society membership secret is the same shape.

No cost on the rite, and nothing that SPENDS either meter. Dark Power and Visibility both
accumulate and neither is ever consumed, so they are honest counters rather than an economy.
The rank gate exists now -- the rite needs Standing 50, which is the second breakpoint -- and
society.9002 is the bootstrapping answer to it, since the rite is the only thing that grants
Standing and you cannot hold one until you have some.

No on_action. The approach fires by hand from the console today; eventually it rolls yearly
against the traits the society recruits for, which is one small file and no change to
anything here.
