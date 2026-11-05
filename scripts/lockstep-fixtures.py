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


def sweep_cases():
    return [(10001, 10, W().i32(20261102).u32(100000).b)]


def scc_cases():
    def encode(nodes, table):
        w = W().u32(len(nodes))
        for v in nodes:
            w.i32(v)
        w.u32(len(table))
        for succ in table:
            w.u32(len(succ))
            for x in succ:
                w.i32(x)
        return w.b

    cases = [
        (6001, 6, encode([0, 1, 2, 3, 4], [[1], [2], [0, 3], [4], [3]])),          # a ring and a 2-cycle downstream
        (6002, 6, encode([0, 1, 2, 3], [[1], [2], [3], []])),                     # a chain: singletons, sink first
        (6003, 6, encode([0, 1, 2], [[0], [], [2, 1]])),                          # self-edges
        (6004, 6, encode([2, 0, 2, 1, 7], [[1], [0], [0, 9], [], [], [], [], [0]])),  # duplicates, edge outside the set
        (6005, 6, encode([], [])),                                                # empty
    ]
    for k, seed in enumerate([61, 62, 63]):
        rng = Lcg(seed)
        n = 12 + rng.below(9)
        table = [[rng.below(n) for _ in range(rng.below(4))] for _ in range(n)]
        cases.append((6006 + k, 6, encode(list(range(n)), table)))
    return cases


def union_find_cases():
    def encode(n, pairs):
        w = W().u32(n).u32(len(pairs))
        for a, b in pairs:
            w.i32(a).i32(b)
        return w.b

    cases = [
        (7001, 7, encode(6, [(5, 4), (3, 4), (0, 1), (4, 3), (2, 2)])),
        (7002, 7, encode(1, [(0, 0)])),
        (7003, 7, encode(4, [(0, 9)])),  # out of range: refused
    ]
    for k, seed in enumerate([71, 72]):
        rng = Lcg(seed)
        n = 25
        cases.append((7004 + k, 7, encode(n, [(rng.below(n), rng.below(n)) for _ in range(30)])))
    return cases


def percentile_cases():
    w = W()
    triples = [(7, 100, 100), (95, 100, 30), (50, 100, 4), (0, 100, 9), (100, 100, 9), (1, 3, 2_000_000_000),
               (46339, 46340, 2_147_483_647), (99, 100, 1), (1, 46340, 46341)]
    rng = Lcg(81)
    for _ in range(40):
        den = 1 + rng.below(46340)
        triples.append((rng.below(den + 1), den, 1 + rng.below(2_000_000_000)))
    w.u32(len(triples))
    for t in triples:
        w.i32(t[0]).i32(t[1]).i32(t[2])
    samples = [rng.below(1000) - 500 for _ in range(37)]
    w.u32(len(samples))
    for x in samples:
        w.i32(x)
    ps = [0, 1, 7, 25, 50, 75, 90, 95, 99, 100]
    w.u32(len(ps))
    for p in ps:
        w.i32(p)
    return [(8001, 8, w.b), (8002, 8, W().u32(1).i32(3).i32(2).i32(5).u32(1).i32(1).u32(0).b)]  # 8002 refused


def arbitration_cases():
    def encode(ops):
        w = W().u32(len(ops))
        for op in ops:
            w.u8(op[0])
            if op[0] == 0:
                w.i32(op[1]).i32(op[2]).u8(op[3])
            elif op[0] == 1:
                w.i32(op[1]).i32(op[2])
            else:
                w.i32(op[1])
        return w.b

    chain = [(0, 4, 1, 1), (0, 2, 2, 1), (0, 0, 3, 1), (3, 0), (3, 2)]
    perms = [(0, 4, 1, 0), (0, 2, 2, 0), (1, 4, 2), (1, 3, 2), (1, 0, 1), (1, 9, 77), (0, 5, 2, 0), (2, 3), (4, 99)]
    cases = [(9001, 9, encode(chain)), (9002, 9, encode(perms))]
    for k, seed in enumerate([91, 92, 93]):
        rng = Lcg(seed)
        ops, key = [], 0
        for _ in range(80):
            r = rng.below(10)
            if r < 4:
                key += 1
                ops.append((0, rng.below(6), key, 1 if rng.below(3) else 0))
            elif r < 5:
                ops.append((1, rng.below(6), 1 + rng.below(max(1, key))))
            elif r < 6:
                ops.append((2, rng.below(6)))
            elif r < 7:
                ops.append((3, rng.below(6)))
            else:
                ops.append((4, 1 + rng.below(max(1, key))))
        cases.append((9003 + k, 9, encode(ops)))
    return cases


def topo_cases():
    def encode(nodes, table, durations):
        w = W().u32(len(nodes))
        for v in nodes:
            w.i32(v)
        w.u32(len(table))
        for succ in table:
            w.u32(len(succ))
            for x in succ:
                w.i32(x)
        w.u32(len(durations))
        for d in durations:
            w.i32(d)
        return w.b

    cases = [
        (11001, 11, encode([0, 1, 2, 3, 4, 5], [[2, 3], [3], [4], [4, 5], [], []], [3, 2, 4, 1, 2, 6])),  # CPM textbook shape
        (11002, 11, encode([3, 0, 2, 1], [[1], [2], [3], []], [1, 1, 1, 1])),                            # chain given out of order
        (11003, 11, encode([0, 1, 2, 3], [[1], [2], [0], [3]], [1, 1, 1, 1])),                            # a ring and a self-edge
        (11004, 11, encode([0, 1, 2], [[1, 1, 9], [2], []], [0, 0, 0])),                                  # repeated and outside edges, zero durations
        (11005, 11, encode([], [], [])),
    ]
    for k, seed in enumerate([111, 112, 113]):
        rng = Lcg(seed)
        n = 15 + rng.below(10)
        table = [[w for w in (rng.below(n) for _ in range(rng.below(4))) if w > v] for v in range(n)]
        nodes = list(range(n))
        for i in range(n - 1, 0, -1):  # a fixed shuffle: the order must come from `compare`, not the node list
            j = rng.below(i + 1)
            nodes[i], nodes[j] = nodes[j], nodes[i]
        cases.append((11006 + k, 11, encode(nodes, table, [rng.below(10) for _ in range(n)])))
    # Two equally critical predecessors of 2 (both finish at 3). The node list and 1's edge come first, but the
    # trace takes the FIRST predecessor in topological order, which is 0 under compare (review note on #4).
    cases.append((11009, 11, encode([1, 0, 2], [[2], [2], []], [3, 3, 1])))
    return cases


def bt_encode(tree, w):
    kind = tree[0]
    w.u8(kind)
    if kind == 0:
        w.i32(tree[1])
    elif kind in (1, 2, 9, 10):
        w.u32(len(tree[1]))
        for c in tree[1]:
            bt_encode(c, w)
    elif kind == 3:
        w.i32(tree[1]).i32(tree[2]).u32(len(tree[3]))
        for c in tree[3]:
            bt_encode(c, w)
    elif kind in (4, 5, 6):
        bt_encode(tree[1], w)
    else:
        w.i32(tree[1])
        bt_encode(tree[2], w)


def bt_random(rng, depth):
    r = rng.below(12)
    if depth == 0 or r < 3:
        return (0, rng.below(6))
    if r < 9:
        kind = [1, 1, 2, 2, 3, 4][r - 3]
        n = rng.below(4) + (1 if kind == 3 else 0)
        kids = [bt_random(rng, depth - 1) for _ in range(n)]
        if kind == 3:
            return (3, 1 + rng.below(n), 1 + rng.below(n), kids)
        if kind == 4:
            return (4, bt_random(rng, depth - 1))
        return (kind, kids)
    if r == 9:
        return (5 + rng.below(2), bt_random(rng, depth - 1))
    return (7 + rng.below(2), 1 + rng.below(3), bt_random(rng, depth - 1))


def bt_cases():
    def encode(tree, scripts, ticks):
        w = W()
        bt_encode(tree, w)
        w.u32(len(scripts))
        for sc in scripts:
            w.u32(len(sc))
            for x in sc:
                w.u8(x)
        w.u32(ticks)
        return w.b

    S, F, R = 0, 1, 2
    six = lambda *xs: list(xs) + [[S]] * (6 - len(xs))
    cases = [
        # resumption: check, walk (running twice), open
        (12001, 12, encode((1, [(0, 0), (0, 1), (0, 2)]), six([S], [R, R, S], [S]), 4)),
        # selector succeeds fast; sequence fails fast
        (12002, 12, encode((2, [(0, 0), (0, 1), (0, 2)]), six([F], [S], [S]), 2)),
        (12003, 12, encode((1, [(0, 0), (0, 1), (0, 2)]), six([S], [F], [S]), 2)),
        # parallel halts a running branch
        (12004, 12, encode((3, 1, 2, [(0, 0), (1, [(0, 1), (0, 2)])]), six([R, S], [S], [R]), 3)),
        # decorators, repeat and retry
        (12005, 12, encode((1, [(4, (0, 0)), (5, (0, 1)), (6, (0, 2))]), six([F], [F], [S]), 2)),
        (12006, 12, encode((7, 3, (0, 0)), six([S, S, R, S]), 4)),
        (12007, 12, encode((8, 3, (0, 0)), six([F, F, R, F, S]), 4)),
        # empty composites
        (12008, 12, encode((2, [(1, []), (2, [])]), six(), 1)),
    ]
    for k, seed in enumerate([121, 122, 123, 124]):
        rng = Lcg(seed)
        tree = bt_random(rng, 4)
        scripts = [[rng.below(3) for _ in range(1 + rng.below(4))] for _ in range(6)]
        cases.append((12009 + k, 12, encode(tree, scripts, 8)))
    return cases


def bt_random_reactive(rng, depth):
    """Random trees for the reactive cases: every kind, reactive composites (9, 10) included. A separate
    generator, so bt_random and the cases it made stay byte-identical."""
    r = rng.below(16)
    if depth == 0 or r < 3:
        return (0, rng.below(6))
    if r < 13:
        kind = [1, 2, 3, 4, 9, 9, 9, 10, 10, 10][r - 3]
        if kind == 4:
            return (4, bt_random_reactive(rng, depth - 1))
        n = rng.below(4) + (1 if kind == 3 else 0)
        kids = [bt_random_reactive(rng, depth - 1) for _ in range(n)]
        if kind == 3:
            return (3, 1 + rng.below(n), 1 + rng.below(n), kids)
        return (kind, kids)
    if r == 13:
        return (5 + rng.below(2), bt_random_reactive(rng, depth - 1))
    return (7 + rng.below(2), 1 + rng.below(3), bt_random_reactive(rng, depth - 1))


def bt_reactive_cases():
    """Operation 12 cases for ReactiveSequence (9) and ReactiveFallback (10), FS.GG.Algorithms#8. Appended after
    every earlier case, so the earlier records stay a byte-identical prefix."""
    def encode(tree, scripts, ticks):
        w = W()
        bt_encode(tree, w)
        w.u32(len(scripts))
        for sc in scripts:
            w.u32(len(sc))
            for x in sc:
                w.u8(x)
        w.u32(ticks)
        return w.b

    S, F, R = 0, 1, 2
    six = lambda *xs: list(xs) + [[S]] * (6 - len(xs))
    deep = (0, 2)
    for _ in range(6):
        deep = (9, [(0, 1), deep])
    cases = [
        # priority interrupt: ReactiveFallback [ReactiveSequence [danger; flee]; Sequence [walkA; walkB]]; danger
        # appears on tick 3 and clears on tick 4: patrol is halted, then restarts at walkA
        (12013, 12, encode((10, [(9, [(0, 0), (0, 1)]), (1, [(0, 2), (0, 3)])]), six([F, F, S, F, F], [R], [S], [R]), 6)),
        # a failing guard aborts a running action with memory (a sequence and a repeat counter) and halts it
        (12014, 12, encode((9, [(0, 0), (1, [(0, 1), (7, 3, (0, 2))])]), six([S, S, F, S], [S], [S, R, S, R]), 5)),
        # a higher-priority child that RUNS also preempts
        (12015, 12, encode((10, [(0, 0), (0, 1), (0, 2)]), six([F, F, R, F], [F, R, F], [R]), 6)),
        # reactive composites inside a parallel, and a parallel inside a reactive composite
        (12016, 12, encode((3, 1, 2, [(9, [(0, 0), (0, 1)]), (10, [(0, 2), (3, 2, 1, [(0, 3), (0, 4)])])]),
                           six([S, S, F], [R, S], [F, F, S], [R, R, S], [R, F]), 6)),
        # nested reactive composites under a memory sequence and a repeat
        (12017, 12, encode((1, [(0, 5), (7, 2, (10, [(4, (0, 0)), (9, [(0, 1), (0, 2)])]))]),
                           six([S, F, S, F, F], [S, S, F], [R, S, R]), 8)),
        # empty reactive composites
        (12018, 12, encode((2, [(10, []), (9, [])]), six(), 1)),
        # an interrupt halts a 6-deep running reactive branch (a cursor at every level)
        (12019, 12, encode((10, [(0, 0), deep]), six([F, F, S, F], [S], [R]), 5)),
    ]
    for k, seed in enumerate([131, 132, 133, 134, 135, 136, 137, 138]):
        rng = Lcg(seed)
        tree = bt_random_reactive(rng, 4)
        scripts = [[rng.below(3) for _ in range(1 + rng.below(5))] for _ in range(6)]
        cases.append((12020 + k, 12, encode(tree, scripts, 12)))
    return cases


def bt_halt_cases():
    """Operation 15 is additive: the existing operation 12 bytes and cases are unchanged. Replay its shapes
    with explicit halt observations, then add event-order, repeated-activation and active-path boundaries."""
    cases = [(15001 + i, 15, payload) for i, (_, _, payload) in enumerate(bt_cases() + bt_reactive_cases())]

    def encode(tree, scripts, ticks):
        w = W()
        bt_encode(tree, w)
        w.u32(len(scripts))
        for sc in scripts:
            w.u32(len(sc))
            for status in sc:
                w.u8(status)
        return w.u32(ticks).b

    S, F, R = 0, 1, 2
    cases += [
        # Inner halt occurs before an outer halt at a lower preorder index; duplicate leaf values.
        (15028, 15, encode((3, 1, 2, [(0, 0), (1, [(0, 1), (3, 1, 2, [(0, 2), (0, 0)])])]), [[R], [S], [S]], 3)),
        # A Repeat restarts a finishing Parallel twice in the same tick: repeated halt indices.
        (15029, 15, encode((7, 2, (3, 1, 2, [(0, 0), (0, 1)])), [[R], [S]], 3)),
        # Preemption clears a Parallel: only running children, not completed or never-ticked leaves.
        (15030, 15, encode((10, [(0, 0), (3, 3, 3, [(0, 1), (0, 2), (1, [(0, 3), (0, 4), (0, 5)])])]),
                           [[F, S, F, S], [R], [S], [S], [R], [S]], 6)),
        (15031, 15, encode((0, 0), [[R, S, F]], 6)),
        (15032, 15, encode((10, []), [], 2)),
    ]
    return cases


def door_case(cid, planes, side, walk, edges, stairs, diagonal, ortho, diag, start, goal, heuristic, budget):
    w = W().u8(planes).u8(side)
    w.b += bytes(walk)
    w.u32(len(edges))
    for (p, x, y, d, kind, extra) in edges:
        w.i32(p).i32(x).i32(y).u8(d).u8(kind).i32(extra)
    w.u32(len(stairs))
    for (a, b, c) in stairs:
        for v in a + b:
            w.i32(v)
        w.i32(c)
    w.u8(1 if diagonal else 0).i32(ortho).i32(diag)
    for v in start + goal:
        w.i32(v)
    w.u8(1 if heuristic else 0).i32(budget)
    return (cid, 13, w.b)


def door_cases():
    cases = [
        # a door on the direct edge: go around it (and the diagonal is refused next to the door)
        door_case(13001, 1, 2, [1] * 4, [(0, 0, 0, 0, 2, 7)], [], True, 10, 14, (0, 0, 0), (0, 1, 1), True, I32_MAX),
        # a wall forces the door; Dijkstra and A* agree
        door_case(13002, 1, 3, [1] * 9, [(0, 0, 0, 1, 1, 0), (0, 0, 0, 0, 2, 3)], [], False, 1, 1, (0, 0, 0), (0, 0, 2), False, I32_MAX),
        # planes: the stair is the only way up
        door_case(13003, 2, 3, [1] * 18, [], [((0, 2, 2), (1, 2, 2), 5)], False, 1, 1, (0, 0, 0), (1, 0, 0), True, I32_MAX),
        # unreachable, and a budget
        door_case(13004, 1, 3, [1, 1, 1, 0, 0, 0, 1, 1, 1], [], [], True, 10, 14, (0, 0, 0), (0, 0, 2), True, I32_MAX),
        door_case(13005, 1, 4, [1] * 16, [], [], True, 10, 14, (0, 0, 0), (0, 3, 3), True, 2),
    ]
    for k, seed in enumerate([131, 132, 133, 134, 135, 136, 137, 138]):
        rng = Lcg(seed)
        planes, side = 1 + rng.below(3), 4 + rng.below(4)
        walk = [1 if rng.below(9) else 0 for _ in range(planes * side * side)]
        edges = []
        for p in range(planes):
            for y in range(side):
                for x in range(side):
                    for d in (0, 1):
                        if (d == 0 and x + 1 < side) or (d == 1 and y + 1 < side):
                            r = rng.below(12)
                            if r < 1:
                                edges.append((p, x, y, d, 1, 0))
                            elif r < 3:
                                edges.append((p, x, y, d, 2, rng.below(20)))
        stairs = []
        for _ in range(rng.below(4)):
            p, q = rng.below(planes), rng.below(planes)
            x, y = rng.below(side), rng.below(side)
            x2 = max(0, min(side - 1, x + rng.below(3) - 1))
            stairs.append(((p, x, y), (q, x2, y), rng.below(10) + abs(x2 - x) * 10))
        start = (rng.below(planes), 0, rng.below(side))
        goal = (rng.below(planes), side - 1, rng.below(side))
        stairs += [((p, 0, 0), (p + 1, 0, 0), 4) for p in range(planes - 1)]  # a guaranteed way up from each plane
        for (p, x, y) in (start, goal):
            walk[(p * side + y) * side + x] = 1
        cases.append(door_case(13006 + k, planes, side, walk, edges, stairs, k % 2 == 0, 10, 14, start, goal, k < 2, I32_MAX))
    return cases


def kd_cases():
    def encode(points, queries):
        w = W().u32(len(points))
        for (x, y) in points:
            w.i32(x).i32(y)
        w.u32(len(queries))
        for q in queries:
            w.u8(q[0])
            for v in (list(q[1:]) + [0, 0, 0, 0])[:4]:
                w.i32(v)
        return w.b

    lo, hi = -16383, 16383
    ties = [(0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (0, 1), (2, 0)]
    cases = [
        (14001, 14, encode(ties, [(0, 0, 0), (1, 3, 0, 0), (2, 1, 0, 0), (3, -1, -1, 1, 1), (1, 0, 5, 5)])),
        (14002, 14, encode([(lo, lo), (hi, hi), (lo, hi)], [(0, hi, hi - 1), (2, 46340, lo, lo), (1, 2, 0, 0)])),
        (14003, 14, encode([], [(0, 0, 0), (1, 3, 0, 0), (2, 5, 0, 0), (3, 0, 0, 1, 1)])),
    ]
    for k, seed in enumerate([141, 142, 143]):
        rng = Lcg(seed)
        span = (6, 300, 16383)[k]
        pts = [(rng.below(2 * span + 1) - span, rng.below(2 * span + 1) - span) for _ in range(120)]
        queries = []
        for _ in range(12):
            qx, qy = rng.below(2 * span + 1) - span, rng.below(2 * span + 1) - span
            kind = rng.below(4)
            if kind == 0:
                queries.append((0, qx, qy))
            elif kind == 1:
                queries.append((1, rng.below(8), qx, qy))
            elif kind == 2:
                queries.append((2, rng.below(max(2, span // 2)), qx, qy))
            else:
                queries.append((3, qx, qy, min(hi, qx + rng.below(span + 1)), min(hi, qy + rng.below(span + 1))))
        cases.append((14004 + k, 14, encode(pts, queries)))
    return cases


def door_cost_cases():
    """Cost pairs where octile is NOT a valid bound (review of FS.GG.Algorithms#6): diagonal < orthogonal, so
    zigzags beat straight lines, and diagonal > 2 x orthogonal, so diagonals never pay."""
    open4 = [1] * 16
    cases = [
        door_case(13014, 1, 4, open4, [], [], True, 10, 1, (0, 0, 0), (0, 3, 1), True, I32_MAX),   # the counterexample
        door_case(13015, 1, 4, open4, [], [], True, 10, 1, (0, 0, 0), (0, 3, 0), True, I32_MAX),   # odd parity
        door_case(13016, 1, 4, open4, [], [], True, 1, 5, (0, 0, 0), (0, 3, 3), True, I32_MAX),    # diagonals useless
    ]
    for k, seed in enumerate([151, 152, 153]):
        rng = Lcg(seed)
        side = 5 + rng.below(3)
        walk = [1 if rng.below(7) else 0 for _ in range(side * side)]
        walk[0] = walk[side * side - 1] = 1
        ortho, diag = [(10, 1), (7, 3), (2, 9)][k]
        cases.append(door_case(13017 + k, 1, side, walk, [], [], True, ortho, diag, (0, 0, 0), (0, side - 1, side - 1), True, I32_MAX))
    return cases


def utility_cases():
    vectors = [[], [0], [65536], [0, 0], [65536, 65536], [1, 32768],
               [I32_MIN, -1, 0, 1, 32767, 32768, 32769, 65535, 65536, 65537, I32_MAX],
               list(range(0, 65537, 257)) + [65536]]
    rng = Lcg(16001)
    for n in [3, 7, 32, 100, 1000]:
        vectors.append([s32(rng.next()) if i % 3 == 0 else rng.below(65537) for i in range(n)])
    cases = []
    for i, values in enumerate(vectors):
        w = W().u32(len(values))
        for v in values:
            w.i32(v)
        cases.append((16001 + i, 16, w.b))
    return cases


def all_cases():
    # Order is append-only: PR 1's cases (operations 1-5 and the operation 10 sweep) stay a byte-identical prefix.
    return (heap_script_cases() + indexed_script_cases() + fixed_cases() + grid_cases() + xoshiro_cases()
            + sweep_cases() + scc_cases() + union_find_cases() + percentile_cases() + arbitration_cases()
            + topo_cases() + bt_cases() + door_cases() + kd_cases() + door_cost_cases() + bt_reactive_cases() + bt_halt_cases() + utility_cases())


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


def py_tarjan(nodes, table):
    """Recursive Tarjan in Python (independent oracle): vertices are the distinct nodes in first-occurrence order."""
    sys.setrecursionlimit(10000)
    verts = []
    for v in nodes:
        if v not in verts:
            verts.append(v)
    pos = {v: i for i, v in enumerate(verts)}
    succ = [[pos[w] for w in (table[v] if 0 <= v < len(table) else []) if w in pos] for v in verts]
    index, low, on, stack, out, counter = {}, {}, set(), [], [], [0]

    def strong(v):
        index[v] = low[v] = counter[0]
        counter[0] += 1
        stack.append(v)
        on.add(v)
        for w in succ[v]:
            if w not in index:
                strong(w)
                low[v] = min(low[v], low[w])
            elif w in on:
                low[v] = min(low[v], index[w])
        if low[v] == index[v]:
            comp = []
            while True:
                w = stack.pop()
                on.discard(w)
                comp.insert(0, w)
                if w == v:
                    break
            out.append(comp)

    for v in range(len(verts)):
        if v not in index:
            strong(v)
    comps = [[verts[i] for i in c] for c in out]
    cycles = [[verts[i] for i in c] for c in out if len(c) > 1 or c[0] in succ[c[0]]]
    cid = {}
    for k, c in enumerate(out):
        for i in c:
            cid[verts[i]] = k
    return comps, cycles, [cid[v] for v in nodes]


def py_rank(num, den, n):
    if n < 1 or not (1 <= den <= 46340) or not (0 <= num <= den):
        raise Refused
    return max(1, -(-num * n // den))


ARB_BAND = lambda owner: 3 - owner // 2


def py_arbitrate(ops):
    """An independent Python model of the arbitration rules (fixture policy of operation 9)."""
    entries, seq, out = [], 1, []

    def key_of(e):
        return (-e["band"], -e["seq"] if e["band"] == 3 else e["seq"])

    def settle():
        cur = next((e for e in entries if e["status"] == 1), None)
        if not entries:
            return []
        win = min(entries, key=key_of)
        if cur is not None and cur["key"] == win["key"]:
            return []
        ds = []
        if cur is not None:
            ds.append(("pre", cur["key"], win["key"]))
            cur["status"] = 2
        ds.append(("act", win["key"], win["started"]))
        win["status"], win["started"] = 1, True
        return ds

    def end(pick, code, by):
        nonlocal entries
        gone = [e for e in entries if pick(e)]
        entries = [e for e in entries if not pick(e)]
        return [("end", e["key"], code, by) for e in gone]

    for op in ops:
        ds = []
        if op[0] == 0:
            _, owner, key, replace = op
            if any(e["key"] == key for e in entries):
                ds = [("ref", 0, key)]
            else:
                band = ARB_BAND(owner)
                saved = [dict(e) for e in entries]
                ended = end(lambda e: e["owner"] == owner, 2, -1) if replace else []
                waiting = sum(1 for e in entries if e["band"] == band and e["status"] != 1)
                if waiting >= (16 if band == 3 else 6):
                    entries = saved
                    ds = [("ref", 3, -1)]
                else:
                    entries.append({"key": key, "owner": owner, "band": band, "seq": seq, "status": 0, "started": False})
                    seq += 1
                    ds = [("acc", key)] + ended + settle()
        elif op[0] == 1:
            _, by, key = op
            e = next((e for e in entries if e["key"] == key), None)
            if e is None:
                ds = [("ref", 1, key)]
            elif e["owner"] != by and ARB_BAND(by) <= e["band"]:
                ds = [("ref", 2, key)]
            else:
                ds = end(lambda x: x["key"] == key, 1, by) + settle()
        elif op[0] == 2:
            by = op[1]
            ended = end(lambda e: e["band"] <= ARB_BAND(by), 4, by)
            ds = [("all", by, len(ended))] + ended + settle()
        elif op[0] == 3:
            ds = end(lambda e: e["owner"] == op[1], 3, -1) + settle()
        else:
            act = next((e for e in entries if e["status"] == 1), None)
            if act is not None and act["key"] == op[1]:
                ds = end(lambda e: e["key"] == op[1], 0, -1) + settle()
        out.append(ds)
    return out, entries


def py_topo(nodes, table, durations):
    """Kahn with a min-heap on int nodes (the oracle's own heapq), and CPM by definition."""
    verts = []
    for v in nodes:
        if v not in verts:
            verts.append(v)
    vs = set(verts)
    succ = {v: [w for w in (table[v] if 0 <= v < len(table) else []) if w in vs] for v in verts}
    indeg = {v: 0 for v in verts}
    for v in verts:
        for w in succ[v]:
            indeg[w] += 1
    ready = [v for v in verts if indeg[v] == 0]
    heapq.heapify(ready)
    order = []
    while ready:
        v = heapq.heappop(ready)
        order.append(v)
        for w in succ[v]:
            indeg[w] -= 1
            if indeg[w] == 0:
                heapq.heappush(ready, w)
    if len(order) < len(verts):
        return None, py_tarjan(nodes, table)[1]
    dur = {v: (durations[v] if 0 <= v < len(durations) else 0) for v in verts}
    preds = {v: [u for u in order for w in succ[u] if w == v] for v in verts}
    es, ef = {}, {}
    for v in order:
        es[v] = max([ef[p] for p in preds[v]], default=0)
        ef[v] = es[v] + dur[v]
    makespan = max(ef.values(), default=0)
    lf, ls = {}, {}
    for v in reversed(order):
        lf[v] = min([ls[w] for w in succ[v]], default=makespan)
        ls[v] = lf[v] - dur[v]
    path = []
    if order:
        cur = next(v for v in order if ef[v] == makespan)
        path = [cur]
        while True:
            cand = [p for p in order if p in preds[cur] and ef[p] == es[cur]]
            if not cand:
                break
            cur = cand[0]
            path.insert(0, cur)
    acts = [(v, dur[v], es[v], ef[v], ls[v], lf[v], ls[v] - es[v]) for v in order]
    return (order, makespan, path, acts), None


def bt_decode(r):
    kind = r.u8()
    if kind == 0:
        return {"kind": 0, "leaf": r.i32(), "kids": []}
    if kind in (1, 2, 9, 10):
        return {"kind": kind, "kids": [bt_decode(r) for _ in range(r.u32())]}
    if kind == 3:
        sa, fa = r.i32(), r.i32()
        return {"kind": 3, "sa": sa, "fa": fa, "kids": [bt_decode(r) for _ in range(r.u32())]}
    if kind in (4, 5, 6):
        return {"kind": kind, "kids": [bt_decode(r)]}
    n = r.i32()
    return {"kind": kind, "n": n, "kids": [bt_decode(r)]}


def bt_number(node, start):
    """Preorder indices and subtree ends, by the oracle's own walk."""
    node["index"] = start
    nxt = start + 1
    for c in node["kids"]:
        nxt = bt_number(c, nxt)
    node["end"] = nxt
    return nxt


def py_bt_tick(root, cursor, fin, script, calls, trace, running=None, halted=None):
    """The oracle's recursive interpreter of the documented BehaviourTree semantics."""
    def clear(t):
        for k in range(t["index"], t["end"]):
            if running is not None and k in running:
                running.remove(k)
                halted.append(k)
            cursor[k] = 0
            if k > t["index"]:
                fin[k] = 0

    def done(t, s):
        if s != 2:
            clear(t)
        return s

    def ev(t):
        k, i = t["kids"], t["index"]
        kind = t["kind"]
        if kind == 0:
            l = t["leaf"]
            sc = script[l]
            s = sc[calls[l] % len(sc)]
            calls[l] += 1
            trace.append(i)
            if running is not None:
                if s == 2:
                    running.add(i)
                else:
                    running.discard(i)
            return done(t, s)
        if kind in (1, 2):
            go = 0 if kind == 1 else 1
            while True:
                c = cursor[i]
                if c >= len(k):
                    return done(t, 0 if kind == 1 else 1)
                s = ev(k[c])
                if s == 2:
                    return 2
                if s == go:
                    cursor[i] = c + 1
                else:
                    return done(t, s)
        if kind == 3:
            for c in k:
                if fin[c["index"]] == 0:
                    s = ev(c)
                    if s != 2:
                        fin[c["index"]] = 1 if s == 0 else 2
            ns = sum(1 for c in k if fin[c["index"]] == 1)
            nf = sum(1 for c in k if fin[c["index"]] == 2)
            if ns >= t["sa"]:
                return done(t, 0)
            if nf >= t["fa"]:
                return done(t, 1)
            if ns + nf == len(k):
                return done(t, 1)
            return 2
        if kind in (4, 5, 6):
            s = ev(k[0])
            if s == 2:
                return 2
            return done(t, {4: 1 - s, 5: 0, 6: 1}[kind])
        if kind in (9, 10):
            # Reactive: always from the first child. The child that does not proceed takes over, and every
            # child after it is abandoned, so all of them are halted (cleared).
            go = 0 if kind == 9 else 1
            for c_idx, c in enumerate(k):
                s = ev(c)
                if s == go:
                    continue
                for later in k[c_idx + 1:]:
                    clear(later)
                if s == 2:
                    cursor[i] = c_idx
                    return 2
                return done(t, s)
            return done(t, go)
        counted = 0 if kind == 7 else 1
        while True:
            s = ev(k[0])
            if s == 2:
                return 2
            if s == counted:
                cursor[i] += 1
                if cursor[i] >= t["n"]:
                    return done(t, counted)
            else:
                return done(t, s)

    return ev(root)


def door_decode(payload):
    r = R(payload)
    planes, side = r.u8(), r.u8()
    walk = list(r.b[r.p:r.p + planes * side * side])
    r.p += planes * side * side
    edges = {}
    for _ in range(r.u32()):
        p, x, y, d, kind, extra = r.i32(), r.i32(), r.i32(), r.u8(), r.u8(), r.i32()
        a, b = (p, x, y), ((p, x + 1, y) if d == 0 else (p, x, y + 1))
        v = ("open",) if kind == 0 else ("blocked",) if kind == 1 else ("door", extra)
        edges[(a, b)] = edges[(b, a)] = v
    stairs = [((r.i32(), r.i32(), r.i32()), (r.i32(), r.i32(), r.i32()), r.i32()) for _ in range(r.u32())]
    diagonal = r.u8() == 1
    ortho, diag = r.i32(), r.i32()
    start, goal = (r.i32(), r.i32(), r.i32()), (r.i32(), r.i32(), r.i32())
    heuristic, budget = r.u8(), r.i32()

    def walkable(t):
        p, x, y = t
        return 0 <= p < planes and 0 <= x < side and 0 <= y < side and walk[(p * side + y) * side + x] == 1

    def moves(t):
        p, x, y = t
        out = []
        for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
            n = (p, x + dx, y + dy)
            if walkable(n):
                e = edges.get((t, n), ("open",))
                if e[0] == "open":
                    out.append((n, ortho))
                elif e[0] == "door":
                    out.append((n, ortho + e[1]))
        if diagonal:
            for dx, dy in ((1, -1), (1, 1), (-1, 1), (-1, -1)):
                n, sx, sy = (p, x + dx, y + dy), (p, x + dx, y), (p, x, y + dy)
                op = lambda a, b: edges.get((a, b), ("open",))[0] == "open"
                if walkable(n) and walkable(sx) and walkable(sy) and op(t, sx) and op(t, sy) and op(sx, n) and op(sy, n):
                    out.append((n, diag))
        out += [(b, c) for (a, b, c) in stairs if a == t]
        return out

    return planes, side, walkable, moves, start, goal, budget


def door_check(payload, out):
    planes, side, walkable, moves, start, goal, budget = door_decode(payload)
    o = R(out)
    errors = []
    for p in range(planes):
        for y in range(side):
            for x in range(side):
                t = (p, x, y)
                if walkable(t):
                    got = [((o.i32(), o.i32(), o.i32()), o.i32()) for _ in range(o.u32())]
                    if got != moves(t):
                        errors.append(f"neighbours of {t} differ")
    dist = {start: 0}
    pq = [(0, start)]
    while pq:
        d, c = heapq.heappop(pq)
        if d > dist[c]:
            continue
        for n, w in moves(c):
            if d + w < dist.get(n, 1 << 62):
                dist[n] = d + w
                heapq.heappush(pq, (d + w, n))
    outcome = o.u8()
    if outcome == 0:
        cost, plen = o.i32(), o.u32()
        path = [(o.i32(), o.i32(), o.i32()) for _ in range(plen)]
        expanded = o.u32()
        if cost != dist.get(goal):
            errors.append(f"cost {cost} is not optimal {dist.get(goal)}")
        if path[0] != start or path[-1] != goal:
            errors.append("path endpoints")
        total = 0
        for a, b in zip(path, path[1:]):
            options = [w for n, w in moves(a) if n == b]
            if not options:
                errors.append(f"illegal move {a}->{b}")
                break
            total += min(options)
        if total != cost:
            errors.append("path cost differs")
        if not 1 <= expanded <= budget:
            errors.append("expanded outside [1, budget]")
    elif outcome == 1:
        o.u32()
        if goal in dist:
            errors.append("reported unreachable")
    else:
        if o.u32() != budget:
            errors.append("budget exhaustion must expand exactly the budget")
    if o.p != len(out):
        errors.append("trailing bytes")
    return errors


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
    elif op == 10:
        st = xo_seed(r.i32())
        pairs = r.u32()
        h = [0x811C9DC5] * 9

        def draw():
            return s32(xo_next(st))

        for _ in range(pairs):
            a0 = draw()
            a = a0 >> (draw() & 15)
            b0 = draw()
            b = b0 >> (draw() & 15)
            results = [fx(0, a, b), fx(1, a, b), fx(2, a, b), fx(3, a, b), fx(7, a, b), fx(4, a, 0), fx(5, a, 0),
                       fx(11, a, 0), fx(12, a, b)]
            for k, v in enumerate(results):
                h[k] = ((h[k] ^ (v & M32)) * 16777619) & M32
        w.u32(pairs)
        for x in h:
            w.i32(x)
    elif op == 6:
        nodes = [r.i32() for _ in range(r.u32())]
        table = [[r.i32() for _ in range(r.u32())] for _ in range(r.u32())]
        comps, cycles, ids = py_tarjan(nodes, table)
        for ls in (comps, cycles):
            w.u32(len(ls))
            for l in ls:
                w.u32(len(l))
                for x in l:
                    w.i32(x)
        w.u32(len(ids))
        for x in ids:
            w.i32(x)
    elif op == 7:
        n, m = r.u32(), r.u32()
        pairs = [(r.i32(), r.i32()) for _ in range(m)]
        label = list(range(n))
        w.u32(m)
        for a, b in pairs:
            if not (0 <= a < n and 0 <= b < n):
                raise Refused
            la, lb = label[a], label[b]
            w.u8(1 if la != lb else 0)
            if la != lb:
                lo, hi = min(la, lb), max(la, lb)
                label = [lo if x == hi else x for x in label]
        for i in range(n):
            w.i32(label[i])
        groups = sorted({x for x in label})
        w.u32(len(groups))
        w.u32(len(groups))
        for g in groups:
            members = [i for i in range(n) if label[i] == g]
            w.u32(len(members))
            for x in members:
                w.i32(x)
    elif op == 8:
        k = r.u32()
        w.u32(k)
        for _ in range(k):
            num, den, n = r.i32(), r.i32(), r.i32()
            w.i32(py_rank(num, den, n))
        samples = sorted(r.i32() for _ in range(r.u32()))
        ps = r.u32()
        w.u32(ps)
        for _ in range(ps):
            p = r.i32()
            if not samples or not (0 <= p <= 100):
                raise Refused
            w.i32(samples[py_rank(p, 100, len(samples)) - 1])
    elif op == 9:
        ops = []
        for _ in range(r.u32()):
            tag = r.u8()
            if tag == 0:
                ops.append((0, r.i32(), r.i32(), r.u8()))
            elif tag == 1:
                ops.append((1, r.i32(), r.i32()))
            else:
                ops.append((tag, r.i32()))
        steps, final = py_arbitrate(ops)
        w.u32(len(steps))
        for ds in steps:
            w.u32(len(ds))
            for d in ds:
                if d[0] == "acc":
                    w.u8(0).i32(d[1])
                elif d[0] == "ref":
                    w.u8(1).u8(d[1]).i32(d[2])
                elif d[0] == "act":
                    w.u8(2).i32(d[1]).u8(1 if d[2] else 0)
                elif d[0] == "pre":
                    w.u8(3).i32(d[1]).i32(d[2])
                elif d[0] == "end":
                    w.u8(4).i32(d[1]).u8(d[2]).i32(d[3])
                else:
                    w.u8(5).i32(d[1]).u32(d[2])
        w.u32(len(final))
        for e in final:
            w.i32(e["key"]).i32(e["owner"]).i32(e["band"]).i32(e["seq"]).u8(e["status"]).u8(1 if e["started"] else 0)
    elif op == 11:
        nodes = [r.i32() for _ in range(r.u32())]
        table = [[r.i32() for _ in range(r.u32())] for _ in range(r.u32())]
        durations = [r.i32() for _ in range(r.u32())]
        result, cycles = py_topo(nodes, table, durations)
        if result is None:
            w.u8(0).u32(len(cycles))
            for c in cycles:
                w.u32(len(c))
                for x in c:
                    w.i32(x)
        else:
            order, makespan, path, acts = result
            w.u8(1).u32(len(order))
            for x in order:
                w.i32(x)
            w.i32(makespan).u32(len(path))
            for x in path:
                w.i32(x)
            for a in acts:
                for x in a:
                    w.i32(x)
    elif op in (12, 15):
        root = bt_decode(r)
        n = bt_number(root, 0)
        script = [[r.u8() for _ in range(r.u32())] for _ in range(r.u32())]
        ticks = r.u32()
        cursor, fin, calls = [0] * n, [0] * n, [0] * len(script)
        running = set() if op == 15 else None
        w.u32(ticks)
        for _ in range(ticks):
            trace, halted = [], []
            s = py_bt_tick(root, cursor, fin, script, calls, trace, running, halted)
            w.u8(s).u32(len(trace))
            for x in trace:
                w.i32(x)
            for c in calls:
                w.i32(c)
            for arr in (cursor, fin):
                w.u32(len(arr))
                for x in arr:
                    w.i32(x)
            if op == 15:
                w.u32(len(halted))
                for x in halted:
                    w.i32(x)
    elif op == 16:
        import math
        xs = [max(0, min(65536, r.i32())) for _ in range(r.u32())]
        w.u32(len(xs))
        product = 65536
        for x in xs:
            quadratic = (x * x + 32768) // 65536
            w.i32(x).i32(x).i32(quadratic).i32(math.isqrt(x * 65536)).i32(65536 - quadratic)
            product = (product * quadratic + 32768) // 65536
        w.i32(product)
        if xs:
            best = max(xs)
            w.u8(1).i32(xs.index(best)).i32(best)
        else:
            w.u8(0)
    elif op == 14:
        pts = [(r.i32(), r.i32()) for _ in range(r.u32())]
        n = r.u32()
        w.u32(n)
        for _ in range(n):
            kind, a, b, c, d = r.u8(), r.i32(), r.i32(), r.i32(), r.i32()
            if kind == 0:
                qx, qy = a, b
            elif kind in (1, 2):
                qx, qy = b, c
            if kind in (0, 1, 2):
                keyed = sorted(((x - qx) ** 2 + (y - qy) ** 2, x, y, i) for i, (x, y) in enumerate(pts))
                if kind == 0:
                    res = keyed[:1]
                elif kind == 1:
                    res = keyed[:a]
                else:
                    res = [t for t in keyed if t[0] <= a * a]
                res = [(x, y, i) for (_, x, y, i) in res]
            else:
                res = sorted((x, y, i) for i, (x, y) in enumerate(pts) if a <= x <= c and b <= y <= d)
            w.u32(len(res))
            for (x, y, i) in res:
                w.i32(x).i32(y).i32(i)
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
        elif op == 13:
            if outcome != 0:
                problems.append(f"outcome {outcome}")
            else:
                problems += door_check(bytes(payload), body)
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
