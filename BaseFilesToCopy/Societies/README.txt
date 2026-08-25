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
  common/activities/intents/00_society_intents.txt                 why anyone attends
  common/activities/guest_invite_rules/00_society_invite_rules.txt who the planner offers
  common/character_interactions/00_society_interactions.txt        the recruiter's half
  common/opinion_modifiers/00_society_opinions.txt                 what refusing costs
  events/society_events.txt                                        the approach, and the rite
  localization/english/society_l_english.yml                       every string the above needs

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
  1. In the console:  event society.0001
     Somebody in your court turns out to have been a member the whole time and makes the
     offer. Accept, and you hold the Society Member trait with Standing at 10.
  2. Inspect the courtier who approached you -- they are a member too, at 40, which puts them
     on the second rung. That is two members, which is the minimum a rite needs.
  3. "Hold the Rite" is now in your activity planner. It is NOT in the planner of any
     character without the trait -- switch to one and confirm, because that is the property
     the whole set exists to demonstrate.
  4. Open the guest list. The "Sworn Members" tab arrives UNTICKED -- tick it, and it fills with
     members and nobody else, however large your court is. It was `defaults` at first, which
     pre-ticked it and made the guest list look like something that filled itself in; see the
     note above guest_invite_rules for why that is the wrong key for a rite.
  5. Start it, then complete it. Every attendee gains 8 Standing; the host gains 20.

Use "Offer the Oath" on any other courtier to grow the membership -- it fires the same
society.0001 at them with you as the recruiter, so there is one set of odds in the set rather
than two that can drift apart. The interaction is auto_accept and the decision is taken inside
the event by the person asked, so the two toasts in society.0001's options are the only report
the recruiter gets: "<name> Is Sworn" or "<name> Refused". Without them the button appears to do
nothing, which is exactly how it was first reported.

WHAT IS DELIBERATELY MISSING
----------------------------
No secret type, so nothing about this is actually secret yet -- a non-member cannot be in the
room, but nor can they discover who was. That is the next piece, and it is where CK3 gives
the most for free: vanilla's `secret_witch` already has discovery, blackmail hooks and
exposure, and a society membership secret is the same shape.

No currency, no rank gates, and no cost on the rite. All three are one line each in the
activity, and they are left out so that a misbehaving rite is unambiguously the membership
plumbing's fault rather than a new economy's.

No on_action. The approach fires by hand from the console today; eventually it rolls yearly
against the traits the society recruits for, which is one small file and no change to
anything here.
