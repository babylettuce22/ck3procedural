Societies -- the Restorationists.

One secret society per world, sworn to put a fallen crown back on the head of the house that lost
it. The first fully built society, and the pattern later ones (a hidden faith, an inversion cult)
are meant to follow: a LOYALTY that never changes, and AIMS that change with the state of the world.

The hand-written prototype this grew out of is in ../SocietyPrototype and ships only with
--society-prototype. It is the reference for the CK3 mechanisms it proved; nothing here depends on it.


WHAT IS GENERATED AND WHAT IS NOT
---------------------------------
Generated per world (MapGen/Societies/Restoration.cs + Emit/Societies/RestorationWriter.cs):

  the crown         a de jure kingdom that fell, of one of two kinds:
                      BROKEN   nobody holds it at the start; the formation shows a realm that once
                               held most of it. It must be re-made (restor_restore_the_crown_decision).
                      USURPED  another house holds it at the start; the formation shows a different
                               realm held it first. It must be taken (Raise the Standard claims it,
                               vanilla's claim war does the rest).
                    Broken is preferred, but on most worlds the crowns that really fell were taken
                    whole, and the kingdoms nobody holds are the ones nobody ever united. A kingdom
                    with no recorded fall is used only when no recorded one exists.
  the fall          its year and manner (absorbed / collapsed / fragmented), from the formation's
                    frames and event log. $restor_fall_sentence$ is the whole opening sentence,
                    worded for the crown's kind -- start introductions with it.
  the house         the fallen realm's rump, if it survived, else the house in the old seat, else
                    a house written for the purpose -- two kings and a line of exiles down to a
                    living pretender.
  the pretender     the house's living claimant, landed or in exile. An exile starts sheltered at
                    the court of a sworn ruler.
  the sworn         4-8 rulers of the crown's land and people, one of them the Keeper.
  the nouns         society name, crown, old seat, house, fall year -- all $key$ substitutions into
                    the world's own localisation (gen_restor_l_english.yml).
  the kings         history/titles gives the crown its last kings, so the title's former-holders
                    panel shows a line that ends on the day the crown fell.

Everything else is here, and reads the generated half ONLY through global variables set at game
start. No static file names a generated key. If the world has no fallen crown, global_var:restor_crown
is never set and every file in this set stays inert -- gate new content on restor_active_trigger.


THE LOYALTY, AND THE PHASES
---------------------------
Loyalty: House global_var:restor_house, on the throne of global_var:restor_crown. Fixed, except that
if the house dies out the society may take up another (restor.0811).

  global_var:restor_phase = 1   UNDERGROUND. The crown is unheld or held by someone else.
                                Aim: see the pretender crowned. Secret, hunted, exposure matters.
  global_var:restor_phase = 2   RESTORED. A member of the house holds the crown.
                                The society goes public as global "restor_society_name_public" --
                                secrets dissolve, exposure stops. Aim: make the crown whole (every
                                de jure county under the king) and keep it.

  Triumph   on_title_gain: the crown gained by a member of the house (or by the pretender)
            -> restor_triumph_effect -> phase 2, restor.0701 (king), restor.0702 (the sworn).
  Fall      on_title_lost / on_title_destroyed while phase 2, to anyone not of the house
            -> restor_fall_effect -> phase 1, restor.0801. The new holder is the usurper.

Winning does not end the society; it changes what it is. Losing again sends it back underground.


THE PEOPLE
----------
  Pretender   global_var:restor_pretender. The claimant. On death: restor_pretender_succession_effect
              picks their primary heir if of the house, else the eldest living adult of the house,
              else the line has ended (restor.0811 to the Keeper).
  Keeper      global_var:restor_keeper. Runs the society; hands out charges. On death the highest-
              ranked member takes it (restor.0820).
  Usurper     global_var:restor_usurper, recomputed yearly: the crown's holder if it has one and is
              not of the house, else the top liege of whoever holds the old seat. May be unset.
  The sworn   trait restor_member, rank track restor_rank (XP 0-100):
                0 Sworn    25 Trusted    50 Captain    75 Elder
              Global list restor_roster holds every living member (maintained by the swear-in and
              leave effects and by on_death -- never add_to_global_variable_list by hand).


THE METERS
----------
  Favour      var:restor_favour on each member. Personal currency: earned by charges, the rite and
              recruiting; spent on powers. restor_favour_gain_effect / restor_favour_spend_effect
              ({ VALUE = n }) -- ALWAYS through these, they tooltip and toast.
  Exposure    var:restor_exposure on each member. 15 = under suspicion (+secret), 25 = highly
              suspect (the usurper is told). Decays yearly, faster for the patient and the deceitful.
              restor_exposure_gain_effect / restor_exposure_loss_effect ({ VALUE = n }). Phase 2: no-op.
  Support     global_var:restor_support, 0-100, recomputed yearly by restor_support_value:
                40 x share of the crown's counties inside the pretender's realm
                30 x share inside a sworn realm (held by a member or by anyone under one)
                15 x share carrying the restor_county_stirred modifier
                +1 per member, up to 15
              Milestones (global_var:restor_support_level 0-3) at 25 / 40 / 75 fire restor.0601-0603
              to every member; the starting value sets the level silently. 40
              (restor_rising_support_value) unlocks Raise the Standard -- once per underground phase --
              and is what the crown decision needs, with 25% of the land in the pretender's own realm
              (restor_crowning_share_value). A pretender who is a sworn lord's vassal is released by
              the crowning.


DIRECTION: CHARGES
------------------
CK2's goal-driven societies gave every member a concrete mission from the leader. Here that is a
CHARGE: every couple of years the Keeper gives each member without one a task aimed at the aim,
with a named target on the map and three years to do it (restor.0501 offers, restor.0510-0514 pay).

  win_over     a named lord on the crown's land: done when they are sworn, or their opinion of
               the pretender reaches 30.
  war_chest    give 100 gold to the cause (decision restor_give_to_the_cause_decision).
  stir         a named county of the crown: done when it carries restor_county_stirred, which the
               errand activity leaves where it goes.
  shelter      a landless pretender: done when they are at your court.
  recruit      swear anyone in.

Completion pays favour, rank XP and support, and toasts. Expiry (the timed variable runs out)
costs a little favour and the Keeper's opinion. The AI completes charges abstractly (35% a year).


FILES AND NAMESPACES
--------------------
Every key is restor_*. Event namespace restor, blocks by area -- keep to them:

  restor.0001-0099  core: intros, debug                  events/restor_core_events.txt
  restor.0100-0199  recruitment                          events/restor_recruitment_events.txt
  restor.0200-0299  belonging (yearly pulse)             events/restor_life_events.txt
  restor.0300-0399  exposure, the usurper's side         events/restor_exposure_events.txt
  restor.0400-0499  powers and their outcomes            events/restor_power_events.txt
  restor.0500-0599  charges                              events/restor_charge_events.txt
  restor.0600-0699  support milestones, the rising       events/restor_rising_events.txt
  restor.0700-0799  triumph, the restored realm          events/restor_crown_events.txt
  restor.0800-0899  fall and succession                  events/restor_crown_events.txt
  restor.0900-0999  the gathering (rite) and the errand  events/restor_gathering_events.txt

Each events file has its own loc file of the same stem under localization/english/.


VOICE
-----
Vanilla's plain, concrete voice (see the memory note on event prose tone; the worked example is
../Wilderness/localization/english/wilderness_clearing_l_english.yml). Short literal titles. The
description says what happened, who is asking and what the choice is, in the second person ("you"),
naming people and places through scopes. Options are things a lord would say, first person ("I").
Name the crown, house and society through the generated keys -- $restor_crown_name$,
$restor_house_name$, $restor_society_name$, $restor_fell_year$, $restor_seat_name$ -- so every
world's events read as its own. Never "the society" where the name fits.

Every option whose result is random ends in something the player reads -- a toast or a follow-up
event -- including the branch where nothing happened. Every payload the tooltip cannot draw (a flag,
a variable) gets a custom_tooltip.


HOW TO TEST
-----------
  Generate with --societies; the run log prints the crown, the fall, the pretender and the sworn.
  In game (console):
    event restor.0001    swear yourself in at Captain, with 100 favour
    event restor.0002    +50 support (walks the milestones)
    event restor.0003    make yourself the pretender
    event restor.0004    print the society's state as a toast chain
