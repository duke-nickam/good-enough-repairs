"""The Director's priority score: which gap of a goal is planned next.

The Director judges each gap's factors in its reply (swap loop, cut order, size, which gaps it needs);
this function turns them into a number, so the order is repeatable and its reasons are visible.
The formula is in docs/agent-board-plan.md (Goals)."""

SWAP_LOOP = 30
PER_WAITING_GAP = 10
CUT_ORDER = {1: -20, 2: -15, 3: -10, 4: -5}
SIZE = {"S": 0, "M": -5, "L": -10}


def priority_score(key, gaps):
    """Score of one gap. gaps: {key: gap} with 'swap_loop', 'cut_order', 'size', 'needs', 'title'.
    Returns (score, breakdown); score is None while the gap needs another gap: it cannot start yet."""
    gap = gaps[key]
    waits_for = [k for k in gap.get("needs") or [] if k in gaps]
    if waits_for:
        return None, "waits for " + ", ".join(gaps[k]["title"] for k in waits_for)
    parts = []
    if gap.get("swap_loop"):
        parts.append((SWAP_LOOP, "swap loop"))
    waiting = [k for k, g in gaps.items() if key in (g.get("needs") or [])]
    if waiting:
        parts.append((PER_WAITING_GAP * len(waiting), f"({len(waiting)} gap{'s' if len(waiting) > 1 else ''} need{'' if len(waiting) > 1 else 's'} it)"))
    cut = int(gap.get("cut_order") or 0)
    if cut in CUT_ORDER:
        parts.append((CUT_ORDER[cut], f"cut order {cut}"))
    size = gap.get("size", "M")
    if SIZE.get(size):
        parts.append((SIZE[size], f"size {size}"))
    score = sum(p for p, _ in parts)
    if not parts:
        return 0, "0 = 0"
    text = f"{parts[0][0]} {parts[0][1]}"
    for p, why in parts[1:]:
        text += f" {'+' if p >= 0 else '-'} {abs(p)} {why}"
    return score, f"{text} = {score}"


def rank(gaps):
    """All gaps as (key, score, breakdown), best first. Gaps that cannot start yet come last; ties keep the
    Director's order, which follows the GDD."""
    order = list(gaps)
    scored = [(k, *priority_score(k, gaps)) for k in order]
    return sorted(scored, key=lambda s: (s[1] is None, -(s[1] or 0), order.index(s[0])))
