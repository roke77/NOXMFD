"""Parses the plugin's real keybind registry (src/plugin/Input/Keybinds.cs) so the KEY-page
preview in serve_web.py never drifts from it again — adding a keybind is still just adding one
Def()/DefFree()/DefKeyOnly()/AddAxis() call in Keybinds.cs's Bind(), same as always; there is
nothing second to remember to update.

Not a general C# parser — just enough to recover the four call shapes Keybinds.cs uses, its
`const string` section names, its three numbered-loop bind groups (TD assign, HUD/TGT presets),
and the SectionTitle/SectionNote switch expressions. If Keybinds.cs's style ever changes in a way
this can't follow, KeybindsParseError says exactly where and why, rather than silently returning a
wrong or incomplete list — a stale-but-plausible preview is the whole bug this module exists to
kill, so a loud failure here is strictly better than a quiet wrong one.
"""
import re
from pathlib import Path

KEYBINDS_CS = Path("src/plugin/Input/Keybinds.cs")


class KeybindsParseError(Exception):
    pass


_OPEN = {"(": ")", "{": "}", "[": "]"}
_CLOSE = {")": "(", "}": "{", "]": "["}

_CALL_RE = re.compile(r"\b(Def|DefFree|DefKeyOnly|AddAxis)\(config,")
_CONST_RE = re.compile(r'\bconst\s+string\s+(\w+)\s*=\s*"((?:[^"\\]|\\.)*)"\s*;')
_FOR_RE = re.compile(r"for\s*\(\s*int\s+(\w+)\s*=\s*(\d+)\s*;\s*\1\s*<=\s*([\w.]+)\s*;\s*\1\+\+\s*\)")
_SECTION_TITLE_SIG = re.compile(
    r"internal\s+static\s+string\s+SectionTitle\s*\(\s*string\s+section\s*\)\s*=>\s*section\s+switch\s*\{")
_SECTION_NOTE_SIG = re.compile(
    r"internal\s+static\s+string\?\s+SectionNote\s*\(\s*string\s+section\s*\)\s*=>\s*section\s+switch\s*\{")


def _find_matching(text, open_idx):
    """text[open_idx] is one of ({[ — returns the index of its match, skipping string/char literals."""
    opener = text[open_idx]
    if opener not in _OPEN:
        raise KeybindsParseError(f"_find_matching: {opener!r} at {open_idx} is not an opener")
    depth = 0
    i = open_idx
    n = len(text)
    while i < n:
        c = text[i]
        if c in ('"', "'"):
            quote = c
            i += 1
            while i < n and text[i] != quote:
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if c in _OPEN:
            depth += 1
        elif c in _CLOSE:
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise KeybindsParseError(f"unbalanced {opener!r} starting at offset {open_idx}")


def _split_top_level(text):
    """Splits text on commas at bracket depth 0, treating string/char literals as opaque."""
    parts = []
    depth = 0
    start = 0
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c in ('"', "'"):
            quote = c
            i += 1
            while i < n and text[i] != quote:
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if c in _OPEN:
            depth += 1
        elif c in _CLOSE:
            depth -= 1
        elif c == "," and depth == 0:
            parts.append(text[start:i])
            start = i + 1
        i += 1
    parts.append(text[start:])
    return [p.strip() for p in parts]


_ESCAPES = {'"': '"', "\\": "\\", "n": "\n", "t": "\t", "r": "\r", "'": "'", "0": "\0"}


def _decode_cs_string(raw):
    out = []
    i, n = 0, len(raw)
    while i < n:
        c = raw[i]
        if c == "\\" and i + 1 < n and raw[i + 1] in _ESCAPES:
            out.append(_ESCAPES[raw[i + 1]])
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def _string_literal_at(text, i):
    """text[i] must be '"'. Returns (decoded value, index just past the closing quote)."""
    if text[i] != '"':
        raise KeybindsParseError(f"expected a string literal at offset {i}: {text[i:i+20]!r}")
    j = i + 1
    raw = []
    while j < len(text) and text[j] != '"':
        if text[j] == "\\" and j + 1 < len(text):
            raw.append(text[j:j + 2])
            j += 2
        else:
            raw.append(text[j])
            j += 1
    if j >= len(text):
        raise KeybindsParseError(f"unterminated string literal starting at offset {i}")
    return _decode_cs_string("".join(raw)), j + 1


def _parse_concat_expr(expr, loop_names=None, loop_val=None):
    """Evaluates a `"lit" + "lit" + loopVar + "lit"`-shaped C# expression to a plain string.
    loop_names is the set of identifiers that stand for the current loop value — the loop
    variable itself, plus any `int alias = loopVar;` per-iteration copy declared in its body
    (Keybinds.cs makes that copy so a lambda doesn't capture the loop variable directly, and the
    call site sometimes uses the copy instead of the original)."""
    i, n = 0, len(expr)
    out = []
    while i < n:
        while i < n and expr[i] in " \t\r\n+":
            i += 1
        if i >= n:
            break
        if expr[i] == '"':
            lit, i = _string_literal_at(expr, i)
            out.append(lit)
            continue
        j = i
        while j < n and (expr[j].isalnum() or expr[j] == "_"):
            j += 1
        ident = expr[i:j]
        if not ident:
            raise KeybindsParseError(f"can't parse expression fragment {expr[i:i+30]!r} in {expr!r}")
        if loop_names is not None and ident in loop_names:
            out.append(str(loop_val))
        else:
            raise KeybindsParseError(f"unresolvable identifier {ident!r} in expression {expr!r}")
        i = j
    return "".join(out)


def _resolve_loop_bound(expr, repo_root):
    if expr.isdigit():
        return int(expr)
    m = re.match(r"^(\w+)\.(\w+)$", expr)
    if not m:
        raise KeybindsParseError(f"can't resolve loop bound {expr!r}")
    cls, member = m.groups()
    candidates = list(Path(repo_root, "src", "plugin").rglob(f"{cls}.cs"))
    if not candidates:
        raise KeybindsParseError(f"can't find {cls}.cs to resolve loop bound {expr!r}")
    text = candidates[0].read_text(encoding="utf-8")
    cm = re.search(rf"\bconst\s+int\s+{re.escape(member)}\s*=\s*(\d+)\s*;", text)
    if not cm:
        raise KeybindsParseError(f"can't find 'const int {member}' in {candidates[0]} to resolve {expr!r}")
    return int(cm.group(1))


def _loop_spans(text, repo_root):
    spans = []
    for m in _FOR_RE.finditer(text):
        var, start_s, end_expr = m.groups()
        brace_idx = text.index("{", m.end())
        body_end = _find_matching(text, brace_idx)
        names = {var}
        for am in re.finditer(rf"\bint\s+(\w+)\s*=\s*{re.escape(var)}\s*;", text[brace_idx:body_end]):
            names.add(am.group(1))
        spans.append({
            "names": names, "start": int(start_s), "end": _resolve_loop_bound(end_expr, repo_root),
            "body_start": brace_idx, "body_end": body_end,
        })
    return spans


def _loop_at(spans, pos):
    for s in spans:
        if s["body_start"] < pos < s["body_end"]:
            return s
    return None


def _parse_switch_map(text, sig_re):
    """Parses a `section switch { "lit" => <expr>, ..., _ => ... }` body into {case: value}.
    The `_` default arm is dropped — callers fall back to the untranslated raw section name/no
    note, which is exactly what Keybinds.cs's own `_ => section` / `_ => null` defaults do."""
    m = sig_re.search(text)
    if not m:
        raise KeybindsParseError(f"can't find switch expression matching {sig_re.pattern!r}")
    brace_idx = text.index("{", m.start())
    close_idx = _find_matching(text, brace_idx)
    result = {}
    for arm in _split_top_level(text[brace_idx + 1:close_idx]):
        arm = arm.strip()
        if not arm:
            continue
        if "=>" not in arm:
            raise KeybindsParseError(f"malformed switch arm: {arm!r}")
        left, right = arm.split("=>", 1)
        left = left.strip()
        if left == "_":
            continue
        if not left.startswith('"'):
            raise KeybindsParseError(f"unexpected switch case label: {left!r}")
        key, _ = _string_literal_at(left, 0)
        result[key] = _parse_concat_expr(right.strip())
    return result


def load_keybinds(repo_root):
    """Returns (binds, notes) built straight from Keybinds.cs — binds is a list of dicts in the
    exact shape /keybinds-config serves (id/section/label/description plus key+joy, key-only, or
    axis fields per bind kind); notes is {display section title: note text}."""
    text = Path(repo_root, KEYBINDS_CS).read_text(encoding="utf-8")

    consts = {m.group(1): _decode_cs_string(m.group(2)) for m in _CONST_RE.finditer(text)}
    spans = _loop_spans(text, repo_root)

    binds = []
    for m in _CALL_RE.finditer(text):
        kind_fn = m.group(1)
        paren_idx = m.start() + len(kind_fn)
        close_idx = _find_matching(text, paren_idx)
        args = _split_top_level(text[paren_idx + 1:close_idx])[1:]   # drop the leading `config`

        loop = _loop_at(spans, m.start())
        loop_names = loop["names"] if loop else None
        iter_values = range(loop["start"], loop["end"] + 1) if loop else [None]

        for loop_val in iter_values:
            id_ = _parse_concat_expr(args[0], loop_names, loop_val)
            section_key = args[1].strip()
            if section_key.startswith('"'):
                section, _ = _string_literal_at(section_key, 0)
            elif section_key in consts:
                section = consts[section_key]
            else:
                raise KeybindsParseError(f"unknown section const {section_key!r} for bind {id_!r}")
            label = _parse_concat_expr(args[3], loop_names, loop_val)

            if kind_fn in ("Def", "DefFree"):
                description = _parse_concat_expr(args[5], loop_names, loop_val)
                entry = {"id": id_, "section": section, "label": label, "description": description,
                          "key": "", "joyButton": -1, "joyNum": 0}
            elif kind_fn == "DefKeyOnly":
                description = _parse_concat_expr(args[4], loop_names, loop_val)
                entry = {"id": id_, "section": section, "label": label, "description": description, "key": ""}
            else:   # AddAxis
                description = _parse_concat_expr(args[4], loop_names, loop_val)
                entry = {"id": id_, "section": section, "label": label, "description": description,
                          "axis": -1, "axisNum": 0, "axisInvert": False}
            binds.append(entry)

    section_titles = _parse_switch_map(text, _SECTION_TITLE_SIG)
    for b in binds:
        b["section"] = section_titles.get(b["section"], b["section"])

    notes_raw = _parse_switch_map(text, _SECTION_NOTE_SIG)
    notes = {section_titles.get(k, k): v for k, v in notes_raw.items()}

    return binds, notes


def self_check(repo_root):
    """Assert-based smoke test (no Python test runner in this repo — same reasoning as JsonLite.cs's
    SelfCheck/keybinds-keymap.test.js) exercising both the parsing mechanics on synthetic snippets
    and, end to end, the real Keybinds.cs — so a change to either the parser or that file's shape
    that breaks the preview is a loud failure at server startup, not a silently stale KEY page."""
    def check(cond, what):
        if not cond:
            raise AssertionError(f"keybinds_source.self_check failed: {what}")

    # -- mechanics: string decoding, escaped quotes inside one literal --
    check(_decode_cs_string('a\\"b\\\\c') == 'a"b\\c', "escaped quote/backslash decoding")
    lit, end = _string_literal_at('"the \\"IR\\" mode"', 0)
    check(lit == 'the "IR" mode', "escaped quotes inside a single string literal")

    # -- mechanics: top-level comma splitting ignores commas inside nested calls/lambdas --
    parts = _split_top_level('config, "id", cm, "Key", "Label", edge: false, "d", ac => { f(a, b); }')
    check(len(parts) == 8, f"top-level split should yield 8 args, got {len(parts)}: {parts}")
    check(parts[7].strip().endswith("}"), "lambda body with nested commas stays one argument")

    # -- mechanics: literal + literal + loopVar concatenation, including a per-iteration alias --
    expr = '"Assign " + s + " done"'
    check(_parse_concat_expr(expr, {"s"}, 3) == "Assign 3 done", "literal+var+literal concatenation")
    check(_parse_concat_expr(expr, {"slot", "s"}, 3) == "Assign 3 done", "alias name resolves alongside the loop var")

    # -- end to end: the real file parses, and specific known-tricky binds come out right --
    binds, notes = load_keybinds(repo_root)
    by_id = {b["id"]: b for b in binds}

    check("internal-mfd-poc-toggle" in by_id, "internal-mfd-poc-toggle must be present")
    check(by_id["internal-mfd-poc-toggle"]["label"] == "Internal MFD POC Toggle", "internal-mfd-poc-toggle label")
    check("weapon-release-single" in by_id, "weapon-release-single must be present")

    td_ids = [f"td-assign-{n}" for n in range(1, 10)]
    check(all(i in by_id for i in td_ids), "all 9 td-assign-N binds must be present")
    check("squad slot 3" in by_id["td-assign-3"]["description"], "td-assign-3 description substitutes its slot number")

    check(all(f"hud-preset-{n}" in by_id for n in range(1, 6)), "all 5 hud-preset-N binds must be present")
    check(all(f"tgt-preset-{n}" in by_id for n in range(1, 6)), "all 5 tgt-preset-N binds must be present")

    check("axis" in by_id["cursor-axis-h"] and "key" not in by_id["cursor-axis-h"], "cursor-axis-h is axis-only")
    check("key" in by_id["layout-save"] and "joyButton" not in by_id["layout-save"], "layout-save is key-only")

    check("TD" in notes, "TD section note must come through (it was missing from the old hand-written mock)")


if __name__ == "__main__":
    repo_root = Path(__file__).resolve().parent.parent
    self_check(repo_root)
    binds, notes = load_keybinds(repo_root)
    print(f"OK — parsed {len(binds)} binds across {len(set(b['section'] for b in binds))} sections, "
          f"{len(notes)} section notes.")
