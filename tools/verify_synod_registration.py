"""Check Synod script compatibility against the installed vanilla objects.

This is a source/packaging check, not a substitute for CK3 runtime tests.
Run: python tools/verify_synod_registration.py --game ".../Crusader Kings III/game"
"""
import argparse
import math
from pathlib import Path
import re


def parse(text):
    tokens = re.findall(r'#[^\n]*|"(?:\\.|[^"\\])*"|[{}]|[!?<>]?=|[<>]|[^\s{}=!?<>]+', text)
    tokens = [t for t in tokens if not t.startswith('#')]
    position = 0

    def entries():
        nonlocal position
        result = []
        while position < len(tokens) and tokens[position] != '}':
            key = tokens[position]
            position += 1
            if key == '{':
                value = entries()
                assert position < len(tokens) and tokens[position] == '}'
                position += 1
                result.append((key, None, value))
                continue
            if position >= len(tokens) or tokens[position] not in ('=', '?=', '!=', '<=', '>=', '<', '>'):
                # Bare list members (e.g. on_actions) and numeric lists are legal.
                result.append((key, None, None))
                continue
            operator = tokens[position]
            position += 1
            assert position < len(tokens), f'Missing value after {key}'
            if tokens[position] == '{':
                position += 1
                value = entries()
                assert position < len(tokens) and tokens[position] == '}', f'Unclosed {key}'
                position += 1
            else:
                value = tokens[position]
                position += 1
            result.append((key, operator, value))
        return result

    result = entries()
    assert position == len(tokens), 'Unexpected closing brace'
    return result


def object_body(tree, key):
    matches = [value for name, op, value in tree if name == key and op == '=']
    assert len(matches) == 1, f'{key}: expected one definition, got {len(matches)}'
    return matches[0]


def walk(tree):
    for entry in tree:
        yield entry
        if isinstance(entry[2], list):
            yield from walk(entry[2])


def vanilla_interaction_projection(patched, name):
    """Remove only the explicitly generated branches; compare every remaining native token."""
    direct = name == 'appoint_cardinal_interaction'
    gate = parse(('scope:puppet_or_actor' if direct else 'scope:actor')
                 + '.faith = { has_doctrine = special_doctrine_gen_clerical_regions }')
    offer = 'gen_synod_direct_offer' if direct else 'gen_synod_petition_offer'
    offer_gate = parse('OR = { scope:' + offer + ' ?= yes AND = { NOT = { exists = scope:' + offer + ' } '
                       + ('scope:puppet_or_actor' if direct else 'scope:actor')
                       + '.faith = { has_doctrine = special_doctrine_gen_clerical_regions } } }')
    restored = []
    for key, op, value in patched:
        if key in ('on_send', 'localization_values'):
            continue
        if key in ('desc', 'notification_text'):
            selected = object_body(value, 'first_valid')
            assert len(selected) == 2 and selected[0][:2] == ('triggered_desc', '=')
            assert object_body(selected[0][2], 'trigger') == gate
            assert selected[1][0] == 'desc'
            value = selected[1][2]
        elif key in ('is_shown', 'is_valid_showing_failures_only', 'on_accept', 'on_decline', 'auto_accept'):
            effect = key in ('on_accept', 'on_decline')
            assert len(value) == 2
            assert value[0][:2] == ('if' if effect else 'trigger_if', '=')
            assert value[1][:2] == ('else' if effect else 'trigger_else', '=')
            assert object_body(value[0][2], 'limit') == (offer_gate if effect else gate)
            value = value[1][2]
        elif not direct and key == 'is_available':
            assert value[-1][:2] == ('trigger_if', '=')
            assert object_body(value[-1][2], 'limit') == parse('faith = { has_doctrine = special_doctrine_gen_clerical_regions }')
            value = value[:-1]
        elif key == 'cost':
            value = list(value)
            if direct:
                treasury = object_body(value, 'treasury')
                assert treasury[-1] == ('if', '=', [('limit', '=', gate), ('multiply', '=', '0')])
                value = [(k, o, v[:-1] if k == 'treasury' else v) for k, o, v in value]
            else:
                assert object_body(value, 'piety') == [
                    ('value', '=', 'pam_request_court_chaplain_cardinalate_cost_value'),
                    ('if', '=', [('limit', '=', gate), ('multiply', '=', '0')])]
                value = [('piety', '=', 'pam_request_court_chaplain_cardinalate_cost_value')]
        elif not direct and key == 'send_option':
            if ('flag', '=', 'gen_synod_petition_hook') in value:
                assert object_body(value, 'is_shown') == gate
                continue
            if ('flag', '=', 'hook') in value:
                assert value[0] == ('is_shown', '=', [('NOT', '=', gate)])
                value = value[1:]
        elif direct and key == 'ai_will_do':
            assert value[-1][0] == 'modifier' and value[-1][2][0] == gate[0]
            assert ('value', '=', 'gen_synod_personal_appointment_ai_value') in list(walk(value[-1][2]))
            value = [(k, o, v[1:] if k == 'modifier' else v) for k, o, v in value[:-1]]
            for _, _, modifier in [e for e in object_body(patched, key)[:-1] if e[0] == 'modifier']:
                assert modifier[0] == ('NOT', '=', gate)
        elif not direct and key == 'ai_accept':
            assert value[-1][2][0] == ('scope:gen_synod_petition_hook', '?=', 'yes')
            restored_ai = []
            for k, o, v in value[:-1]:
                if k == 'modifier' and v[0] == ('NOT', '=', gate):
                    assert any(e[0] == 'desc' and e[2] in (
                        'COLLEGE_OF_CARDINALS_UNDERMANNED_REASON',
                        'COLLEGE_OF_CARDINALS_SEVERELY_UNDERMANNED_REASON') for e in v)
                    v = v[1:]
                restored_ai.append((k, o, v))
            value = restored_ai
        restored.append((key, op, value))
    return restored


def appointment_contract_checks(core, read):
    triggers = read(core / 'common/scripted_triggers/zz_gen_synod_personal_seat_triggers.txt')
    established = list(walk(object_body(triggers, 'gen_synod_established_faith_trigger')))
    assert ('gen_synod_electing_faith_trigger', '=', 'yes') in established
    assert ('has_realm_law', '=', 'theocratic_elective_succession_law') in established
    identity = list(walk(object_body(triggers, 'gen_synod_personal_candidate_identity_trigger')))
    assert ('NOT', '=', [('exists', '=', 'clerical_elector_title')]) in identity
    assert ('has_variable', '=', 'gen_synod_faith') in identity
    assert ('has_variable', '=', 'gen_synod_personal') in identity
    assert ('has_variable', '=', 'gen_synod_see') in identity
    grant = object_body(read(core / 'common/scripted_effects/zz_gen_synod_personal_seat_effects.txt'),
                        'gen_synod_grant_personal_seat_effect')
    assert grant[0] == ('clear_saved_scope', '=', 'gen_personal_seat')
    guarded = object_body(grant, 'if')
    assert object_body(guarded, 'limit') == parse(
        'gen_synod_can_grant_personal_seat_trigger = { AUTHORITY = $AUTHORITY$ }')
    assert ('set_destroy_on_succession', '=', 'yes') in list(walk(guarded))
    assert not any(v == 'roma_cardinalates' for k, o, v in walk(grant))
    transfer = next(v for k, o, v in guarded if k == 'if'
                    and any(e[0] == 'resolve_title_and_vassal_change' for e in v))
    resolved = next(i for i, e in enumerate(transfer) if e[0] == 'resolve_title_and_vassal_change')
    after_resolve = transfer[resolved + 1]
    assert object_body(after_resolve[2], 'limit') == parse('exists = scope:gen_personal_primary_title')
    assert ('set_primary_title_to', '=', 'scope:gen_personal_primary_title') in after_resolve[2]
    effects = read(core / 'common/scripted_effects/zz_gen_synod_personal_appointment_effects.txt')
    for effect_name, recipient, payment in (
        ('gen_synod_accept_direct_appointment_effect', 'scope:recipient', 'remove_treasury_or_gold'),
        ('gen_synod_accept_chaplain_petition_effect', 'scope:new_cardinal', 'add_piety'),
    ):
        body = object_body(effects, effect_name)
        guarded = object_body(body, 'if')
        assert ('exists', '=', 'scope:gen_synod_sent_head') in list(walk(object_body(guarded, 'limit')))
        grant_index = next(i for i, e in enumerate(guarded) if e[0] == recipient)
        assert object_body(guarded[grant_index][2], 'gen_synod_grant_personal_seat_effect')
        success = object_body(guarded[grant_index + 1][2], 'limit')
        assert success == parse('scope:gen_personal_seat ?= { holder = ' + recipient + ' }')
        assert any(k == payment for k, o, v in walk(guarded[grant_index + 1][2]))
        # Every payment/hook effect belongs under the successful grant, never before it or in cancellation.
        assert not any(k in ('remove_treasury_or_gold', 'add_piety', 'use_hook')
                       for k, o, v in walk(guarded[:grant_index + 1]))
    petition = object_body(effects, 'gen_synod_accept_chaplain_petition_effect')
    assert ('cp:councillor_court_chaplain', '?=', 'scope:new_cardinal') in list(walk(petition))
    assert ('use_hook', '=', 'scope:recipient') in list(walk(petition))
    assert ('gen_synod_direct_appointment_cooldown', '=', 'yes') not in list(walk(petition))
    for key, op, value in effects:
        if 'cancelled' in key:
            assert not any(k in ('remove_treasury_or_gold', 'add_piety', 'use_hook', 'create_dynamic_title')
                           for k, o, v in walk(value))

    actions = read(core / 'common/on_action/zz_gen_synod_registration_on_actions.txt')
    cleanup = 'gen_synod_ineligible_personal_cleanup'
    for callback in ('on_government_change', 'yearly_playable_pulse'):
        appended = object_body(object_body(actions, callback), 'on_actions')
        assert (cleanup, None, None) in appended
        assert not any(k in ('trigger', 'effect') for k, o, v in object_body(actions, callback))
    invalid = object_body(actions, cleanup)
    condition = object_body(invalid, 'trigger')
    assert object_body(condition, 'any_held_title') == parse('has_variable = gen_synod_personal')
    assert object_body(condition, 'OR') == parse(
        'NOT = { pam_can_receive_cardinalate_trigger = yes } '
        'NOT = { pam_is_investiture_clergy_trigger = yes }')
    assert object_body(invalid, 'effect') == parse('gen_synod_remove_personal_seats_effect = yes')
    assert not any('clerical_elector_title' in k for k, o, v in walk(invalid)), \
        'Inactive personal titles must be cleaned without relying on the active elector pointer'

    # Evaluate the actual capacity script over elector fixtures, including full/overflow allowances.
    values = read(core / 'common/script_values/zz_gen_synod_personal_seat_values.txt')

    def value(name, titles):
        def number(token):
            if any(k == token for k, o, v in values):
                return value(token, titles)
            return float(token)

        def matches(tree, title):
            return all(not matches(v, title) if k == 'NOT' else
                       (title == 'personal') if k == 'has_variable' and v == 'gen_synod_personal' else
                       False for k, o, v in tree)

        def evaluate(tree, total=0):
            for k, o, v in tree:
                if k == 'limit':
                    continue
                if k == 'every_clerical_elector_title':
                    for title in titles:
                        if matches(object_body(v, 'limit'), title):
                            total = evaluate(v, total)
                elif k == 'floor':
                    total = math.floor(total)
                elif k == 'value':
                    total = number(v)
                elif k == 'add':
                    total += number(v)
                elif k == 'subtract':
                    total -= number(v)
                elif k == 'divide':
                    total /= number(v)
                elif k == 'min':
                    total = max(total, number(v))
                else:
                    raise AssertionError(f'Unexpected capacity operation: {k}')
            return total

        return evaluate(object_body(values, name))

    for permanent, personal, allowance, room in (
        (0, 0, 1, 1), (1, 0, 1, 1), (3, 0, 1, 1), (3, 1, 1, 0),
        (5, 1, 1, 0), (6, 1, 2, 1), (9, 3, 3, 0), (9, 4, 3, -1), (15, 4, 5, 1),
    ):
        titles = ['permanent'] * permanent + ['personal'] * personal
        assert value('gen_synod_personal_seat_limit_value', titles) == allowance
        assert value('gen_synod_personal_seat_count_value', titles) == personal
        assert value('gen_synod_personal_seat_room_value', titles) == room


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--mod', type=Path, help='Also check the exported mod contains the current Synod scripts')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    core = repo / 'BaseFilesToCopy/Core'
    effects = core / 'common/scripted_effects'
    read = lambda path: parse(path.read_text(encoding='utf-8-sig'))
    refill = read(effects / 'zz_gen_synod_refill_effects.txt')
    for name, source, character_scope in (
        ('pam_seed_cardinals_effect', '11_dlc_pam_scripted_effects.txt', False),
        ('pam_cardinal_vacancy_on_death_effect', 'pam_effects.txt', True),
    ):
        original = object_body(read(args.game / 'common/scripted_effects' / source), name)
        wrapper = object_body(refill, name)
        assert len(wrapper) == 1 and wrapper[0][:2] == ('if', '=')
        guarded = wrapper[0][2]
        gate = 'NOT = { ' + ('faith = { ' if character_scope else '')
        gate += 'has_doctrine = special_doctrine_gen_clerical_regions '
        gate += '} }' if character_scope else '}'
        assert guarded[0] == ('limit', '=', parse(gate)), f'{name}: exclusion is wrong'
        assert guarded[1:] == original, f'{name}: vanilla body changed; review patch compatibility'

    for name, filename in (
        ('appoint_cardinal_interaction', 'zz_gen_synod_appointment_interaction.txt'),
        ('request_court_chaplain_cardinalate_interaction', 'zz_gen_synod_chaplain_interaction.txt'),
    ):
        original = object_body(read(args.game / 'common/character_interactions/pam_interactions.txt'), name)
        patched = object_body(read(core / 'common/character_interactions' / filename), name)
        assert vanilla_interaction_projection(patched, name) == original, f'{name}: vanilla fallback changed'
    appointment_contract_checks(core, read)

    files = list(core.rglob('*.txt'))
    trees = {file: read(file) for file in files}
    definitions = {key for tree in trees.values() for key, op, value in tree if op == '='}
    synod_files = [f for f in files if 'synod' in f.name or 'see_electors' in f.name or 'pam_cardinal_title' in f.name]
    for file in synod_files:
        for key, op, value in walk(trees[file]):
            if key.startswith('gen_synod') and not key.endswith('_title') and op == '=':
                assert key in definitions, f'{file.name}: undefined {key}'
    assert not any(key == 'pam_pope_is_head_of_faith_or_rite_trigger' for tree in trees.values()
                   for key, op, value in tree if op == '='), 'Global Papacy trigger was overridden'

    registration = object_body(read(effects / 'zz_gen_synod_registration_effects.txt'), 'gen_synod_register_see_effect')
    flat = list(walk(registration))
    for variable in ('gen_synod_seat', 'gen_synod_see', 'gen_synod_faith'):
        assert ('name', '=', variable) in flat, f'Missing persistent {variable} link'
    assert ('set_destroy_on_succession', '=', 'no') in flat
    assert ('add_clerical_elector', '=', 'scope:gen_register_faith') in flat
    assert not any(value == 'roma_cardinalates' for key, op, value in flat), 'Registration uses shared pool'
    assert ('gen_synod_remove_personal_seats_effect', '=', 'yes') in flat

    destruction = object_body(read(effects / 'zz_gen_pam_cardinal_title_effects.txt'), 'pam_destroy_cardinal_title_effect')
    preserve = object_body(destruction[0][2], 'limit')
    assert ('NOT', '=', [('has_variable', '=', 'gen_synod_personal')]) in list(walk(preserve)), 'Personal seats are being preserved'
    original_destroy = object_body(read(args.game / 'common/scripted_effects/11_dlc_pam_scripted_effects.txt'), 'pam_destroy_cardinal_title_effect')
    assert object_body(destruction, 'else') == original_destroy, 'Vanilla destruction body changed'

    loc = core / 'localization/english/zz_gen_synod_registration_l_english.yml'
    assert loc.read_bytes().startswith(b'\xef\xbb\xbf'), 'Localization needs a UTF-8 BOM'
    for key in ('gen_synod_dynamic_seat_name', 'gen_synod_dynamic_seat_adj'):
        assert key + ':' in loc.read_text(encoding='utf-8-sig')
    appointment_loc = core / 'localization/english/zz_gen_synod_appointments_l_english.yml'
    assert appointment_loc.read_bytes().startswith(b'\xef\xbb\xbf')
    native_loc = (args.game / 'localization/english/dlc/pam/pam_character_interactions_l_english.yml').read_text(encoding='utf-8-sig')
    for native_key in (
        'appoint_cardinal_interaction', 'appoint_cardinal_interaction_desc', 'appoint_cardinal_interaction_notification',
        'request_court_chaplain_cardinalate_interaction', 'request_court_chaplain_cardinalate_interaction_desc',
        'request_court_chaplain_cardinalate_interaction_notification',
    ):
        value = re.search(r'^\s*' + native_key + r':(?:0)?\s*(".*")\s*$', native_loc, re.M)[1]
        if native_key.endswith('_desc') or native_key.endswith('_notification'):
            assert f'{native_key}:0 {value}' in appointment_loc.read_text(encoding='utf-8-sig')
        else:
            # The petition menu name is evaluated on actor.Custom(), where actor is ROOT.Char.
            if native_key == 'request_court_chaplain_cardinalate_interaction':
                value = value.replace('[actor.', '[ROOT.Char.')
            assert f'gen_synod_vanilla_{native_key}:0 {value}' in appointment_loc.read_text(encoding='utf-8-sig')
    if args.mod:
        for source in synod_files + [loc, appointment_loc, core / 'localization/english/zz_gen_synod_personal_seat_l_english.yml']:
            exported = args.mod / source.relative_to(core)
            assert exported.exists(), f'Export missing {exported}'
            if source.suffix == '.txt':
                # Export adds a provenance comment header; compare the script objects.
                assert read(exported) == trees[source], f'Export has stale {exported}'
            else:
                lines = lambda p: [line.strip() for line in p.read_text(encoding='utf-8-sig').splitlines()
                                   if line.strip() and not line.lstrip().startswith('#')]
                assert lines(exported) == lines(source), f'Export has stale {exported}'
    print('Synod source checks passed: vanilla fallback bodies unchanged, references resolved, persistent links and personal-seat cleanup present, localization packaged correctly.')


if __name__ == '__main__':
    main()
