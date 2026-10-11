"""Smooth inherited nearest-vertex Mane weight discontinuities.

The source hair geometry/UVs/colors/morphs and exact Body attachment roots are
immutable. A constrained harmonic blend of the existing weights spreads the
scapula/spine transition over the shoulder hair instead of a single short edge.
No controller or Body/hoof weights are changed.
"""
import numpy as np


def repair_mane(mane, body):
    names = [g.name for g in mane.vertex_groups]
    weights = np.zeros((len(mane.data.vertices), len(names)), dtype=np.float64)
    for v in mane.data.vertices:
        for g in v.groups:
            weights[v.index, g.group] = g.weight
    original = weights.copy()
    body_positions = {tuple(round(c, 6) for c in v.co) for v in body.data.vertices}
    roots = np.array([tuple(round(c, 6) for c in v.co) in body_positions for v in mane.data.vertices])
    coordinates = np.array([tuple(v.co) for v in mane.data.vertices])
    # Exact attachment roots are Dirichlet boundaries; diffuse only free hair.
    mobile = ~roots
    edges = np.array([tuple(e.vertices) for e in mane.data.edges])
    a = np.concatenate([edges[:, 0], edges[:, 1]])
    b = np.concatenate([edges[:, 1], edges[:, 0]])
    coefficient = 1.0 / np.maximum(np.linalg.norm(coordinates[a] - coordinates[b], axis=1), 1e-6)
    sums = np.bincount(a, weights=coefficient, minlength=len(weights))
    ids = np.flatnonzero(mobile)
    local = {int(v): i for i, v in enumerate(ids)}
    laplace = np.diag(sums[ids])
    boundary = np.zeros((len(ids), len(names)))
    for va, vb, factor in zip(a, b, coefficient):
        if va not in local:
            continue
        row = local[int(va)]
        if vb in local:
            laplace[row, local[int(vb)]] -= factor
        else:
            boundary[row] += factor * original[vb]
    weights[ids] = np.linalg.solve(laplace, boundary)
    residual = float(np.max(np.abs(laplace @ weights[ids] - boundary)))
    for i in np.flatnonzero(mobile):
        keep = np.argsort(weights[i])[-8:]
        values = np.zeros(len(names))
        values[keep] = weights[i, keep]
        values /= values.sum()
        weights[i] = values
        for g in list(mane.data.vertices[int(i)].groups):
            mane.vertex_groups[g.group].remove([int(i)])
        for j in np.flatnonzero(values > 1e-8):
            mane.vertex_groups[int(j)].add([int(i)], float(values[j]), 'REPLACE')
    assert np.array_equal(weights[roots], original[roots])
    return {'method': 'Inverse-edge-length harmonic extension over free Mane, all exact Body-coincident roots locked; at most eight influences.',
            'rootCount': int(roots.sum()), 'modifiedVertices': int(np.count_nonzero(np.max(np.abs(weights-original),axis=1)>1e-7)),
            'linearSolveMaximumResidual': residual,
            'rootWeightsExact': True, 'lockedWeightsExact': bool(np.array_equal(weights[~mobile], original[~mobile]))}
