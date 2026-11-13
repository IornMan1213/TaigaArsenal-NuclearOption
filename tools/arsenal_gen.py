"""Procedural (from-scratch) models for Taiga Arsenal, a pack of Soviet / Russian munitions for Nuclear Option.

Output: <out>/arsenal.json (mesh dump: Unity coords, metres, +Z forward, Unity front-face winding) and <out>/arsenal_atlas.png
(+ flat normal / metallic textures). Each store is modelled at its real size from open sources:
  R-27ER 4.78 m / R-27ET 4.50 m (230 mm front, 260 mm motor), R-74M 2.90 m x 170 mm;
  Kh-25ML 3.7 m / Kh-25MP 4.3 m x 275 mm; Kh-38ML / MT 4.2 m x 310 mm; Kh-59M 5.7 m x 380 mm; Kh-35U 3.85 m x 420 mm;
  KAB-500S-E 3.0 m x 400 mm; KAB-250LG-E 3.2 m x 255 mm; FAB-250 M-62 1.96 m x 325 mm; FAB-500 M-62 2.47 m x 400 mm;
  OFAB-250-270 1.48 m x 325 mm; ODAB-500PM 2.3 m x 500 mm; BetAB-500ShP 2.5 m x 325 mm; RBK-500 2.5 m x 450 mm;
  B-8M1 pod 2.75 m x 520 mm (20 x S-8, 80 mm); B-13L pod 3.5 m x 410 mm (5 x S-13, 122 mm); S-24B 2.33 m x 240 mm.
  R-77 3.6 m x 200 mm; R-24R 4.46 m / R-24T 4.16 m x 230 mm; Kh-29L / T / TD 3.875 m x 380 mm; Kh-38MK 4.2 m; Kh-31A 4.7 m /
  Kh-31P 5.23 m x 360 mm; Kh-59 5.37 m x 380 mm; FAB-100 1.07 m x 267 mm; ZAB-500 2.28 m x 450 mm; KAB-500L 3.05 m x 400 mm;
  KAB-500Kr 3.05 m x 350 mm; KAB-250S-E 2.9 m x 255 mm; PBK-500U 3.08 m; RBK-250 2.12 m x 325 mm; KMGU-2 3.0 m x 400 mm; RN-40 3.0 m
  x 400 mm; UB-32 pod 2.0 m x 410 mm (32 x S-5, 57 mm); S-25OF 3.31 m x 340 mm in its O-25 tube; MSP-418K 2.2 m x 340 mm.
The mesh helpers and the APU / AKU launchers follow the MiG-29 mods' tools/missile_gen.py (same author).
python arsenal_gen.py <out dir>
"""
import json, math, sys
import numpy as np
from PIL import Image

import os
OUT = sys.argv[1]

# 4x4 colour atlas: cell -> sRGB
CELLS = {
    "white": (232, 232, 226), "lgrey": (190, 192, 190), "dgrey": (62, 64, 66), "glass": (18, 22, 26),
    "olive": (110, 112, 70), "red": (170, 40, 40), "yellow": (200, 168, 48), "metal": (130, 134, 138),
    "brown": (120, 92, 62), "camo": (176, 186, 182), "black": (12, 12, 12), "orange": (210, 120, 40),
    "bgreen": (86, 104, 76),
}
NAMES = list(CELLS)


def uv(cell):
    i = NAMES.index(cell); cx, cy = i % 4, i // 4
    return ((cx + 0.5) / 4, 1 - (cy + 0.5) / 4)


class Mesh:
    def __init__(self):
        self.v, self.n, self.uv, self.t = [], [], [], []

    def tri(self, a, b, c, cell, normal=None):
        a, b, c = map(np.asarray, (a, b, c))
        fn = np.cross(b - a, c - a)
        if normal is not None and np.dot(fn, normal) < 0:  # Unity front face: cross(b-a, c-a) points outward
            b, c = c, b; fn = -fn
        nn = fn / (np.linalg.norm(fn) + 1e-12) if normal is None else np.asarray(normal) / np.linalg.norm(normal)
        base = len(self.v)
        for p in (a, b, c):
            self.v.append(p.tolist()); self.n.append(nn.tolist()); self.uv.append(list(uv(cell)))
        self.t += [base, base + 1, base + 2]

    def quad(self, a, b, c, d, cell, normal):
        self.tri(a, b, c, cell, normal); self.tri(a, c, d, cell, normal)

    def revolve(self, profile, seg=24, cells=None, z_off=0.0, x_off=0.0, y_off=0.0):
        """profile: [(z, r), ...] nose first. cells(z) -> atlas cell. Smooth normals around the ring."""
        for i in range(len(profile) - 1):
            (z0, r0), (z1, r1) = profile[i], profile[i + 1]
            cell = cells((z0 + z1) / 2) if cells else "white"
            slope = (r0 - r1) / (z0 - z1 + 1e-9)
            for k in range(seg):
                a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
                P = lambda r, z, a: np.array([x_off + r * math.cos(a), y_off + r * math.sin(a), z_off + z])
                N = lambda a: np.array([math.cos(a), math.sin(a), slope * (1 if z0 > z1 else -1) * 0 + slope])
                p00, p01, p10, p11 = P(r0, z0, a0), P(r0, z0, a1), P(r1, z1, a0), P(r1, z1, a1)
                am = (a0 + a1) / 2
                nrm = np.array([math.cos(am), math.sin(am), (r1 - r0) / (abs(z0 - z1) + 1e-9) * (1 if z0 > z1 else -1)])  # radius growing aft -> normal tilts forward
                if r0 < 1e-6:
                    self.tri(p00, p10, p11, cell, nrm)
                elif r1 < 1e-6:
                    self.tri(p00, p01, p10, cell, nrm)
                else:
                    self.quad(p00, p01, p11, p10, cell, nrm)
        # aft cap
        z, r = profile[-1]
        if r > 1e-6:
            c = np.array([x_off, y_off, z_off + z])
            for k in range(seg):
                a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
                self.tri(c, np.array([x_off + r * math.cos(a0), y_off + r * math.sin(a0), z_off + z]),
                         np.array([x_off + r * math.cos(a1), y_off + r * math.sin(a1), z_off + z]), "dgrey", np.array([0, 0, -1.0]))

    def fin(self, planform, roll_deg, thickness, cell, z_off=0.0):
        """planform: polygon [(s, z)] with s = distance from the missile axis. Fin plane spans radial dir + z."""
        phi = math.radians(roll_deg)
        er = np.array([math.cos(phi), math.sin(phi), 0.0]); et = np.array([-math.sin(phi), math.cos(phi), 0.0])
        ez = np.array([0, 0, 1.0])
        P = lambda s, z, side: s * er + (z + z_off) * ez + side * thickness / 2 * et
        poly = planform
        for side, nrm in ((1, et), (-1, -et)):
            for i in range(1, len(poly) - 1):
                self.tri(P(*poly[0], side), P(*poly[i], side), P(*poly[i + 1], side), cell, nrm)
        for i in range(len(poly)):
            (s0, z0), (s1, z1) = poly[i], poly[(i + 1) % len(poly)]
            edge = P(s1, z1, 0) - P(s0, z0, 0)
            out = np.cross(edge, et); out = out if np.dot(out, P(s0, z0, 0) - P(*np.mean(poly, axis=0), 0)) > 0 else -out
            self.quad(P(s0, z0, 1), P(s1, z1, 1), P(s1, z1, -1), P(s0, z0, -1), cell, out)

    def box(self, x0, x1, y0, y1, z0, z1, cell, nose_taper=0.0):
        """Axis box; nose_taper > 0 pinches the front (+z) end to a wedge of that length."""
        c = lambda x, y, z: np.array([x, y, z])
        zf = z1
        corners = {}
        for xi, x in ((0, x0), (1, x1)):
            for yi, y in ((0, y0), (1, y1)):
                for zi, z in ((0, z0), (1, z1)):
                    zz = z
                    if zi == 1 and nose_taper > 0 and yi == 0:
                        zz = z1 - nose_taper  # bottom front edge pulled back -> wedge nose
                    corners[(xi, yi, zi)] = c(x, y, zz)
        faces = [((0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, -1)), ((0, 0, 1), (0, 1, 1), (1, 1, 1), (1, 0, 1), (0, -0.4, 1)),
                 ((0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1), (-1, 0, 0)), ((1, 0, 0), (1, 0, 1), (1, 1, 1), (1, 1, 0), (1, 0, 0)),
                 ((0, 1, 0), (1, 1, 0), (1, 1, 1), (0, 1, 1), (0, 1, 0)), ((0, 0, 0), (0, 0, 1), (1, 0, 1), (1, 0, 0), (0, -1, 0.3))]
        for a, b, cc, d, n in faces:
            self.quad(corners[a], corners[b], corners[cc], corners[d], cell, np.array(n, float))

    def dump(self, name):
        return dict(name=name, material="missile", vertices=sum(self.v, []), normals=sum(self.n, []), uvs=sum(self.uv, []), triangles=self.t)



def bar(m, a, b, thick, z0, z1, er, et, cell):
    """A thin flat bar from a to b, given as (radial s, tangential t) in the plane spanned by er/et, extruded from z0 to z1."""
    ez = np.array([0, 0, 1.0])
    P = lambda s, t, z: s * er + t * et + z * ez
    d = np.array(b, float) - np.array(a, float); d /= np.linalg.norm(d) + 1e-12
    nrm = np.array([-d[1], d[0]]) * thick / 2                # in-plane normal of the bar, (s, t)
    c = [(a[0] + nrm[0], a[1] + nrm[1]), (b[0] + nrm[0], b[1] + nrm[1]), (b[0] - nrm[0], b[1] - nrm[1]), (a[0] - nrm[0], a[1] - nrm[1])]
    ring0 = [P(s, t, z0) for s, t in c]; ring1 = [P(s, t, z1) for s, t in c]
    mid = np.mean(ring0 + ring1, axis=0)
    for i in range(4):
        q = [ring0[i], ring0[(i + 1) % 4], ring1[(i + 1) % 4], ring1[i]]
        n = np.cross(q[1] - q[0], q[3] - q[0]); n = n if np.dot(n, np.mean(q, axis=0) - mid) > 0 else -n
        m.quad(*q, cell, n)
    m.quad(*ring1, cell, ez); m.quad(*ring0[::-1], cell, -ez)


def grid_fin(m, roll_deg, s0, s1, w, z0, depth, cell="lgrey"):
    """Lattice (grid) tail fin, as on the R-77: a rectangular frame standing out from the body from s0 to s1, w wide, its face
    across the airflow, filled with a diagonal lattice; on a short stub."""
    phi = math.radians(roll_deg)
    er = np.array([math.cos(phi), math.sin(phi), 0.0]); et = np.array([-math.sin(phi), math.cos(phi), 0.0])
    z1 = z0 + depth; t = 0.004
    for a, b in (((s0, -w / 2), (s1, -w / 2)), ((s0, w / 2), (s1, w / 2)), ((s0, -w / 2), (s0, w / 2)), ((s1, -w / 2), (s1, w / 2))):
        bar(m, a, b, t, z0, z1, er, et, cell)
    h = s1 - s0
    for k in range(-2, 3):                                   # diagonal lattice: lines at +-45 deg, clipped to the frame
        for sgn in (1, -1):
            off = k * h / 2.5
            pts = []
            for s in (s0, s1):
                tt = sgn * (s - (s0 + s1) / 2) + off
                pts.append((s, tt))
            (sa, ta), (sb, tb) = pts
            # clip to |t| <= w/2
            if max(ta, tb) < -w / 2 or min(ta, tb) > w / 2: continue
            def clipt(s_, t_, s2, t2):
                if abs(t_) <= w / 2: return s_, t_
                tc = math.copysign(w / 2, t_); f = (tc - t_) / (t2 - t_)
                return s_ + (s2 - s_) * f, tc
            pa = clipt(sa, ta, sb, tb); pb = clipt(sb, tb, sa, ta)
            if math.dist(pa, pb) > 0.01: bar(m, pa, pb, 0.0025, z0, z1, er, et, cell)
    stub = 0.012                                             # hinge stub from the body to the frame
    bar(m, (s0 - 0.03, 0.0), (s0, 0.0), stub, z0 + depth * 0.2, z1 - depth * 0.2, er, et, "dgrey")



def aku58(deg=2.5):
    """AKU-58 launcher for the Kh-29 / Kh-31: as long as the AKU-470 (the inner fairing's flat underside), deeper and wider for the
    heavier missiles; missile body top at y = -0.17."""
    return levelled(launcher(-1.15, 0.95, 0.065, 0.17, nose=0.50, tail=0.30, rail_depth=0.018, rail_half_w=0.032), 0.95, 0.17, deg)


def levelled(m, z1, depth, deg):
    """The pylon stations follow their fairings, which slope `deg` nose-down; the missile must stay parallel to the fuselage (user
    reference photo, MiG-29 mod v0.8.9). In the station frame a level line rises toward the nose, so the beam's bottom and the rail are tilted by
    `deg` about the launcher's nose (z1): slim at the front, deeper at the back, filling the wedge as on the real launchers. The top
    stays flat against the fairing."""
    k = math.tan(math.radians(deg))
    for v in m.v:
        s = (v[2] - z1) * k                          # <= 0: lower toward the back
        if v[1] <= -depth + 0.006: v[1] += s         # rail, shoes, beam bottom
        elif v[1] < 0: v[1] += s * (-v[1] / depth)  # beam sides: blend from the flat top
    return m


def launcher(z0, z1, half_w, depth, nose, tail, rail_depth, rail_half_w, cell="lgrey", seg=6):
    """Streamlined launcher beam as on the real APU-73 / APU-60 / AKU-470 (user reference photo, MiG-29 mod v0.8.9): flat top against the
    pylon fairing, rounded sides, an ogive nose and a shorter tapered tail, with a metal guide rail and hanger shoes underneath.
    Spans z0..z1 (z1 forward), top at y = 0, beam bottom at -depth, rail bottom at -depth - rail_depth (where the missile touches)."""
    m = Mesh()
    L = z1 - z0
    zs = [z0 + L * k / 40 for k in range(41)]

    # Box-section beam the width of the fairing's underside, flat sides, rounded lower corners; the nose and tail taper in height
    # only (a wedge, as on the real launchers). Front views showed the earlier pointed, V-section beam meeting the fairing at a
    # point (user, MiG-29 mod v0.8.9).
    def height(z):
        if z > z1 - nose:
            t = (z - (z1 - nose)) / nose; return 1 - 0.72 * t ** 1.3
        if z < z0 + tail:
            t = ((z0 + tail) - z) / tail; return 1 - 0.55 * t ** 1.3
        return 1.0

    r = half_w * 0.45                                # lower corner radius

    def section(h):
        d = depth * h; rr = min(r, d * 0.6)
        pts = [(-half_w, 0.0), (-half_w, -d + rr)]
        for i in range(1, seg):                      # left lower corner
            a = math.pi / 2 * i / seg
            pts.append((-half_w + rr * (1 - math.cos(a)), -d + rr * (1 - math.sin(a))))
        pts += [(-half_w + rr, -d), (half_w - rr, -d)]
        for i in range(1, seg):                      # right lower corner
            a = math.pi / 2 * i / seg
            pts.append((half_w - rr * (1 - math.sin(a)), -d + rr * (1 - math.cos(a))))
        pts += [(half_w, -d + rr), (half_w, 0.0)]
        return pts

    def scale(z): return height(z)

    rings = [[(x, y, z) for x, y in section(scale(z))] for z in zs]
    for a, b in zip(rings, rings[1:]):
        for i in range(len(a) - 1):
            p0, p1, q1, q0 = a[i], a[i + 1], b[i + 1], b[i]
            mid = np.mean([p0, p1, q0, q1], axis=0)
            outward = np.array([mid[0], min(mid[1] + depth * 0.5, 0.0), 0.0])
            m.quad(p0, p1, q1, q0, cell, outward if np.linalg.norm(outward) > 1e-6 else np.array([0, -1.0, 0]))
    for a, b in zip(rings, rings[1:]):                              # flat top (against the fairing; seen at the tapered ends)
        m.quad(a[0], a[-1], b[-1], b[0], cell, np.array([0, 1.0, 0]))
    for ring, nz in ((rings[0], -1.0), (rings[-1], 1.0)):          # end caps
        c = np.mean(ring, axis=0)
        for i in range(len(ring) - 1):
            m.tri(c, ring[i], ring[i + 1], cell, np.array([0, 0, nz]))
    # guide rail under the full-height part of the beam, and three hanger shoes reaching down to the missile's top (it hangs 1.5 cm
    # below the rail), so it visibly hangs from them
    rz0, rz1 = z0 + tail * 0.8, z1 - nose * 0.8
    m.box(-rail_half_w, rail_half_w, -depth - rail_depth, -depth + 0.005, rz0, rz1, "metal")
    for f in (0.12, 0.5, 0.88):
        zc = rz0 + f * (rz1 - rz0)
        m.box(-rail_half_w * 1.3, rail_half_w * 1.3, -depth - rail_depth - 0.016, -depth - rail_depth * 0.3, zc - 0.045, zc + 0.045, "dgrey")
    return m


def apu60(deg=2.5, half_w=0.045):
    """APU-60 rail launcher for the R-60M: missile body top touches the rail underside at y = -0.08."""
    return levelled(launcher(-0.95, 0.80, half_w, 0.08, nose=0.40, tail=0.25, rail_depth=0.012, rail_half_w=0.016), 0.80, 0.08, deg)


def apu73(deg=2.5, half_w=0.05):
    """APU-73 rail launcher: missile body top touches the rail underside at y = -0.10."""
    return levelled(launcher(-1.25, 1.05, half_w, 0.10, nose=0.50, tail=0.30, rail_depth=0.015, rail_half_w=0.020), 1.05, 0.10, deg)


def aku470(deg=2.5):
    """AKU-470 ejector launcher for the R-27. 2.1 m long, to fit under the model's inner pylon fairing (flat underside from 1.17 m
    behind to 0.90 m ahead of the hardpoint): at 3.15 m its ends hung in the air past the fairing (user screenshots, MiG-29 mod v0.8.8)."""
    return levelled(launcher(-1.15, 0.95, 0.05, 0.14, nose=0.50, tail=0.30, rail_depth=0.015, rail_half_w=0.028), 0.95, 0.14, deg)


def aku170(deg=2.5):
    """AKU-170 ejector launcher for the R-77: a short beam, missile body top at y = -0.10."""
    return levelled(launcher(-0.80, 0.75, 0.05, 0.10, nose=0.35, tail=0.25, rail_depth=0.015, rail_half_w=0.022), 0.75, 0.10, deg)




# ---------------- shared shapes ----------------

def x_fins(m, planform, thickness, cell, rolls=(45, 135, 225, 315)):
    for roll in rolls: m.fin(planform, roll, thickness, cell)


def body(m, L, R, nose, tail_r=None, tail=0.08, seg=28, cells=None):
    """A missile body: nose profile [(z, r)] from the tip (zt = L/2), then the cylinder of radius R to the tail and a short boat-tail."""
    zt = L / 2; tail_r = R * 0.85 if tail_r is None else tail_r
    prof = nose(zt) + [(-zt + tail, R), (-zt + tail * 0.4, (R + tail_r) / 2), (-zt, tail_r)]
    m.revolve(prof, seg=seg, cells=cells)
    return zt


def ogive(zt, R, length, n=8, tip=0.0):
    """Tangent-ogive nose of the given length, from the tip (radius `tip`) to the full radius R."""
    pts = []
    for i in range(n + 1):
        t = i / n
        pts.append((zt - length * t, tip + (R - tip) * math.sin(t * math.pi / 2) ** 0.9))
    pts[0] = (zt, tip if tip > 0 else 0.0)
    if tip > 0: pts.insert(0, (zt, 0.0))
    return pts


def ring(m, z0, z1, r, cell, seg=28, thick=0.004):
    """An open thin-walled ring from z0 to z1 (both faces), as round a bomb's fins."""
    for k in range(seg):
        a0, a1 = 2 * math.pi * k / seg, 2 * math.pi * (k + 1) / seg
        for rr, sgn in ((r, 1), (r - thick, -1)):
            P = lambda a, z: np.array([rr * math.cos(a), rr * math.sin(a), z])
            am = (a0 + a1) / 2
            m.quad(P(a0, z0), P(a1, z0), P(a1, z1), P(a0, z1), cell, sgn * np.array([math.cos(am), math.sin(am), 0.0]))


# ---------------- air-to-air ----------------

def r27e(L, ir):
    """R-27ER / R-27ET: the R-27 front end on a longer, fatter motor section (260 mm against 230 mm), so the body steps out behind
    the control fins; same butterfly canards and long-chord tail wings."""
    m = Mesh(); R0, R1 = 0.115, 0.13; zt = L / 2
    if ir:
        nose = [(zt, 0), (zt - 0.012, 0.042), (zt - 0.035, 0.066), (zt - 0.07, 0.080), (zt - 0.09, 0.083), (zt - 0.10, 0.090), (zt - 0.22, 0.104), (zt - 0.36, R0)]
        cell = lambda z: "glass" if z > zt - 0.092 else ("dgrey" if z > zt - 0.36 else ("yellow" if zt - 0.72 < z < zt - 0.66 else "white"))
    else:
        nose = [(zt, 0), (zt - 0.05, 0.028), (zt - 0.14, 0.058), (zt - 0.28, 0.085), (zt - 0.44, 0.103), (zt - 0.60, 0.112), (zt - 0.68, R0)]
        cell = lambda z: "lgrey" if z > zt - 0.66 else ("yellow" if zt - 1.00 < z < zt - 0.94 else "white")
    step = zt - 1.75                                           # the motor section's front: fatter behind the canards
    prof = nose + [(step, R0), (step - 0.12, R1), (-zt + 0.08, R1), (-zt + 0.03, 0.118), (-zt, 0.10)]
    m.revolve(prof, seg=28, cells=cell)
    x_fins(m, [(R0, zt - 1.05), (R0, zt - 1.30), (0.485, zt - 1.62), (0.485, zt - 1.14)], 0.010, "lgrey")
    x_fins(m, [(R1, -zt + 1.05), (R1, -zt + 0.10), (0.42, -zt + 0.12), (0.42, -zt + 0.42)], 0.010, "lgrey")
    return m


def r74():
    """R-74M (RVV-MD): the R-73 airframe, 2.9 m x 170 mm, with gas vanes; a dark seeker band and a new grey finish."""
    m = Mesh(); L = 2.90; R = 0.085; zt = L / 2
    prof = [(zt, 0), (zt - 0.01, 0.03), (zt - 0.03, 0.052), (zt - 0.06, 0.07), (zt - 0.10, 0.081), (zt - 0.15, R),
            (-zt + 0.06, R), (-zt + 0.02, 0.078), (-zt, 0.066)]
    m.revolve(prof, cells=lambda z: "glass" if z > zt - 0.12 else ("dgrey" if zt - 0.20 < z <= zt - 0.12 else
                                                                    ("yellow" if zt - 0.62 < z < zt - 0.56 else "lgrey")))
    x_fins(m, [(R, zt - 0.20), (R, zt - 0.30), (R + 0.045, zt - 0.30)], 0.006, "white")
    x_fins(m, [(R, zt - 0.36), (R, zt - 0.62), (0.192, zt - 0.58), (0.192, zt - 0.50)], 0.008, "white")
    x_fins(m, [(R, -zt + 0.56), (R, -zt + 0.06), (0.255, -zt + 0.10), (0.255, -zt + 0.30)], 0.008, "white")
    return m


# ---------------- air-to-surface missiles ----------------

def kh25(L, radar):
    """Kh-25ML (laser) / Kh-25MP (anti-radiation): 275 mm body. ML 3.7 m with a glass seeker window on a blunt nose; MP 4.3 m with a
    long dark radome. Small canards near the nose, cruciform tail wings of 0.75 m span."""
    m = Mesh(); R = 0.1375; zt = L / 2
    if radar:
        nose = lambda zt: ogive(zt, R, 0.75, tip=0.012)
        cells = lambda z: "dgrey" if z > zt - 0.75 else ("yellow" if zt - 1.05 < z < zt - 0.99 else "lgrey")
    else:
        nose = lambda zt: [(zt, 0), (zt - 0.02, 0.05), (zt - 0.06, 0.085), (zt - 0.09, 0.10), (zt - 0.10, 0.11), (zt - 0.28, R)]
        cells = lambda z: "glass" if z > zt - 0.095 else ("dgrey" if z > zt - 0.28 else ("yellow" if zt - 0.80 < z < zt - 0.74 else "olive"))
    body(m, L, R, nose, cells=cells)
    c = "lgrey" if radar else "olive"
    cz = zt - (0.95 if radar else 0.45)
    x_fins(m, [(R, cz), (R, cz - 0.22), (0.27, cz - 0.20), (0.27, cz - 0.10)], 0.008, c)
    x_fins(m, [(R, -zt + 0.85), (R, -zt + 0.06), (0.375, -zt + 0.08), (0.375, -zt + 0.30)], 0.010, c)
    return m


def kh38(thermal):
    """Kh-38ML (laser) / Kh-38MT (thermal imaging): 4.2 m x 310 mm, a short nose with the seeker, four long folding mid-body wings and
    four tail fins. Light grey; the MT's seeker is a dark faceted window."""
    m = Mesh(); L = 4.20; R = 0.155; zt = L / 2
    if thermal:
        nose = lambda zt: [(zt, 0), (zt - 0.02, 0.07), (zt - 0.05, 0.11), (zt - 0.10, 0.13), (zt - 0.30, R)]
        cells = lambda z: "black" if z > zt - 0.10 else ("dgrey" if z > zt - 0.30 else ("yellow" if zt - 0.90 < z < zt - 0.84 else "lgrey"))
    else:
        nose = lambda zt: [(zt, 0), (zt - 0.02, 0.055), (zt - 0.06, 0.095), (zt - 0.10, 0.115), (zt - 0.12, 0.125), (zt - 0.32, R)]
        cells = lambda z: "glass" if z > zt - 0.11 else ("dgrey" if z > zt - 0.32 else ("yellow" if zt - 0.90 < z < zt - 0.84 else "lgrey"))
    body(m, L, R, nose, cells=cells)
    x_fins(m, [(R, 0.55), (R, -0.75), (0.36, -0.55), (0.36, 0.30)], 0.010, "lgrey")           # folding mid wings, deployed
    x_fins(m, [(R, -zt + 0.55), (R, -zt + 0.05), (0.33, -zt + 0.07), (0.33, -zt + 0.22)], 0.010, "lgrey")
    return m


def kh59m():
    """Kh-59M (AS-18 Kazoo): 5.7 m x 380 mm turbojet stand-off missile, TV window in the nose, small canards, straight folding wings
    (span 1.3 m) and cruciform tail, with the RDK-300 turbofan in a pod under the rear body."""
    m = Mesh(); L = 5.69; R = 0.19; zt = L / 2
    body(m, L, R, lambda zt: [(zt, 0), (zt - 0.03, 0.08), (zt - 0.08, 0.13), (zt - 0.14, 0.155), (zt - 0.40, R)],
         cells=lambda z: "glass" if z > zt - 0.14 else ("dgrey" if z > zt - 0.40 else ("yellow" if zt - 1.10 < z < zt - 1.03 else "lgrey")), seg=28)
    x_fins(m, [(R, zt - 0.55), (R, zt - 0.75), (0.30, zt - 0.73), (0.30, zt - 0.62)], 0.008, "lgrey")
    for roll in (0, 180):                                                                         # straight wings, port and starboard
        m.fin([(R, 0.35), (R, -0.25), (0.65, -0.18), (0.65, 0.20)], roll, 0.012, "lgrey")
    x_fins(m, [(R, -zt + 0.70), (R, -zt + 0.08), (0.42, -zt + 0.10), (0.42, -zt + 0.30)], 0.010, "lgrey")
    eng = [(0.0, 0), (-0.06, 0.07), (-0.20, 0.10), (-1.10, 0.10), (-1.25, 0.07)]               # engine pod under the tail, intake open
    m.revolve([(z - zt + 1.45, r) for z, r in eng], seg=16, cells=lambda z: "dgrey", y_off=-(R + 0.07))
    m.box(-0.03, 0.03, -(R + 0.02), -R + 0.02, -zt + 0.35, -zt + 1.25, "lgrey")                 # its pylon
    return m


def kh35u():
    """Kh-35U (AS-20 Kayak): 3.85 m x 420 mm sea-skimming anti-ship missile: dark radome, a flush ventral air intake for its turbofan,
    four folding mid-body wings and four tail fins."""
    m = Mesh(); L = 3.85; R = 0.21; zt = L / 2
    body(m, L, R, lambda zt: ogive(zt, R, 0.55, tip=0.02), cells=lambda z: "dgrey" if z > zt - 0.55 else
         ("yellow" if zt - 0.85 < z < zt - 0.79 else "camo"), seg=28)
    m.box(-0.10, 0.10, -R - 0.07, -R + 0.03, -0.55, 0.25, "camo", nose_taper=0.12)               # ventral intake fairing
    m.quad(np.array([-0.08, -R - 0.06, 0.251]), np.array([0.08, -R - 0.06, 0.251]), np.array([0.08, -R + 0.01, 0.251]),
           np.array([-0.08, -R + 0.01, 0.251]), "black", np.array([0, 0, 1.0]))                   # its dark mouth
    x_fins(m, [(R, 0.20), (R, -0.55), (0.48, -0.42), (0.48, 0.05)], 0.010, "camo")
    x_fins(m, [(R, -zt + 0.55), (R, -zt + 0.05), (0.40, -zt + 0.07), (0.40, -zt + 0.22)], 0.010, "camo")
    return m


# ---------------- bombs ----------------

def m62(L, R, nose_len, tail_len, cell="bgreen", nose="ogive", fin_span=None, band_z=None):
    """Soviet M-62 pattern general-purpose bomb: ogive nose, cylindrical body, conical tail, four flat tail fins tied by a ring, two
    suspension lugs on top."""
    m = Mesh(); zt = L / 2; tail_r = R * 0.35
    if nose == "ogive":
        prof = ogive(zt, R, nose_len, tip=R * 0.12)
    else:                                                       # blunt (thermobaric / cluster): a rounded nose
        prof = [(zt, 0.0)] + [(zt - nose_len * (1 - math.cos(t * math.pi / 2)), R * math.sin(t * math.pi / 2)) for t in [i / 8 for i in range(1, 9)]]
    bz = band_z if band_z is not None else zt - nose_len - 0.05
    # the red band gets its own ring of the profile (cells are picked per segment midpoint: a long segment through the band
    # would turn the whole body red)
    prof += [(bz + 0.03, R), (bz - 0.03, R), (-zt + tail_len, R), (-zt + tail_len * 0.4, (R + tail_r) / 2), (-zt, tail_r)]
    m.revolve(prof, seg=28, cells=lambda z: "red" if abs(z - bz) < 0.029 else cell)
    span = fin_span or R * 1.05
    x_fins(m, [(tail_r * 1.2, -zt + tail_len * 0.9), (tail_r, -zt), (span, -zt), (span, -zt + tail_len * 0.55)], 0.006, cell)
    ring(m, -zt + 0.16, -zt, span + 0.006, cell)                 # the fin ring
    for zl in (0.25 * L, -0.25 * L):                            # suspension lugs
        m.box(-0.012, 0.012, R - 0.005, R + 0.025, zl - 0.02, zl + 0.02, "metal")
    return m


def betab500():
    """BetAB-500ShP concrete-piercing bomb: 2.5 m x 325 mm, sharp nose, a parachute pack and the solid booster in the tail, four fins."""
    m = Mesh(); L = 2.5; R = 0.1625; zt = L / 2
    prof = ogive(zt, R, 0.55, tip=0.01) + [(-zt + 0.45, R), (-zt + 0.40, 0.12), (-zt + 0.05, 0.12), (-zt, 0.10)]
    m.revolve(prof, seg=28, cells=lambda z: "metal" if z > zt - 0.25 else ("red" if abs(z - (zt - 0.62)) < 0.03 else
                                                                              ("dgrey" if z < -zt + 0.45 else "bgreen")))
    x_fins(m, [(0.12, -zt + 0.45), (0.12, -zt + 0.02), (0.24, -zt + 0.04), (0.24, -zt + 0.30)], 0.006, "bgreen")
    for zl in (0.35, -0.25):
        m.box(-0.012, 0.012, R - 0.005, R + 0.025, zl - 0.02, zl + 0.02, "metal")
    return m


def kab(L, R, laser):
    """KAB-500S-E (satellite) / KAB-250LG-E (laser): guided bombs with long forward strakes and tail fins. The KAB-500S has a smooth
    rounded nose; the KAB-250L a gimballed laser seeker on a neck."""
    m = Mesh(); zt = L / 2
    if laser:
        prof = [(zt, 0), (zt - 0.03, 0.045), (zt - 0.08, 0.07), (zt - 0.12, 0.075), (zt - 0.15, 0.055), (zt - 0.22, 0.06),
                (zt - 0.35, R * 0.8), (zt - 0.50, R), (-zt + 0.45, R), (-zt + 0.12, R * 0.75), (-zt, R * 0.55)]
        cells = lambda z: "glass" if z > zt - 0.08 else ("dgrey" if z > zt - 0.24 else ("yellow" if zt - 0.70 < z < zt - 0.64 else "olive"))
    else:
        prof = [(zt, 0)] + [(zt - 0.42 * (1 - math.cos(t * math.pi / 2)), R * math.sin(t * math.pi / 2)) for t in [i / 8 for i in range(1, 9)]] \
               + [(-zt + 0.55, R), (-zt + 0.15, R * 0.75), (-zt, R * 0.55)]
        cells = lambda z: "yellow" if zt - 0.70 < z < zt - 0.63 else "olive"
    m.revolve(prof, seg=28, cells=cells)
    x_fins(m, [(R * 0.9, zt - 0.50), (R, zt - 0.50 - L * 0.32), (R + 0.09, zt - 0.50 - L * 0.29), (R + 0.09, zt - 0.62)], 0.008, "olive")
    x_fins(m, [(R * 0.75, -zt + 0.42), (R * 0.6, -zt + 0.02), (R + 0.17, -zt + 0.05), (R + 0.17, -zt + 0.20)], 0.010, "olive")
    return m


def bd3_rack(scale=1.0):
    """BD3-UMK bomb rack: a short streamlined beam with sway braces; bomb top at y = -0.14."""
    m = Mesh()
    m.box(-0.07, 0.07, -0.12, 0.0, -0.70 * scale, 0.70 * scale, "lgrey", nose_taper=0.30 * scale)
    for z in (-0.25 * scale, 0.25 * scale):
        m.box(-0.13, 0.13, -0.145, -0.11, z - 0.03, z + 0.03, "dgrey")
    return m


# ---------------- rockets and pods ----------------

def rocket(L, R, fin_span, cell="lgrey", nose_len=None):
    """An unguided rocket: ogive nose, cylinder, four folding tail fins (deployed)."""
    m = Mesh(); zt = L / 2
    body(m, L, R, lambda zt: ogive(zt, R, nose_len or R * 4, tip=R * 0.08), tail=0.02, seg=16,
         cells=lambda z: "olive" if z > zt - (nose_len or R * 4) else cell)
    x_fins(m, [(R, -zt + 0.18), (R, -zt + 0.01), (fin_span, -zt + 0.02), (fin_span, -zt + 0.08)], 0.004, "dgrey", rolls=(0, 90, 180, 270))
    return m


def rocket_pod(L, R, tubes, tube_r, nose_len, cell="metal"):
    """A rocket pod: cylindrical body with a short tapered front, the tube mouths as dark discs on the front face, and a tapered
    tail. `tubes` are (x, y) tube centres on the face."""
    m = Mesh(); zt = L / 2; rf = R * 0.92
    prof = [(zt, 0.0), (zt, rf), (zt - nose_len, R), (-zt + 0.25, R), (-zt + 0.05, R * 0.85), (-zt, R * 0.8)]
    m.revolve(prof, seg=32, cells=lambda z: "dgrey" if z > zt - 0.01 else cell)
    for x, y in tubes:                                                  # tube mouths, just proud of the front face
        c = np.array([x, y, zt + 0.004])
        for k in range(12):
            a0, a1 = 2 * math.pi * k / 12, 2 * math.pi * (k + 1) / 12
            m.tri(c, c + np.array([tube_r * math.cos(a0), tube_r * math.sin(a0), 0]), c + np.array([tube_r * math.cos(a1), tube_r * math.sin(a1), 0]),
                  "black", np.array([0, 0, 1.0]))
    for zl in (0.25 * L, -0.25 * L):                                    # suspension lugs
        m.box(-0.012, 0.012, R - 0.005, R + 0.025, zl - 0.02, zl + 0.02, "metal")
    return m


def b8m1_tubes():
    """The B-8M1's 20 tubes: rings of 1, 7 and 12."""
    pts = [(0.0, 0.0)]
    pts += [(0.085 * math.cos(2 * math.pi * k / 7), 0.085 * math.sin(2 * math.pi * k / 7)) for k in range(7)]
    pts += [(0.17 * math.cos(2 * math.pi * k / 12 + 0.26), 0.17 * math.sin(2 * math.pi * k / 12 + 0.26)) for k in range(12)]
    return pts


def b13l_tubes():
    """The B-13L's five 122 mm tubes, in a ring."""
    return [(0.12 * math.cos(2 * math.pi * k / 5 + math.pi / 2), 0.12 * math.sin(2 * math.pi * k / 5 + math.pi / 2)) for k in range(5)]


def apu68():
    """APU-68 rail launcher for the S-24B: a short beam, rocket body top at y = -0.12."""
    return levelled(launcher(-0.95, 0.85, 0.05, 0.12, nose=0.40, tail=0.25, rail_depth=0.015, rail_half_w=0.025), 0.85, 0.12, 2.5)


# ---------------- second batch ----------------

def r77():
    """R-77 (RVV-AE): 3.6 m x 200 mm, ogive radome, four narrow strake wings mid-body and the lattice (grid) tail fins."""
    m = Mesh(); L = 3.60; R = 0.10; zt = L / 2
    body(m, L, R, lambda zt: ogive(zt, R, 0.45, tip=0.01), tail=0.05,
         cells=lambda z: "dgrey" if z > zt - 0.45 else ("yellow" if zt - 0.75 < z < zt - 0.69 else "lgrey"))
    x_fins(m, [(R, 0.55), (R, -0.85), (0.16, -0.75), (0.16, 0.35)], 0.008, "lgrey")
    for roll in (45, 135, 225, 315):
        grid_fin(m, roll, R + 0.04, R + 0.30, 0.20, -zt + 0.02, 0.07)
    return m


def r24(L, ir):
    """R-24R / R-24T (AA-7 Apex): 230 mm body, the R-23 layout: small canards, long delta wings, tail fins behind them. R 4.46 m with a
    radome, T 4.16 m with a glass IR dome."""
    m = Mesh(); R = 0.115; zt = L / 2
    if ir:
        nose = lambda zt: [(zt, 0), (zt - 0.015, 0.045), (zt - 0.04, 0.07), (zt - 0.08, 0.085), (zt - 0.30, R)]
        cells = lambda z: "glass" if z > zt - 0.085 else ("dgrey" if z > zt - 0.30 else ("yellow" if zt - 0.80 < z < zt - 0.74 else "white"))
    else:
        nose = lambda zt: ogive(zt, R, 0.65, tip=0.008)
        cells = lambda z: "lgrey" if z > zt - 0.65 else ("yellow" if zt - 0.95 < z < zt - 0.89 else "white")
    body(m, L, R, nose, cells=cells)
    cz = zt - 0.85
    x_fins(m, [(R, cz), (R, cz - 0.18), (0.30, cz - 0.16), (0.30, cz - 0.08)], 0.008, "lgrey")
    x_fins(m, [(R, -0.10), (R, -1.25), (0.50, -1.15), (0.50, -0.95)], 0.010, "white")
    x_fins(m, [(R, -zt + 0.40), (R, -zt + 0.03), (0.30, -zt + 0.05), (0.30, -zt + 0.20)], 0.008, "white")
    return m


def kh29(L, seeker):
    """Kh-29 (AS-14 Kedge): 380 mm body, a blunt seeker head (laser: a glass window ringed by the laser receiver; TV: a dark flat
    window), four canards behind the head and four big trapezoidal tail wings. The TD's longer motor makes it 3.9 m like the T."""
    m = Mesh(); R = 0.19; zt = L / 2
    if seeker == "laser":
        nose = lambda zt: [(zt, 0), (zt - 0.02, 0.06), (zt - 0.06, 0.11), (zt - 0.10, 0.14), (zt - 0.14, 0.15), (zt - 0.45, R)]
        cells = lambda z: "glass" if z > zt - 0.10 else ("metal" if z > zt - 0.14 else ("dgrey" if z > zt - 0.45 else
                                                                                       ("yellow" if zt - 1.10 < z < zt - 1.04 else "olive")))
    else:
        nose = lambda zt: [(zt, 0), (zt, 0.12), (zt - 0.04, 0.15), (zt - 0.35, R)]
        cells = lambda z: "black" if z > zt - 0.01 else ("dgrey" if z > zt - 0.35 else ("yellow" if zt - 1.10 < z < zt - 1.04 else
                                                                                       ("lgrey" if seeker == "tvd" else "olive")))
    body(m, L, R, nose, cells=cells, seg=28)
    c = "lgrey" if seeker == "tvd" else "olive"
    cz = zt - 0.55
    x_fins(m, [(R, cz), (R, cz - 0.26), (0.36, cz - 0.24), (0.36, cz - 0.12)], 0.010, c)
    x_fins(m, [(R, -zt + 1.05), (R, -zt + 0.08), (0.55, -zt + 0.10), (0.55, -zt + 0.45)], 0.012, c)
    return m


def kh38_sat():
    """Kh-38MK: the Kh-38 with satellite guidance: a smooth closed ogive nose instead of a seeker window."""
    m = Mesh(); L = 4.20; R = 0.155; zt = L / 2
    body(m, L, R, lambda zt: ogive(zt, R, 0.45, tip=0.02), cells=lambda z: "yellow" if zt - 0.90 < z < zt - 0.84 else "lgrey")
    x_fins(m, [(R, 0.55), (R, -0.75), (0.36, -0.55), (0.36, 0.30)], 0.010, "lgrey")
    x_fins(m, [(R, -zt + 0.55), (R, -zt + 0.05), (0.33, -zt + 0.07), (0.33, -zt + 0.22)], 0.010, "lgrey")
    return m


def kh31(L, active):
    """Kh-31A (anti-ship, active radar, 4.7 m) / Kh-31P (anti-radiation, 5.2 m): 360 mm body with four ramjet intake ducts along the
    rear two thirds, small canards ahead of them and four short clipped tail wings."""
    m = Mesh(); R = 0.18; zt = L / 2
    body(m, L, R, lambda zt: ogive(zt, R, 0.70, tip=0.012), seg=28,
         cells=lambda z: "dgrey" if z > zt - 0.70 else ("yellow" if zt - 1.00 < z < zt - 0.94 else ("camo" if active else "lgrey")))
    c = "camo" if active else "lgrey"
    for k in range(4):                                                    # ramjet intakes, between the fins
        a = math.radians(45 + 90 * k); d = R + 0.035
        duct = [(0.20, 0.0), (0.18, 0.045), (0.05, 0.055), (-L / 2 + 0.75, 0.055), (-L / 2 + 0.55, 0.03)]
        m.revolve([(z, r) for z, r in duct], seg=10, cells=lambda z: c, x_off=d * math.cos(a), y_off=d * math.sin(a))
    cz = zt - 0.85
    x_fins(m, [(R, cz), (R, cz - 0.20), (0.32, cz - 0.18), (0.32, cz - 0.08)], 0.008, c, rolls=(0, 90, 180, 270))
    x_fins(m, [(R, -zt + 0.55), (R, -zt + 0.05), (0.46, -zt + 0.08), (0.46, -zt + 0.25)], 0.010, c, rolls=(0, 90, 180, 270))
    return m


def kh59():
    """Kh-59 (AS-13 Kingbolt): the solid-motor original, 5.37 m x 380 mm: TV window, small canards, straight wings, cruciform tail;
    no engine pod."""
    m = Mesh(); L = 5.37; R = 0.19; zt = L / 2
    body(m, L, R, lambda zt: [(zt, 0), (zt - 0.03, 0.08), (zt - 0.08, 0.13), (zt - 0.14, 0.155), (zt - 0.40, R)],
         cells=lambda z: "glass" if z > zt - 0.14 else ("dgrey" if z > zt - 0.40 else ("yellow" if zt - 1.10 < z < zt - 1.03 else "white")), seg=28)
    x_fins(m, [(R, zt - 0.55), (R, zt - 0.75), (0.30, zt - 0.73), (0.30, zt - 0.62)], 0.008, "white")
    for roll in (0, 180):
        m.fin([(R, 0.35), (R, -0.25), (0.65, -0.18), (0.65, 0.20)], roll, 0.012, "white")
    x_fins(m, [(R, -zt + 0.70), (R, -zt + 0.08), (0.42, -zt + 0.10), (0.42, -zt + 0.30)], 0.010, "white")
    return m


def kab_tv(L, R):
    """KAB-500Kr: TV-guided 500 kg bomb: a big flat dark seeker window on a short drum, then the KAB strakes and tail fins."""
    m = Mesh(); zt = L / 2
    prof = [(zt, 0), (zt, 0.13), (zt - 0.03, 0.16), (zt - 0.22, 0.17), (zt - 0.40, R), (-zt + 0.45, R), (-zt + 0.12, R * 0.75), (-zt, R * 0.55)]
    m.revolve(prof, seg=28, cells=lambda z: "black" if z > zt - 0.01 else ("dgrey" if z > zt - 0.24 else ("yellow" if zt - 0.70 < z < zt - 0.64 else "olive")))
    x_fins(m, [(R * 0.9, zt - 0.50), (R, zt - 0.50 - L * 0.32), (R + 0.09, zt - 0.50 - L * 0.29), (R + 0.09, zt - 0.62)], 0.008, "olive")
    x_fins(m, [(R * 0.75, -zt + 0.42), (R * 0.6, -zt + 0.02), (R + 0.17, -zt + 0.05), (R + 0.17, -zt + 0.20)], 0.010, "olive")
    return m


def pbk500u():
    """PBK-500U Drel: 3.1 m gliding cluster bomb with a flattened, faceted body, its long wings folded along the sides as it is carried
    (they swing out after release) and four tail fins. Dark grey."""
    m = Mesh(); L = 3.08; zt = L / 2
    m.box(-0.19, 0.19, -0.17, 0.15, -zt + 0.35, zt - 0.55, "dgrey")
    m.revolve([(zt, 0.0), (zt - 0.10, 0.10), (zt - 0.30, 0.16), (zt - 0.55, 0.17)], seg=8, cells=lambda z: "dgrey")    # faceted nose
    m.revolve([(-zt + 0.35, 0.17), (-zt + 0.10, 0.12), (-zt, 0.08)], seg=8, cells=lambda z: "dgrey")
    for roll in (0, 180):
        m.fin([(0.19, 0.95), (0.19, -0.95), (0.25, -0.90), (0.25, 0.90)], roll, 0.014, "dgrey")
    x_fins(m, [(0.12, -zt + 0.45), (0.10, -zt + 0.02), (0.36, -zt + 0.05), (0.36, -zt + 0.20)], 0.010, "dgrey")
    return m


def kmgu2():
    """KMGU-2 dispenser: a 3.0 m x 400 mm cylindrical container with rounded ends, a row of bay doors along it and two lugs."""
    m = Mesh(); L = 3.0; R = 0.20; zt = L / 2
    prof = [(zt, 0.0)] + [(zt - 0.25 * (1 - math.cos(t * math.pi / 2)), R * math.sin(t * math.pi / 2)) for t in [i / 6 for i in range(1, 7)]] \
           + [(-zt + 0.25, R), (-zt + 0.08, R * 0.8), (-zt, R * 0.55)]
    m.revolve(prof, seg=28, cells=lambda z: "olive")
    for k in range(8):                                                     # bay door seams underneath
        z = -zt + 0.45 + k * 0.30
        m.box(-0.14, 0.14, -R - 0.004, -R + 0.01, z, z + 0.012, "dgrey")
    for zl in (0.25 * L, -0.25 * L):
        m.box(-0.012, 0.012, R - 0.005, R + 0.025, zl - 0.02, zl + 0.02, "metal")
    return m


def rn40():
    """RN-40: a free-fall tactical nuclear bomb, 3.0 m x 400 mm, rounded nose, slim body and four tail fins with a ring. White."""
    m = Mesh(); L = 3.0; R = 0.20; zt = L / 2
    prof = [(zt, 0.0)] + [(zt - 0.45 * (1 - math.cos(t * math.pi / 2)), R * math.sin(t * math.pi / 2)) for t in [i / 8 for i in range(1, 9)]] \
           + [(zt - 0.60, R), (zt - 0.66, R), (-zt + 0.60, R), (-zt + 0.25, R * 0.6), (-zt, R * 0.45)]
    m.revolve(prof, seg=28, cells=lambda z: "red" if zt - 0.66 < z < zt - 0.60 else "white")
    x_fins(m, [(R * 0.62, -zt + 0.55), (R * 0.45, -zt), (R + 0.10, -zt), (R + 0.10, -zt + 0.30)], 0.008, "white")
    ring(m, -zt + 0.16, -zt, R + 0.106, "white")
    for zl in (0.35, -0.35):
        m.box(-0.012, 0.012, R - 0.005, R + 0.025, zl - 0.02, zl + 0.02, "metal")
    return m


def ub32_tubes():
    """The UB-32's 32 tubes, in rings of 4, 10 and 18."""
    pts = [(0.045 * math.cos(2 * math.pi * k / 4 + 0.785), 0.045 * math.sin(2 * math.pi * k / 4 + 0.785)) for k in range(4)]
    pts += [(0.11 * math.cos(2 * math.pi * k / 10), 0.11 * math.sin(2 * math.pi * k / 10)) for k in range(10)]
    pts += [(0.172 * math.cos(2 * math.pi * k / 18 + 0.17), 0.172 * math.sin(2 * math.pi * k / 18 + 0.17)) for k in range(18)]
    return pts


def o25():
    """O-25 launch tube for the S-25: a 3.5 m disposable tube with its front closed by a frangible cap (dark), hung on top."""
    m = Mesh(); L = 3.50; R = 0.215; zt = L / 2
    m.revolve([(zt, 0.0), (zt, R * 0.95), (zt - 0.05, R), (-zt + 0.05, R), (-zt, R * 0.95), (-zt, 0.0)], seg=28,
              cells=lambda z: "dgrey" if z > zt - 0.02 else "olive", y_off=-R - 0.02)
    for zl in (0.9, -0.9):
        m.box(-0.012, 0.012, -0.03, 0.0, zl - 0.03, zl + 0.03, "metal")
    return m


def msp418k():
    """MSP-418K jamming pod: 2.2 m x 340 mm, dark dielectric radomes at both ends, a flat-topped pylon fairing, hanging under the
    station (top at y = 0)."""
    m = Mesh(); L = 2.20; R = 0.17; zt = L / 2; y = -R - 0.06
    prof = [(zt, 0.0), (zt - 0.05, 0.08), (zt - 0.18, 0.15), (zt - 0.35, R), (-zt + 0.35, R), (-zt + 0.18, 0.15), (-zt + 0.05, 0.08), (-zt, 0.0)]
    m.revolve(prof, seg=28, cells=lambda z: "dgrey" if abs(z) > zt - 0.35 else "lgrey", y_off=y)
    m.box(-0.04, 0.04, y + R - 0.01, 0.0, -0.45, 0.45, "lgrey", nose_taper=0.15)
    return m


def textures():
    a = Image.new("RGB", (64, 64))
    for i, name in enumerate(NAMES):
        cx, cy = i % 4, i // 4
        a.paste(CELLS[name], (cx * 16, cy * 16, cx * 16 + 16, cy * 16 + 16))
    a.save(OUT + "/arsenal_atlas.png")
    Image.new("RGB", (8, 8), (128, 128, 255)).save(OUT + "/flat_normal.png")
    met = Image.new("RGBA", (64, 64), (0, 0, 0, 90))
    for name in ("glass", "metal"):
        i = NAMES.index(name); cx, cy = i % 4, i // 4
        met.paste((40 if name == "glass" else 200, 0, 0, 220 if name == "glass" else 150), (cx * 16, cy * 16, cx * 16 + 16, cy * 16 + 16))
    met.save(OUT + "/arsenal_metallic.png")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    textures()
    parts = [
        r27e(4.78, False).dump("R27ER"), r27e(4.50, True).dump("R27ET"), r74().dump("R74M"),
        kh25(3.70, False).dump("KH25ML"), kh25(4.30, True).dump("KH25MP"), kh38(False).dump("KH38ML"), kh38(True).dump("KH38MT"),
        kh59m().dump("KH59M"), kh35u().dump("KH35U"),
        kab(3.0, 0.20, False).dump("KAB500S"), kab(3.2, 0.1275, True).dump("KAB250L"),
        m62(1.96, 0.1625, 0.45, 0.55).dump("FAB250"), m62(2.47, 0.20, 0.55, 0.65).dump("FAB500"),
        m62(1.48, 0.1625, 0.35, 0.45, cell="olive").dump("OFAB250"),
        m62(2.30, 0.25, 0.35, 0.55, nose="round", band_z=0.30).dump("ODAB500"),
        betab500().dump("BETAB500"), m62(2.50, 0.225, 0.40, 0.60, cell="olive", nose="round", band_z=0.10).dump("RBK500"),
        rocket(1.57, 0.040, 0.10).dump("S8"), rocket(2.90, 0.061, 0.16, nose_len=0.35).dump("S13"),
        rocket(2.33, 0.120, 0.30, nose_len=0.45).dump("S24B"),
        rocket_pod(2.75, 0.26, b8m1_tubes(), 0.042, 0.35).dump("B8M1"), rocket_pod(3.50, 0.205, b13l_tubes(), 0.063, 0.40).dump("B13L"),
        apu73().dump("APU73"), apu73(5.0, 0.04).dump("APU73_O"), aku470().dump("AKU470"), aku58().dump("AKU58"), apu68().dump("APU68"),
        bd3_rack().dump("BD3"), bd3_rack(0.75).dump("BD3S"),
        # second batch
        r77().dump("R77"), r24(4.46, False).dump("R24R"), r24(4.16, True).dump("R24T"), aku170().dump("AKU170"),
        kh29(3.875, "laser").dump("KH29L"), kh29(3.875, "tv").dump("KH29T"), kh29(3.875, "tvd").dump("KH29TD"),
        kh38_sat().dump("KH38MK"), kh31(4.70, True).dump("KH31A"), kh31(5.23, False).dump("KH31P"), kh59().dump("KH59"),
        m62(1.07, 0.134, 0.28, 0.33).dump("FAB100"), m62(2.28, 0.225, 0.40, 0.55, cell="olive", nose="round", band_z=0.20).dump("ZAB500"),
        kab(3.05, 0.20, True).dump("KAB500L"), kab_tv(3.05, 0.175).dump("KAB500KR"), kab(2.90, 0.1275, False).dump("KAB250S"),
        pbk500u().dump("PBK500U"), m62(2.12, 0.1625, 0.30, 0.50, cell="olive", nose="round", band_z=0.10).dump("RBK250"),
        kmgu2().dump("KMGU2"), rn40().dump("RN40"),
        rocket(0.882, 0.0285, 0.07, nose_len=0.12).dump("S5"), rocket_pod(2.00, 0.205, ub32_tubes(), 0.029, 0.30).dump("UB32"),
        rocket(3.31, 0.17, 0.42, nose_len=0.55).dump("S25"), o25().dump("O25"), msp418k().dump("MSP418K"),
    ]
    json.dump({"parts": parts}, open(OUT + "/arsenal.json", "w"))
    for p in parts:
        v = np.array(p["vertices"]).reshape(-1, 3)
        print(p["name"], len(p["triangles"]) // 3, "tris, bbox", v.min(0).round(3), v.max(0).round(3))
