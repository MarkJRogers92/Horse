"""Anatomical signed-distance description of the TEMPORARY Cloverbrook pipeline horse.

This is a pipeline-validation placeholder, not the production horse (see D-002).
It produces a watertight mesh plus per-vertex anatomical data:
  * region tag (trunk / neck / head / ear_L / leg_FL / ... ) used for skin weights
  * mask channels used by the coat shader (points, soft-nose, midline, countershade)
  * canonical marking coordinates (face s/t, leg height) that are identical for
    adult and foal so an individual's markings grow with it.

Coordinates: metres, +X forward (toward the head), +Y the horse's left, +Z up,
ground at Z = 0.  Pure numpy + scikit-image; Blender is used only afterwards.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np

# --------------------------------------------------------------------------------------
# Profiles.  Adult numbers describe a ~1.60 m (15.3 hh) light riding horse.  The foal
# profile re-scales trunk, legs, neck and head independently (foals are mostly leg).
# --------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Profile:
    name: str
    s_body: float = 1.0      # trunk length/depth scale
    s_leg: float = 1.0       # vertical leg length scale
    s_leg_thick: float = 1.0 # leg radius scale (shafts)
    s_joint: float = 1.0     # joint radius scale (foals have knobbly joints)
    s_neck: float = 1.0
    s_head: float = 1.0
    s_eye: float = 1.0
    head_angle_deg: float = 52.0  # face line below horizontal
    ear_scale: float = 1.0
    mane_length: float = 0.24
    tail_length: float = 0.95
    resolution: float = 0.006


ADULT = Profile("adult")
FOAL = Profile("foal", s_body=0.58, s_leg=0.80, s_leg_thick=0.60, s_joint=0.70, s_neck=0.52,
               s_head=0.70, s_eye=0.84, head_angle_deg=50.0, ear_scale=0.80,
               mane_length=0.07, tail_length=0.38, resolution=0.0045)

LEG_TOP = 1.00  # adult height where legs meet the trunk (elbow / stifle line)
LEGS = {  # id: (front?, lateral sign)
    "FL": (True, 1.0), "FR": (True, -1.0), "HL": (False, 1.0), "HR": (False, -1.0),
}
LEG_INDEX = {"FL": 0, "FR": 1, "HL": 2, "HR": 3}


# --------------------------------------------------------------------------------------
# SDF primitives (vectorised over an (N,3) array of points)
# --------------------------------------------------------------------------------------

def rot_y(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_x(deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


@dataclass
class Prim:
    kind: str                      # "ell" | "cone" | "plane_floor"
    group: str                     # region tag
    a: np.ndarray                  # centre (ell) or start (cone)
    b: np.ndarray | None = None    # end (cone)
    r: np.ndarray | float = 0.0    # radii (ell) or start radius (cone)
    rb: float = 0.0                # end radius (cone)
    rot: np.ndarray = field(default_factory=lambda: np.eye(3))
    k: float = 0.04                # smooth-union blend radius
    subtract: bool = False
    mask: dict = field(default_factory=dict)  # mask channel weights carried by this prim

    def sdf(self, p):
        if self.kind == "ell":
            q = (p - self.a) @ self.rot
            r = np.asarray(self.r)
            k0 = np.linalg.norm(q / r, axis=1)
            k1 = np.linalg.norm(q / (r * r), axis=1)
            return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)
        if self.kind == "cone":  # round cone (capsule with linearly varying radius)
            a, b, r1, r2 = self.a, self.b, float(self.r), self.rb
            ba = b - a; l2 = ba @ ba; rr = r1 - r2; a2 = l2 - rr * rr; il2 = 1.0 / l2
            pa = p - a; y = pa @ ba; z = y - l2
            x = pa * l2 - np.outer(y, ba); x2 = np.einsum("ij,ij->i", x, x)
            y2 = y * y * l2; z2 = z * z * l2
            k = np.sign(rr) * rr * rr * x2
            out = (np.sqrt(x2 * a2 * il2) + y * rr) * il2 - r1
            m1 = np.sign(z) * a2 * z2 > k
            m2 = np.sign(y) * a2 * y2 < k
            out = np.where(m1, np.sqrt(x2 + z2) * il2 - r2, out)
            out = np.where(m2 & ~m1, np.sqrt(x2 + y2) * il2 - r1, out)
            return out
        raise ValueError(self.kind)


def smin(a, b, k):
    if k <= 0: return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax_sub(d, s, k):  # smooth subtraction of s from d
    h = np.clip(0.5 - 0.5 * (d + s) / k, 0.0, 1.0)
    return d + (-s - d) * h + k * h * (1.0 - h)


# --------------------------------------------------------------------------------------
# Anatomy
# --------------------------------------------------------------------------------------

class Horse:
    def __init__(self, profile: Profile):
        self.p = profile
        self.prims: list[Prim] = []
        self.landmarks: dict[str, np.ndarray] = {}
        self._build()

    # coordinate helpers -------------------------------------------------------------
    def T(self, x, z, y=0.0):
        """Adult trunk coordinate -> profile coordinate."""
        p = self.p
        return np.array([x * p.s_body, y * p.s_body, LEG_TOP * p.s_leg + (z - LEG_TOP) * p.s_body])

    def L(self, attach_x, x, z, y):
        """Adult leg coordinate -> profile coordinate (legs scale differently)."""
        p = self.p
        return np.array([attach_x * p.s_body + (x - attach_x) * p.s_leg, y * (0.55 + 0.45 * p.s_body), z * p.s_leg])

    def add(self, **kw):
        prim = Prim(**kw); self.prims.append(prim); return prim

    def ell(self, group, c, r, rot=None, k=0.05, **kw):
        return self.add(kind="ell", group=group, a=np.asarray(c, float), r=np.asarray(r, float),
                        rot=np.eye(3) if rot is None else rot, k=k, **kw)

    def cone(self, group, a, b, ra, rb, k=0.04, **kw):
        return self.add(kind="cone", group=group, a=np.asarray(a, float), b=np.asarray(b, float), r=ra, rb=rb, k=k, **kw)

    # build ---------------------------------------------------------------------------
    def _build(self):
        p = self.p; sb = p.s_body
        S = lambda *r: np.array(r) * sb  # trunk radii

        # Trunk ---------------------------------------------------------------------
        self.ell("trunk", self.T(0.0, 1.16), S(0.53, 0.285, 0.35), k=0.0, mask={"counter": 0.2})
        self.ell("trunk", self.T(0.08, 0.97), S(0.40, 0.24, 0.15), k=0.10, mask={"counter": 1.0})          # belly
        self.ell("trunk", self.T(0.50, 1.22), S(0.30, 0.245, 0.40), rot_y(-22), k=0.10)                   # shoulder mass
        self.ell("trunk", self.T(0.46, 1.50), S(0.26, 0.085, 0.12), rot_y(8), k=0.10, mask={"mid": 1.0})  # withers
        self.ell("trunk", self.T(-0.58, 1.27), S(0.37, 0.27, 0.33), k=0.10)                               # quarters
        self.ell("trunk", self.T(-0.58, 1.46), S(0.30, 0.19, 0.12), k=0.08, mask={"mid": 1.0})           # croup
        self.ell("trunk", self.T(-0.18, 1.44), S(0.40, 0.18, 0.10), k=0.10, mask={"mid": 1.0})           # loin / back
        self.ell("trunk", self.T(-0.86, 1.22), S(0.13, 0.19, 0.22), k=0.08)                               # buttocks
        self.ell("trunk", self.T(0.79, 1.10), S(0.11, 0.17, 0.17), k=0.08)                                # breast
        for side in (1, -1):
            self.ell("trunk", self.T(0.76, 1.08, 0.075 * side), S(0.09, 0.08, 0.12), k=0.09)              # pectorals
            self.ell("trunk", self.T(-0.66, 1.03, 0.125 * side), S(0.22, 0.14, 0.28), rot_y(15), k=0.08)  # thighs
            self.ell("trunk", self.T(-0.30, 0.98, 0.17 * side), S(0.20, 0.10, 0.12), k=0.10, mask={"counter": 1.0})  # flank
            self.ell("trunk", self.T(0.60, 1.05, 0.13 * side), S(0.15, 0.10, 0.16), rot_y(-30), k=0.06)   # triceps
        self.cone("tail", self.T(-0.88, 1.50), self.T(-1.02, 1.36), 0.06 * sb, 0.04 * sb, k=0.05)        # dock

        # Legs ----------------------------------------------------------------------
        th, jt = p.s_leg_thick, p.s_joint
        for leg, (front, side) in LEGS.items():
            g = "leg_" + leg; y = side * (0.135 if front else 0.145)
            if front:
                ax = 0.60
                P = lambda x, z: self.L(ax, x, z, y)
                self.cone(g, P(0.62, 1.00), P(0.60, 0.56), 0.088 * th, 0.050 * th, k=0.06)                  # forearm
                self.ell(g, P(0.615, 0.80), np.array([0.07, 0.062, 0.15]) * th, k=0.04)                    # forearm muscle
                self.ell(g, P(0.60, 0.52), np.array([0.056, 0.050, 0.062]) * jt, k=0.03)                   # knee
                self.cone(g, P(0.60, 0.50), P(0.60, 0.22), 0.040 * th, 0.037 * th, k=0.025)                 # cannon
                self.cone(g, P(0.575, 0.47), P(0.578, 0.24), 0.024 * th, 0.024 * th, k=0.02)                # tendons
                knee_z, top_z = 0.52, 1.0
            else:
                ax = -0.62
                P = lambda x, z: self.L(ax, x, z, y)
                self.cone(g, P(-0.58, 1.00), P(-0.73, 0.60), 0.10 * th, 0.055 * th, k=0.06)                 # gaskin
                self.ell(g, P(-0.64, 0.83), np.array([0.10, 0.07, 0.14]) * th, rot_y(25), k=0.05)          # gaskin muscle
                self.ell(g, P(-0.74, 0.57), np.array([0.066, 0.050, 0.066]) * jt, k=0.03)                  # hock
                self.ell(g, P(-0.795, 0.60), np.array([0.032, 0.03, 0.04]) * jt, k=0.03)                   # point of hock
                self.cone(g, P(-0.73, 0.55), P(-0.71, 0.22), 0.044 * th, 0.039 * th, k=0.025)               # cannon
                self.cone(g, P(-0.755, 0.52), P(-0.735, 0.24), 0.024 * th, 0.024 * th, k=0.02)              # tendons
                knee_z, top_z = 0.57, 1.0
            fx = 0.60 if front else -0.705
            self.ell(g, P(fx - 0.005, 0.19), np.array([0.052, 0.046, 0.05]) * jt, k=0.025)                 # fetlock
            self.cone(g, P(fx, 0.18), P(fx + 0.045, 0.085), 0.036 * th, 0.035 * th, k=0.02)                 # pastern
            self.cone(g, P(fx + 0.05, 0.08), P(fx + 0.078, 0.02), 0.046 * jt, 0.064 * jt, k=0.012)
            self.ell(g, P(fx + 0.068, 0.03), np.array([0.07, 0.062, 0.03]) * jt, k=0.01)        # hoof
            self.landmarks["hoof_" + leg] = P(fx + 0.08, 0.0)
            self.landmarks["knee_" + leg] = P(fx, knee_z)
            self.landmarks["fetlock_" + leg] = P(fx - 0.005, 0.19)
            self.landmarks["coronet_" + leg] = P(fx + 0.045, 0.085)
            self.landmarks["legtop_" + leg] = P(0.61 if front else -0.60, top_z)

        # Neck (ellipsoid chain along a bezier from withers to poll) -------------------
        base = self.T(0.66, 1.33)
        poll_adult = np.array([1.17, 0.0, 1.98])
        end = base + (self.T(poll_adult[0], poll_adult[2]) - base) * (p.s_neck / sb)
        # control point keeps the crest convex
        ctrl = base + (end - base) * np.array([0.42, 0, 0.80]) + np.array([0, 0, 0.07 * p.s_neck])
        n = 14
        for i in range(n):
            t = i / (n - 1)
            c = (1 - t) ** 2 * base + 2 * (1 - t) * t * ctrl + t * t * end
            d = 2 * (1 - t) * (ctrl - base) + 2 * t * (end - ctrl)
            ang = math.degrees(math.atan2(d[2], d[0]))
            depth = (0.33 * (1 - t) ** 1.3 + 0.125 * (1 - (1 - t) ** 1.3)) * (p.s_neck * 0.5 + sb * 0.5)
            width = (0.17 * (1 - t) + 0.08 * t) * (p.s_neck * 0.4 + sb * 0.6)
            along = 0.19 * max(p.s_neck, 0.6)
            self.ell("neck", c, [along, width, depth], rot_y(-ang), k=0.09,
                     mask={"mid": 0.0})
        self.landmarks["neck_base"] = base; self.landmarks["poll"] = end; self.landmarks["neck_ctrl"] = ctrl

        # Head (built in a head frame: u along the face, n toward the jaw) -------------
        Lh = 0.58 * p.s_head
        a = math.radians(p.head_angle_deg)
        u = np.array([math.cos(a), 0, -math.sin(a)]); nrm = np.array([-math.sin(a), 0, -math.cos(a)])
        R = np.column_stack([u, [0, 1, 0], nrm])  # local (u, y, n) -> world
        poll = end
        H = lambda su, sn, sy=0.0: poll + u * (su * Lh) + nrm * (sn * Lh) + np.array([0, sy * Lh, 0])
        hr = lambda *r: np.array(r) * Lh
        rotH = R  # ellipsoid local axes == head frame
        self.ell("head", H(0.17, 0.05), hr(0.22, 0.175, 0.19), rotH, k=0.05)                              # cranium/forehead
        self.ell("head", H(0.33, 0.25), hr(0.21, 0.15, 0.20), rotH, k=0.06)                               # jowls
        self.cone("head", H(0.30, 0.03), H(0.86, 0.05), 0.135 * Lh, 0.105 * Lh, k=0.06)                    # nasal / face
        self.ell("head", H(0.93, 0.10), hr(0.12, 0.115, 0.15), rotH, k=0.05, mask={"nose": 1.0})          # muzzle
        self.ell("head", H(0.88, 0.22), hr(0.09, 0.08, 0.06), rotH, k=0.04, mask={"nose": 1.0})           # chin/lip
        self.cone("head", H(0.40, 0.33), H(0.84, 0.20), 0.08 * Lh, 0.06 * Lh, k=0.05)                     # lower jaw
        for side in (1, -1):
            self.ell("head", H(0.94, 0.045, 0.072 * side), hr(0.05, 0.03, 0.04), rotH, k=0.025, subtract=True)  # nostrils
            self.ell("head", H(0.27, 0.0, 0.155 * side), hr(0.06, 0.04, 0.03), rotH, k=0.03)                    # brow ridge
            # eye socket (carved) — the eyeball is a separate mesh placed at landmark
            eye_c = H(0.33, 0.07, 0.150 * side)
            self.ell("head", eye_c, np.array([0.03, 0.02, 0.028]) * p.s_eye * (Lh / 0.58) ** 0.4, rotH, k=0.012,
                     subtract=True, mask={"nose": 0.6})
            self.landmarks["eye_" + ("L" if side > 0 else "R")] = eye_c - np.array([0, 0.009 * side, 0])
        self.landmarks["muzzle"] = H(1.0, 0.1)
        self.head_frame = (poll, u, nrm, Lh)

        # Ears ------------------------------------------------------------------------
        es = p.ear_scale * (0.75 + 0.25 * p.s_head)
        for side, tag in ((1, "ear_L"), (-1, "ear_R")):
            base_e = H(0.03, -0.06, 0.085 * side)
            up = np.array([-0.15, 0.22 * side, 1.0]); up /= np.linalg.norm(up)
            # ear frame: z=up, x=forward (opening), y=lateral
            fwd = np.array([1.0, 0.25 * side, 0.15]); fwd -= up * (fwd @ up); fwd /= np.linalg.norm(fwd)
            lat = np.cross(up, fwd)
            Re = np.column_stack([fwd, lat, up])
            c = base_e + up * 0.075 * es
            self.ell(tag, c + up * 0.02 * es, np.array([0.036, 0.05, 0.105]) * es, Re, k=0.02, mask={"points": 0.5})
            self.ell(tag, c + up * 0.11 * es, np.array([0.014, 0.02, 0.04]) * es, Re, k=0.025, mask={"points": 1.0})  # tip
            self.ell(tag, c + fwd * 0.034 * es + up * 0.03 * es, np.array([0.024, 0.038, 0.09]) * es, Re, k=0.01, subtract=True)
            self.landmarks[tag + "_base"] = base_e; self.landmarks[tag + "_tip"] = c + up * 0.15 * es

        self.landmarks["withers"] = self.T(0.46, 1.60)
        self.landmarks["tail_root"] = self.T(-0.92, 1.48)
        self.landmarks["chest"] = self.T(0.30, 1.30)
        self.landmarks["hips"] = self.T(-0.55, 1.35)

    # evaluation ------------------------------------------------------------------------
    def bounds(self):
        lo = np.array([np.inf] * 3); hi = -lo
        for pr in self.prims:
            pts = [pr.a] + ([pr.b] if pr.b is not None else [])
            rad = float(np.max(pr.r)) if pr.kind == "ell" else max(float(pr.r), pr.rb)
            for q in pts:
                lo = np.minimum(lo, q - rad); hi = np.maximum(hi, q + rad)
        lo[2] = -0.01
        return lo - 0.03, hi + 0.03

    def _prim_box(self, pr, margin):
        pts = [pr.a] + ([pr.b] if pr.b is not None else [])
        rad = float(np.max(pr.r)) if pr.kind == "ell" else max(float(pr.r), pr.rb)
        lo = np.min(pts, 0) - rad - margin; hi = np.max(pts, 0) + rad + margin
        return lo, hi

    def field(self, pts, margin=0.0):
        """Smooth-union field.  Each primitive is evaluated only inside its padded box;
        outside it cannot change the surface (distance > blend radius)."""
        d = np.full(len(pts), 1e3)
        for pr in self.prims:
            lo, hi = self._prim_box(pr, pr.k + margin + 0.02)
            m = np.all((pts >= lo) & (pts <= hi), 1)
            if not m.any():
                continue
            s = pr.sdf(pts[m])
            d[m] = smax_sub(d[m], s, pr.k) if pr.subtract else smin(d[m], s, pr.k)
        return np.maximum(d, -pts[:, 2])  # flat ground contact under the hooves

    def mesh(self):
        from skimage import measure
        lo, hi = self.bounds(); h = self.p.resolution
        nx, ny, nz = (np.ceil((hi - lo) / h).astype(int) + 1)
        xs, ys, zs = (lo[i] + np.arange(n) * h for i, n in enumerate((nx, ny, nz)))
        vol = np.empty((nx, ny, nz), np.float32)
        Y, Z = np.meshgrid(ys, zs, indexing="ij")
        for i, x in enumerate(xs):
            pts = np.column_stack([np.full(Y.size, x), Y.ravel(), Z.ravel()])
            vol[i] = self.field(pts).reshape(ny, nz)
        verts, faces, normals, _ = measure.marching_cubes(vol, 0.0, spacing=(h, h, h))
        verts += lo
        # The field is negative inside. These faces already wind outward;
        # reversing them made the vertex normals face inward, suppressing the
        # forehead marking mask and lighting the placeholder from its interior.
        return verts, faces.copy()

    # per-vertex anatomy ----------------------------------------------------------------
    def vertex_data(self, verts, normals):
        """Region tag, mask channels and canonical marking coordinates per vertex."""
        groups = sorted({pr.group for pr in self.prims if not pr.subtract})
        gd = {g: np.full(len(verts), 1e3) for g in groups}
        chan = {c: np.zeros(len(verts)) for c in ("points", "nose", "mid", "counter")}
        chw = np.full(len(verts), 1e-6)
        for pr in self.prims:
            s = pr.sdf(verts)
            if pr.subtract:
                if pr.mask:  # carved regions (eye sockets) tint nearby skin
                    w = np.exp(-np.maximum(-s, 0) / 0.01) * (s > -0.02) * np.exp(-np.abs(s) / 0.01)
                    for c, v in pr.mask.items(): chan[c] = np.maximum(chan[c], v * w)
                continue
            gd[pr.group] = np.minimum(gd[pr.group], s)
            w = np.exp(-np.maximum(s, 0) / 0.012)
            for c, v in pr.mask.items():
                chan[c] = np.maximum(chan[c], v * w)
        stack = np.stack([gd[g] for g in groups], 1)
        region = np.array(groups)[np.argmin(stack, 1)]

        # Points: lower legs (graded by canonical leg height), ear rims, muzzle
        legh = np.full(len(verts), 50.0); legid = np.full(len(verts), -10.0)
        for leg in LEGS:
            m = region == "leg_" + leg
            knee = self.landmarks["knee_" + leg][2]
            legh[m] = verts[m, 2] / knee      # 0 ground .. 1 knee/hock
            legid[m] = 10 + LEG_INDEX[leg]
        pts_leg = np.clip(1.25 - legh * 0.55, 0, 1) * (legh < 50)
        chan["points"] = np.maximum(chan["points"], pts_leg)

        # Face frame coordinates for head markings
        poll, u, nrm, Lh = self.head_frame
        rel = verts - poll
        s_face = (rel @ u) / Lh
        t_face = verts[:, 1] / (0.17 * Lh)
        # The last neck segment overlaps the poll; anything in front of the poll plane
        # that is close to the head surface belongs to the head (forehead, star area).
        reclaim = (region == "neck") & (s_face > 0.04) & (gd["head"] < 0.03)
        region = np.where(reclaim, "head", region)
        front = np.clip((normals @ (-nrm) - 0.15) / 0.5, 0, 1)
        head = region == "head"
        # Midline: dorsal stripe on trunk/neck (top surface near y=0), face front on head
        dorsal = np.exp(-(verts[:, 1] / 0.03) ** 2) * np.clip((normals[:, 2] - 0.3) / 0.5, 0, 1)
        mid = np.where(head, front, np.where(np.isin(region, ["trunk", "neck", "tail"]), dorsal, 0.0))
        chan["mid"] = mid

        uv2 = np.zeros((len(verts), 2))
        uv2[:] = (-10.0, 50.0)
        uv2[head] = np.column_stack([s_face[head], t_face[head]])
        legm = legh < 50
        uv2[legm] = np.column_stack([legid[legm], legh[legm]])

        # Countershade: belly + inner legs + flank (mealy / pangaré areas)
        belly = np.clip((self.T(0, 1.0)[2] - verts[:, 2]) / 0.12 + 0.5, 0, 1) * np.isin(region, ["trunk"])
        chan["counter"] = np.maximum(chan["counter"] * 0.6, belly)

        color = np.column_stack([chan["points"], np.clip(chan["nose"], 0, 1), np.clip(chan["mid"], 0, 1), np.clip(chan["counter"], 0, 1)])
        return region, np.clip(color, 0, 1), uv2
