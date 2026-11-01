#!/usr/bin/env python3
"""Lockstep fixture corpus for FS.GG.Algorithms (ADR-0069, ADR-0098 §4.2): generate the canonical input bytes and
check the canonical output bytes against an independent Python oracle.

  scripts/lockstep-fixtures.py generate          write tests/FS.GG.Algorithms.Lockstep/fixtures/v1/cases.bin
  scripts/lockstep-fixtures.py check             exit 1 unless cases.bin equals what `generate` would write
  scripts/lockstep-fixtures.py oracle FILE       exit 1 unless FILE (expected.bin, or a runtime's output converted
                                                 to bytes) holds exactly the records this oracle derives
  scripts/lockstep-fixtures.py hex-to-bin IN OUT turn the fixture program's hex lines into canonical bytes

The oracle is independent of the F# code: exact integer Q16.16 arithmetic (Python ints, no fixed width),
xoshiro128** and its SplitMix-style expander written from the published description, list models of the
heap contracts, and Dijkstra for the optimal grid cost (it checks cost, validity and settle order, not the
F# tie-break path). Inputs are synthetic and generated from fixed constants by a 32-bit LCG; there is no
clock, no network and no randomness from the host.

Exit codes: 0 ok, 1 mismatch, 2 usage.
"""
from __future__ import annotations

import heapq
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "tests" / "FS.GG.Algorithms.Lockstep" / "fixtures" / "v1"
CASES = FIXTURES / "cases.bin"
M32 = 0xFFFFFFFF
I32_MIN, I32_MAX = -(2**31), 2**31 - 1


def s32(x: int) -> int:
    x &= M32
    return x - 2**32 if x & 0x80000000 else x


class Lcg:
    """Numerical Recipes 32-bit LCG: a fixed, documented input source (not under test)."""

    def __init__(self, seed: int) -> None:
        self.state = seed & M32

    def next(self) -> int:
        self.state = (self.state * 1664525 + 1013904223) & M32
        return self.state

    def below(self, n: int) -> int:
        return (self.next() >> 8) % n

    def i32(self) -> int:
        return s32(self.next() ^ (self.next() << 16))


class W:
    def __init__(self) -> None:
        self.b = bytearray()

    def u8(self, v: int) -> "W":
        self.b += struct.pack("<B", v & 0xFF)
        return self

    def u16(self, v: int) -> "W":
        self.b += struct.pack("<H", v & 0xFFFF)
        return self

    def i32(self, v: int) -> "W":
        self.b += struct.pack("<i", s32(v))
        return self

    def u32(self, v: int) -> "W":
        self.b += struct.pack("<I", v & M32)
        return self


# ------------------------------------------------------------------------------------------- case generation

SPECIAL = [0, 1, -1, 65536, -65536, 32768, -32768, 0x7FFF, 0x8001, 3 * 65536 + 0x8000, -(5 * 65536) - 0x8000,
           I32_MAX, I32_MIN, I32_MAX - 1, I32_MIN + 1, 181 * 65536, 46341, 0x40000000, -0x40000000]


def heap_script_cases():
    cases = []
    w = W().u32(11)
    for p, v in [(1, 10), (0, 20), (1, 30), (0, 40), (1, 50)]:
        w.u8(0).i32(p).i32(v)
    for _ in range(6):
        w.u8(1)
    cases.append((1001, 1, w.b))
    w = W()
    ops = []
    for i in range(20):
        ops.append((0, 7, i))
        if i % 3 == 2:
            ops.append((1,))
    ops += [(2,)] + [(1,)] * 15
    w.u32(len(ops))
    for op in ops:
        w.u8(op[0])
        if op[0] == 0:
            w.i32(op[1]).i32(op[2])
    cases.append((1002, 1, w.b))
    w = W()
    ops = [(0, I32_MAX, 1), (0, I32_MIN, 2), (0, 0, 3), (0, I32_MIN, 4), (0, I32_MAX, 5), (0, -1, 6)] + [(1,)] * 7 + [(2,)]
    w.u32(len(ops))
    for op in ops:
        w.u8(op[0])
        if op[0] == 0:
            w.i32(op[1]).i32(op[2])
    cases.append((1003, 1, w.b))
    for k, seed in enumerate([11, 12, 13]):
        rng = Lcg(seed)
        ops = []
        for i in range(240):
            r = rng.below(10)
            ops.append((0, rng.below(6) - 2, i) if r < 5 else ((1,) if r < 9 else (2,)))
        w = W().u32(len(ops))
        for op in ops:
            w.u8(op[0])
            if op[0] == 0:
                w.i32(op[1]).i32(op[2])
        cases.append((1004 + k, 1, w.b))
    cases.append((1007, 1, W().u32(2).u8(1).u8(2).b))
    return cases


def indexed_script_cases():
    def encode(ops):
        w = W().u32(len(ops))
        for op in ops:
            w.u8(op[0])
            for x in op[1:]:
                w.i32(x)
        return w.b

    cases = [
        (2001, 2, encode([(0, 0, 7), (0, 1, 7), (0, 2, 7), (1, 0, 7), (4,), (4,), (4,), (4,)])),
        (2002, 2, encode([(0, -1, 0), (0, 3, 5), (0, 3, 1), (1, 4, 1), (1, 3, 6), (2, -2, 0), (6, -1), (7,), (4,)])),
    ]
    for k, seed in enumerate([21, 22, 23]):
        rng = Lcg(seed)
        ops = []
        for _ in range(300):
            r = rng.below(14)
            key, prio = rng.below(16), rng.below(8)
            if r < 4:
                ops.append((0, key, prio))
            elif r < 6:
                ops.append((1, key, prio))
            elif r < 8:
                ops.append((2, key, prio))
            elif r < 9:
                ops.append((3, key))
            elif r < 12:
                ops.append((4,))
            elif r < 13:
                ops.append((5,))
            else:
                ops.append((6, key))
        ops.append((7,))
        cases.append((2003 + k, 2, encode(ops)))
    cases.append((2006, 2, encode([(0, 100000, 3), (0, 99999, 3), (6, 100000), (3, 100000), (6, 100000), (3, 100000),
                                   (4,), (4,), (7,)])))
    return cases


def fixed_cases():
    cases = []
    w = W()
    vec = []
    for fn in (0, 1, 2, 3, 7, 12, 13, 14):
        for a in SPECIAL:
            for b in SPECIAL:
                vec.append((fn, a, b))
    w.u32(len(vec))
    for fn, a, b in vec:
        w.u8(fn).i32(a).i32(b)
    cases.append((3001, 3, w.b))
    w = W()
    vec = [(fn, a, 0) for fn in (4, 5, 6, 8, 9, 10, 11) for a in SPECIAL + [-32769, 32767, 32768, 70000, -70000]]
    w.u32(len(vec))
    for fn, a, b in vec:
        w.u8(fn).i32(a).i32(b)
    cases.append((3002, 3, w.b))
    # Rounding ties: products whose low 16 bits are exactly 0x8000, both signs.
    w = W()
    vec = []
    for odd in (1, 3, 5, 0x7FFF, 0x12345, -1, -3, -0x12345):
        vec += [(2, 0x8000, odd), (2, -0x8000, odd), (2, odd, 0x8000), (2, 0x18000, odd)]
    w.u32(len(vec))
    for fn, a, b in vec:
        w.u8(fn).i32(a).i32(b)
    cases.append((3003, 3, w.b))
    rng = Lcg(31)
    w = W()
    vec = []
    for i in range(2400):
        fn = (0, 1, 2, 3, 4, 7)[i % 6]
        a = rng.i32() if rng.below(2) else s32(rng.below(400 * 65536) - 200 * 65536)
        b = rng.i32() if rng.below(2) else s32(rng.below(400 * 65536) - 200 * 65536)
        vec.append((fn, a, b))
    w.u32(len(vec))
    for fn, a, b in vec:
        w.u8(fn).i32(a).i32(b)
    cases.append((3004, 3, w.b))
    return cases


def grid_case(case_id, width, height, cells, start, goal, mode, eight, budget):
    w = W().u16(width).u16(height)
    w.b += bytes(cells)
    w.i32(start[0]).i32(start[1]).i32(goal[0]).i32(goal[1]).u8(mode).u8(8 if eight else 4).i32(budget)
    return (case_id, 4, w.b)


def grid_cases():
    open8 = [1] * 64
    rng = Lcg(41)
    weighted = [1 + rng.below(9) for _ in range(144)]
    for i in range(0, 144, 7):
        weighted[i] = 0
    weighted[0] = weighted[143] = 1
    maze = [1] * 49
    for y in range(7):
        maze[y * 7 + 3] = 0  # a full wall column: unreachable
    return [
        grid_case(4001, 8, 8, open8, (0, 0), (7, 7), 0, False, I32_MAX),
        grid_case(4002, 8, 8, open8, (0, 0), (7, 7), 1, False, I32_MAX),
        grid_case(4003, 8, 8, open8, (0, 0), (7, 5), 1, True, I32_MAX),
        grid_case(4004, 7, 7, maze, (0, 0), (6, 6), 1, False, I32_MAX),
        grid_case(4005, 12, 12, weighted, (0, 0), (11, 11), 0, False, I32_MAX),
        grid_case(4006, 12, 12, weighted, (0, 0), (11, 11), 1, False, I32_MAX),
        grid_case(4007, 12, 12, weighted, (0, 0), (11, 11), 1, True, I32_MAX),
        grid_case(4008, 12, 12, weighted, (0, 0), (11, 11), 0, False, 10),
        grid_case(4009, 12, 12, weighted, (0, 0), (0, 0), 2, False, I32_MAX),
        grid_case(4010, 8, 8, open8, (3, 3), (3, 3), 1, False, I32_MAX),
        grid_case(4011, 1, 1, [5], (0, 0), (0, 0), 0, False, I32_MAX),
        grid_case(4012, 8, 8, open8, (0, 0), (7, 7), 1, False, 0),
    ]


def xoshiro_cases():
    cases = [(5001, 5, W().u8(1).i32(1).i32(2).i32(3).i32(4).u32(10).b)]
    cid = 5002
    for seed in (0, 1, -1, I32_MAX, I32_MIN, 20261101):
        cases.append((cid, 5, W().u8(0).i32(seed).u32(8).b))
        cid += 1
    for seed in (0, 1, 42):
        cases.append((cid, 5, W().u8(2).i32(seed).u32(8).b))
        cid += 1
    for seed, bound in ((7, 1), (7, 2), (7, 6), (7, 1000), (8, I32_MAX), (9, 1 << 30), (10, (1 << 30) + 1)):
        cases.append((cid, 5, W().u8(3).i32(seed).i32(bound).u32(16).b))
        cid += 1
    for seed in (0, 1):
        cases.append((cid, 5, W().u8(4).i32(seed).u32(8).b))
        cid += 1
    cases.append((cid, 5, W().u8(5).i32(3).i32(-10).i32(11).u32(16).b))
    cid += 1
    cases.append((cid, 5, W().u8(1).i32(0).i32(0).i32(0).i32(0).u32(4).b))  # all-zero: refused
    cid += 1
    cases.append((cid, 5, W().u8(3).i32(1).i32(0).u32(1).b))  # bound 0: refused
    return cases


def all_cases():
    return heap_script_cases() + indexed_script_cases() + fixed_cases() + grid_cases() + xoshiro_cases()


def encode_cases(cases) -> bytes:
    w = W().u32(len(cases))
    for cid, op, payload in cases:
        w.u32(cid).u16(op).u32(len(payload))
        w.b += payload
    return bytes(w.b)


# ------------------------------------------------------------------------------------------------- oracle


class R:
    def __init__(self, b: bytes) -> None:
        self.b, self.p = b, 0

    def take(self, fmt: str):
        size = struct.calcsize(fmt)
        v = struct.unpack_from(fmt, self.b, self.p)
        self.p += size
        return v[0] if len(v) == 1 else v

    def u8(self):
        return self.take("<B")

    def u16(self):
        return self.take("<H")

    def i32(self):
        return self.take("<i")

    def u32(self):
        return self.take("<I")


class Refused(Exception):
    pass


def clamp(x: int) -> int:
    return I32_MAX if x > I32_MAX else I32_MIN if x < I32_MIN else x


def trunc_div(a: int, b: int) -> int:
    q = abs(a) // abs(b)
    return q if (a < 0) == (b < 0) else -q


def fx(fn: int, a: int, b: int) -> int:
    if fn == 0:
        return clamp(a + b)
    if fn == 1:
        return clamp(a - b)
    if fn in (2, 13):
        return clamp((a * b + 0x8000) >> 16)  # Python >> floors: ties toward +infinity
    if fn in (3, 7, 14):
        if b == 0:
            return I32_MAX if a > 0 else I32_MIN if a < 0 else 0
        return clamp(trunc_div(a * 65536, b))
    if fn == 4:
        if a <= 0:
            return 0
        n = a * 65536
        x = 1 << ((n.bit_length() + 1) // 2)
        while True:
            y = (x + n // x) // 2
            if y >= x:
                return x
            x = y
    if fn == 5:
        return clamp(-a)
    if fn == 6:
        return clamp(abs(a))
    if fn == 8:
        return I32_MAX if a > 32767 else I32_MIN if a < -32768 else a * 65536
    if fn == 9:
        return a >> 16
    if fn == 10:
        return -((-a) >> 16)
    if fn == 11:
        return (a + 0x8000) >> 16
    if fn == 12:
        return (a > b) - (a < b)
    raise ValueError(fn)


def rotl(x: int, k: int) -> int:
    return ((x << k) | (x >> (32 - k))) & M32


def mix32(z: int) -> int:
    z &= M32
    z ^= z >> 16
    z = (z * 0x85EBCA6B) & M32
    z ^= z >> 13
    z = (z * 0xC2B2AE35) & M32
    return z ^ (z >> 16)


def xo_seed(seed: int) -> list[int]:
    return [mix32(seed + (k + 1) * 0x9E3779B9) for k in range(4)]


def xo_next(s: list[int]) -> int:
    r = (rotl((s[1] * 5) & M32, 7) * 9) & M32
    t = (s[1] << 9) & M32
    s[2] ^= s[0]
    s[3] ^= s[1]
    s[1] ^= s[2]
    s[0] ^= s[3]
    s[2] ^= t
    s[3] = rotl(s[3], 11)
    return r


def xo_jump(s: list[int]) -> list[int]:
    acc = [0, 0, 0, 0]
    cur = list(s)
    for word in (0x8764000B, 0xF542D2D3, 0x6FA035C3, 0x77F2DB5B):
        for b in range(32):
            if word & (1 << b):
                acc = [a ^ c for a, c in zip(acc, cur)]
            xo_next(cur)
    return acc


def xo_split(s: list[int]) -> tuple[list[int], list[int]]:
    outs = [xo_next(s) for _ in range(4)]
    child = [mix32(o ^ c) for o, c in zip(outs, (0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A))]
    if child == [0, 0, 0, 0]:
        child = xo_seed(0)
    return s, child


def xo_below(s: list[int], bound: int) -> int:
    if bound < 1:
        raise Refused
    if bound == 1:
        return 0
    k = (bound - 1).bit_length()
    while True:
        v = xo_next(s) >> (32 - k)
        if v < bound:
            return v


def oracle_payload(op: int, r: R) -> W:
    w = W()
    if op == 1:
        items, stamp, obs = [], 0, W()
        count = 0
        for _ in range(r.u32()):
            tag = r.u8()
            if tag == 0:
                p, v = r.i32(), r.i32()
                items.append((p, stamp, v))
                stamp += 1
                continue
            count += 1
            if not items:
                obs.u8(0)
                continue
            best = min(items)
            if tag == 1:
                items.remove(best)
            obs.u8(1).i32(best[0]).i32(best[2])
        w.u32(count)
        w.b += obs.b
        w.u32(len(items))
    elif op == 2:
        present: dict[int, tuple[int, int]] = {}
        stamp = 0
        n = r.u32()
        w.u32(n)
        for _ in range(n):
            tag = r.u8()
            if tag in (0, 1, 2):
                k, p = r.i32(), r.i32()
                ok = (k >= 0 and k not in present) if tag == 0 else \
                     (k in present and p <= present[k][0]) if tag == 1 else k >= 0
                if ok:
                    present[k] = (p, stamp)
                    stamp += 1
                w.u8(0 if ok else 1)
            elif tag == 3:
                k = r.i32()
                w.u8(0).u8(1 if k in present else 0)
                present.pop(k, None)
            elif tag in (4, 5):
                w.u8(0)
                if not present:
                    w.u8(0)
                else:
                    k = min(present, key=lambda key: present[key])
                    w.u8(1).i32(k).i32(present[k][0])
                    if tag == 4:
                        del present[k]
            elif tag == 6:
                w.u8(0).u8(1 if r.i32() in present else 0)
            else:
                w.u8(0).u32(len(present))
    elif op == 3:
        n = r.u32()
        w.u32(n)
        for _ in range(n):
            fn, a, b = r.u8(), r.i32(), r.i32()
            w.i32(fx(fn, a, b))
    elif op == 5:
        sub = r.u8()
        if sub == 0:
            s = xo_seed(r.i32())
            n = r.u32()
            for x in s:
                w.i32(x)
            for _ in range(n):
                w.i32(xo_next(s))
        elif sub == 1:
            s = [r.i32() & M32 for _ in range(4)]
            n = r.u32()
            if s == [0, 0, 0, 0]:
                raise Refused
            for _ in range(n):
                w.i32(xo_next(s))
        elif sub == 2:
            s = xo_seed(r.i32())
            n = r.u32()
            parent, child = xo_split(s)
            for part in (parent, child):
                for x in part:
                    w.i32(x)
                for _ in range(n):
                    w.i32(xo_next(part))
        elif sub == 3:
            s = xo_seed(r.i32())
            bound, n = r.i32(), r.u32()
            for _ in range(n):
                w.i32(xo_below(s, bound))
            for x in s:
                w.i32(x)
        elif sub == 4:
            s = xo_jump(xo_seed(r.i32()))
            n = r.u32()
            for x in s:
                w.i32(x)
            for _ in range(n):
                w.i32(xo_next(s))
        elif sub == 5:
            s = xo_seed(r.i32())
            lo, hi, n = r.i32(), r.i32(), r.u32()
            for _ in range(n):
                w.i32(lo + xo_below(s, hi - lo))
    else:
        raise ValueError(op)
    return w


def grid_check(payload: bytes, out: bytes) -> list[str]:
    """Operation 4: the oracle checks optimal cost (Dijkstra), path validity and settle order, not the path."""
    r = R(payload)
    width, height = r.u16(), r.u16()
    cells = list(r.b[r.p:r.p + width * height])
    r.p += width * height
    sx, sy, gx, gy = r.i32(), r.i32(), r.i32(), r.i32()
    mode, hood, budget = r.u8(), r.u8(), r.i32()
    steps = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)] if hood == 8 else \
            [(0, -1), (1, 0), (0, 1), (-1, 0)]

    def nbrs(c):
        for dx, dy in steps:
            x, y = c[0] + dx, c[1] + dy
            if 0 <= x < width and 0 <= y < height and cells[y * width + x] > 0:
                yield (x, y), cells[y * width + x]

    dist = {(sx, sy): 0}
    pq = [(0, (sx, sy))]
    while pq:
        d, c = heapq.heappop(pq)
        if d > dist[c]:
            continue
        for n, w in nbrs(c):
            if d + w < dist.get(n, 1 << 62):
                dist[n] = d + w
                heapq.heappush(pq, (d + w, n))
    o = R(out)
    errors = []
    if mode == 2:
        count = o.u32()
        got = [((o.i32(), o.i32()), o.i32()) for _ in range(count)]
        if sorted(got) != sorted(dist.items()):
            errors.append("distances differ from Dijkstra")
        if any(a[1] > b[1] for a, b in zip(got, got[1:])):
            errors.append("settle order is not non-decreasing in cost")
        return errors
    outcome = o.u8()
    reachable = (gx, gy) in dist
    if outcome == 0:
        cost, plen = o.i32(), o.u32()
        path = [(o.i32(), o.i32()) for _ in range(plen)]
        expanded = o.u32()
        if not reachable or cost != dist[(gx, gy)]:
            errors.append(f"cost {cost} is not optimal {dist.get((gx, gy))}")
        if path[0] != (sx, sy) or path[-1] != (gx, gy):
            errors.append("path endpoints")
        total = 0
        for a, b in zip(path, path[1:]):
            step = dict(nbrs(a)).get(b)
            if step is None:
                errors.append(f"illegal step {a}->{b}")
                break
            total += step
        if total != cost:
            errors.append("path cost differs from reported cost")
        if expanded > budget or expanded < 1:
            errors.append("expanded outside [1, budget]")
    elif outcome == 1:
        expanded = o.u32()
        if reachable:
            errors.append("reported unreachable but the goal is reachable")
    elif outcome == 2:
        expanded = o.u32()
        if expanded != budget:
            errors.append("budget exhaustion must expand exactly the budget")
    else:
        errors.append(f"unknown outcome {outcome}")
    if o.p != len(out):
        errors.append("trailing bytes")
    return errors


def oracle(expected: bytes) -> int:
    cases = all_cases()
    r = R(expected)
    failures = 0
    checked = 0
    for cid, op, payload in cases:
        size = r.u32()
        rec_cid, rec_op, outcome = r.u32(), r.u16(), r.u16()
        body = r.b[r.p:r.p + size - 8]
        r.p += size - 8
        problems = []
        if (rec_cid, rec_op) != (cid, op):
            problems.append(f"record order: got case {rec_cid} op {rec_op}")
        elif op == 4:
            if outcome != 0:
                problems.append(f"outcome {outcome}")
            else:
                problems += grid_check(bytes(payload), body)
        else:
            try:
                want_outcome, want = 0, bytes(oracle_payload(op, R(bytes(payload))).b)
            except Refused:
                want_outcome, want = 1, b""
            if outcome != want_outcome:
                problems.append(f"outcome {outcome}, oracle {want_outcome}")
            elif body != want:
                problems.append(f"payload differs ({len(body)} bytes, oracle {len(want)})")
        checked += 1
        if problems:
            failures += 1
            print(f"lockstep-fixtures: case {cid} (op {op}): " + "; ".join(problems))
    if r.p != len(expected):
        print("lockstep-fixtures: trailing records after the last case")
        failures += 1
    print(f"lockstep-fixtures: oracle checked {checked} cases, {failures} failed")
    return 1 if failures else 0


def main(argv: list[str]) -> int:
    if len(argv) == 2 and argv[1] == "generate":
        CASES.parent.mkdir(parents=True, exist_ok=True)
        data = encode_cases(all_cases())
        CASES.write_bytes(data)
        print(f"lockstep-fixtures: wrote {len(all_cases())} cases, {len(data)} bytes to {CASES.relative_to(ROOT)}")
        return 0
    if len(argv) == 2 and argv[1] == "check":
        data = encode_cases(all_cases())
        if CASES.read_bytes() != data:
            print("lockstep-fixtures: cases.bin is stale; run scripts/lockstep-fixtures.py generate")
            return 1
        print(f"lockstep-fixtures: cases.bin is current ({len(all_cases())} cases, {len(data)} bytes)")
        return 0
    if len(argv) == 3 and argv[1] == "oracle":
        return oracle(Path(argv[2]).read_bytes())
    if len(argv) == 4 and argv[1] == "hex-to-bin":
        lines = [ln.strip() for ln in Path(argv[2]).read_text().splitlines() if ln.strip()]
        Path(argv[3]).write_bytes(b"".join(bytes.fromhex(ln) for ln in lines))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
