"""Check Synod script compatibility against the installed vanilla objects.

This is a source/packaging check, not a substitute for CK3 runtime tests.
Run: python tools/verify_synod_registration.py --game ".../Crusader Kings III/game"
"""
import argparse
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

    name = 'request_court_chaplain_cardinalate_interaction'
    original = object_body(read(args.game / 'common/character_interactions/pam_interactions.txt'), name)
    patched = object_body(read(core / 'common/character_interactions/zz_gen_synod_chaplain_interaction.txt'), name)
    availability = object_body(patched, 'is_available')
    gate = parse('NOT = { faith = { has_doctrine = special_doctrine_gen_clerical_regions } }')[0]
    assert availability[0] == gate
    restored = [(key, op, value[1:] if key == 'is_available' else value) for key, op, value in patched]
    assert restored == original, 'Chaplain interaction changed beyond its Synod exclusion'

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
    if args.mod:
        for source in synod_files + [loc]:
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
