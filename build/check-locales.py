#!/usr/bin/env python3
"""Mechanically validates Scribe's non-English locale files against en.json.

Rules implemented here are Decision 7 of
openspec/changes/add-player-locales/design.md and the "Locale files are mechanically
checked before they ship" requirement in
openspec/changes/add-player-locales/specs/scribe-locales/spec.md. This script proves
structure only (keys, placeholders, VTML, label cross-references) -- it cannot judge
whether a translation is fluent or means the right thing; that is the human/LLM meaning
audit described in Decision 3.

Usage:
    python3 build/check-locales.py                 validate every shipped locale
    python3 build/check-locales.py --dump-crossrefs print the generated UI-label
                                                     cross-reference table and exit
"""
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIBE_LANG_DIR = REPO_ROOT / "src/Mod/assets/scribe/lang"
GAME_LANG_DIR = REPO_ROOT / "src/Mod/assets/game/lang"
EN_PATH = SCRIBE_LANG_DIR / "en.json"
GAME_KEY = "worldattribute-scribeClockmakerRequiresTrait"
COPY_THRESHOLD = 40

PLACEHOLDER_RE = re.compile(r"\{(\d+)\}")
TAG_RE = re.compile(r"<(/?)([a-zA-Z]+)(?:\s[^>]*)?>")
HOTKEY_RE = re.compile(r"<hotkey>([^<]*)</hotkey>")
HANDBOOK_HREF_RE = re.compile(r"href=['\"]handbook://([^'\"]+)['\"]")
SPAN_RE = re.compile(r"<strong>(.*?)</strong>|<em>(.*?)</em>|\"([^\"]*)\"")

MOB_DEATH_RE = re.compile(r"^scribe-mob-death-(\d+)$")
PVP_GENERIC_RE = re.compile(r"^scribe-pvp-verb-generic-(\d+)$")


def is_comment_key(key):
    return key.startswith("_comment-")


def is_optional_flavor_key(key):
    """Matches the optional-flavor set in the scribe-locales spec: the mob-death and
    pvp-verb-generic pools beyond -0, all pvp-verb-tool-*/pvp-verb-damage-* category
    verbs, and any *-participle override."""
    if key.endswith("-participle"):
        return True
    if key.startswith("scribe-pvp-verb-tool-"):
        return True
    if key.startswith("scribe-pvp-verb-damage-"):
        return True
    m = MOB_DEATH_RE.match(key)
    if m:
        return int(m.group(1)) >= 1
    m = PVP_GENERIC_RE.match(key)
    if m:
        return int(m.group(1)) >= 1
    return False


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def extract_placeholders(text):
    return sorted(int(n) for n in PLACEHOLDER_RE.findall(text))


def extract_tags(text):
    return Counter(name.lower() for _, name in TAG_RE.findall(text))


def extract_hotkeys(text):
    return Counter(HOTKEY_RE.findall(text))


def extract_handbook_targets(text):
    return sorted(HANDBOOK_HREF_RE.findall(text))


def extract_spans(text):
    spans = []
    for m in SPAN_RE.finditer(text):
        content = next(g for g in m.groups() if g is not None)
        spans.append(content)
    return spans


def build_label_lookup(en):
    """Maps an English label's exact string value back to its own key. Handbook/
    craftinginfo keys are excluded so a handbook essay never gets treated as the
    canonical source of another handbook essay's quoted text."""
    lookup = {}
    for k, v in en.items():
        if is_comment_key(k) or not isinstance(v, str):
            continue
        if k.startswith("handbook-") or k.startswith("craftinginfo-"):
            continue
        lookup.setdefault(v, k)
    return lookup


def build_crossrefs(en):
    """Walks every handbook-*/craftinginfo-* value and records (handbook_key,
    span_index, label_key) for every quoted/<strong>/<em> span whose exact English text
    equals another key's full English value (design.md Decision 7)."""
    label_lookup = build_label_lookup(en)
    crossrefs = []
    for k, v in en.items():
        if is_comment_key(k) or not isinstance(v, str):
            continue
        if not (k.startswith("handbook-") or k.startswith("craftinginfo-")):
            continue
        for idx, span in enumerate(extract_spans(v)):
            label_key = label_lookup.get(span)
            if label_key:
                crossrefs.append((k, idx, label_key))
    return crossrefs


def check_locale(code, path, en, en_required_keys, en_optional_keys, crossrefs):
    errors = []
    try:
        data = load_json(path)
    except json.JSONDecodeError as e:
        return [f"invalid JSON: {e}"]

    en_keys = set(en_required_keys) | set(en_optional_keys)

    for k, v in data.items():
        if not isinstance(k, str):
            errors.append(f"non-string key: {k!r}")
            continue
        if is_comment_key(k):
            errors.append(f"_comment-* key present in non-English file: {k}")
            continue
        if k not in en_keys:
            errors.append(f"extra key not in en.json: {k}")
            continue
        if not isinstance(v, str):
            errors.append(f"{k}: value is not a string")

    for k in en_required_keys:
        if k not in data:
            errors.append(f"missing required key: {k}")

    for prefix, regex in [
        ("scribe-mob-death-", MOB_DEATH_RE),
        ("scribe-pvp-verb-generic-", PVP_GENERIC_RE),
    ]:
        indices = sorted(int(m.group(1)) for k in data if (m := regex.match(k)))
        for i, n in enumerate(indices):
            if n != i:
                errors.append(f"{prefix}* pool is not contiguous from -0 (found indices {indices})")
                break

    for k, v in data.items():
        if k not in en or is_comment_key(k):
            continue
        en_v = en[k]
        if not isinstance(v, str) or not isinstance(en_v, str):
            continue

        v_ph, en_ph = extract_placeholders(v), extract_placeholders(en_v)
        if v_ph != en_ph:
            errors.append(f"{k}: placeholder mismatch (en={en_ph}, {code}={v_ph})")

        v_tags, en_tags = extract_tags(v), extract_tags(en_v)
        if v_tags != en_tags:
            errors.append(f"{k}: VTML tag mismatch (en={dict(en_tags)}, {code}={dict(v_tags)})")

        v_hk, en_hk = extract_hotkeys(v), extract_hotkeys(en_v)
        if v_hk != en_hk:
            errors.append(f"{k}: hotkey id mismatch (en={dict(en_hk)}, {code}={dict(v_hk)})")

        v_tgt, en_tgt = extract_handbook_targets(v), extract_handbook_targets(en_v)
        if v_tgt != en_tgt:
            errors.append(f"{k}: handbook:// target mismatch (en={en_tgt}, {code}={v_tgt})")

        if len(en_v) > COPY_THRESHOLD and v == en_v:
            errors.append(f"{k}: value is identical to English (len={len(en_v)}) -- looks copied, not translated")

    by_handbook = defaultdict(list)
    for hk, idx, lk in crossrefs:
        by_handbook[hk].append((idx, lk))
    for hk, items in by_handbook.items():
        if hk not in data or not isinstance(data[hk], str):
            continue
        loc_spans = extract_spans(data[hk])
        for idx, lk in items:
            if idx >= len(loc_spans):
                errors.append(f"{hk}: expected a label-referencing span #{idx} (for {lk}) but found none")
                continue
            loc_label_value = data.get(lk)
            if loc_label_value is None or not isinstance(loc_label_value, str):
                continue
            if loc_spans[idx] != loc_label_value:
                errors.append(
                    f"{hk} span #{idx} ({loc_spans[idx]!r}) does not match this locale's own "
                    f"translation of {lk} ({loc_label_value!r})"
                )

    return errors


def check_game_lang(codes):
    errors = []
    en_path = GAME_LANG_DIR / "en.json"
    if not en_path.exists():
        errors.append("assets/game/lang/en.json is missing")
    else:
        en_game = load_json(en_path)
        if GAME_KEY not in en_game:
            errors.append(f"assets/game/lang/en.json missing {GAME_KEY}")

    for code in codes:
        path = GAME_LANG_DIR / f"{code}.json"
        if not path.exists():
            errors.append(f"assets/game/lang/{code}.json is missing")
            continue
        try:
            data = load_json(path)
        except json.JSONDecodeError as e:
            errors.append(f"assets/game/lang/{code}.json: invalid JSON: {e}")
            continue
        if GAME_KEY not in data:
            errors.append(f"assets/game/lang/{code}.json missing {GAME_KEY}")
        extra = [k for k in data if k != GAME_KEY]
        for k in extra:
            errors.append(f"assets/game/lang/{code}.json has unexpected extra key: {k}")

    return errors


def dump_crossrefs(crossrefs):
    print(f"{len(crossrefs)} UI-label cross-reference(s) generated from en.json:\n")
    for hk, idx, lk in crossrefs:
        print(f"  {hk} span #{idx}  ->  {lk}")


def main(argv):
    if not EN_PATH.exists():
        print(f"FATAL: {EN_PATH} not found", file=sys.stderr)
        return 2

    en = load_json(EN_PATH)
    en_required_keys = [k for k in en if not is_comment_key(k) and not is_optional_flavor_key(k)]
    en_optional_keys = [k for k in en if not is_comment_key(k) and is_optional_flavor_key(k)]
    crossrefs = build_crossrefs(en)

    if "--dump-crossrefs" in argv:
        dump_crossrefs(crossrefs)
        return 0

    ok = True
    target_codes = [a for a in argv if not a.startswith("-")]
    if target_codes:
        locale_files = [SCRIBE_LANG_DIR / f"{c}.json" for c in target_codes]
    else:
        locale_files = sorted(p for p in SCRIBE_LANG_DIR.glob("*.json") if p.name != "en.json")
    if not locale_files:
        print("No non-English locale files found under src/Mod/assets/scribe/lang/.")

    for path in locale_files:
        code = path.stem
        errors = check_locale(code, path, en, en_required_keys, en_optional_keys, crossrefs)
        if errors:
            ok = False
            print(f"\n✗ {code}.json ({len(errors)} problem(s)):")
            for e in errors:
                print(f"  - {e}")
        else:
            print(f"✓ {code}.json")

    game_errors = check_game_lang([p.stem for p in locale_files])
    if game_errors:
        ok = False
        print(f"\n✗ assets/game/lang ({len(game_errors)} problem(s)):")
        for e in game_errors:
            print(f"  - {e}")
    else:
        print("✓ assets/game/lang")

    if ok:
        print("\nAll locale files pass.")
        return 0
    print("\nLocale check FAILED.")
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
