"""Re-split the sea provinces of a start with Voronoi cells bounded by contours.

``regions.png`` of a start is a Gall stereographic projection while
``height.png`` is equirectangular. This tool reprojects the height map onto the
regions grid, draws quantized contour lines, places jittered Voronoi seeds in
every contour-bounded sea region, and rewrites ``regions.png`` and
``provinces.json`` so that only the sea (type 0) provinces change.

The game map is a flat image, so nothing here wraps at the x seam.
"""

from __future__ import annotations

import argparse
import copy
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

ROOT = Path(__file__).resolve().parents[2]
REPO = ROOT.parents[1]
HEIGHT = ROOT / "height.png"
START = REPO / "starts" / "ModernDay"
SEA_TYPE = 0
GALL_K = 1 + math.sqrt(2) / 2


def load_ids(a_path: Path) -> np.ndarray:
    rgb = np.asarray(Image.open(a_path).convert("RGB")).astype(np.int64)
    return (rgb[..., 0] << 16) | (rgb[..., 1] << 8) | rgb[..., 2]


def save_ids(a_ids: np.ndarray, a_path: Path) -> None:
    rgba = np.empty(a_ids.shape + (4,), np.uint8)
    rgba[..., 0] = (a_ids >> 16) & 255
    rgba[..., 1] = (a_ids >> 8) & 255
    rgba[..., 2] = a_ids & 255
    rgba[..., 3] = 255
    Image.fromarray(rgba, "RGBA").save(a_path)


def reproject_height(
    a_height: np.ndarray,
    a_size: tuple[int, int],
    a_lat_top: float,
    a_lat_bottom: float,
    a_lon_left: float,
    a_lon_span: float,
) -> np.ndarray:
    """Sample an equirectangular height map into a Gall stereographic grid."""
    h, w = a_size
    src_h, src_w = a_height.shape
    y_top = GALL_K * math.tan(math.radians(a_lat_top) / 2)
    y_bottom = GALL_K * math.tan(math.radians(a_lat_bottom) / 2)
    v = (np.arange(h) + 0.5) / h
    lat = np.degrees(2 * np.arctan((y_top + (y_bottom - y_top) * v) / GALL_K))
    lon = a_lon_left + a_lon_span * (np.arange(w) + 0.5) / w
    rows = np.clip((90 - lat) / 180 * src_h - 0.5, 0, src_h - 1)
    cols = (lon + 180) / 360 * src_w - 0.5
    rr, cc = np.meshgrid(rows, cols, indexing="ij")
    out = ndimage.map_coordinates(
        a_height.astype(np.float32), [rr, cc], order=1, mode="grid-wrap"
    )
    return out


def contour_mask(a_height: np.ndarray, a_interval: int) -> np.ndarray:
    """True where the quantized level differs from the right/down/diagonal pixel."""
    levels = (a_height // a_interval).astype(np.int32)
    mask = np.zeros(levels.shape, bool)
    mask[:, :-1] |= levels[:, :-1] != levels[:, 1:]
    mask[:-1, :] |= levels[:-1, :] != levels[1:, :]
    mask[:-1, :-1] |= levels[:-1, :-1] != levels[1:, 1:]
    return mask


def jittered_seeds(
    a_labels: np.ndarray, a_cell: int, a_rng: np.random.Generator
) -> tuple[np.ndarray, np.ndarray]:
    """One random valid pixel per (region, grid cell). Returns (seed_xy, seed_region)."""
    h, w = a_labels.shape
    valid = a_labels > 0
    inner = valid.copy()
    inner[:, 1:] &= valid[:, :-1]
    inner[:, :-1] &= valid[:, 1:]
    inner[1:, :] &= valid[:-1, :]
    inner[:-1, :] &= valid[1:, :]
    ys, xs = np.nonzero(inner)
    region = a_labels[ys, xs].astype(np.int64)
    cell = (ys // a_cell) * (-(-w // a_cell)) + (xs // a_cell)
    key = region * (h * w) + cell
    rnd = a_rng.random(len(key))
    order = np.lexsort((rnd, key))
    key_sorted = key[order]
    first = np.ones(len(order), bool)
    first[1:] = key_sorted[1:] != key_sorted[:-1]
    pick = order[first]
    seeds = np.stack([xs[pick], ys[pick]], 1).astype(np.float64)
    seeds += a_rng.random(seeds.shape)
    return seeds, region[pick]


def voronoi_owners(
    a_labels: np.ndarray, a_seeds: np.ndarray, a_seed_region: np.ndarray
) -> np.ndarray:
    """Owner seed id per pixel (-1 none): nearest seed of the same region."""
    owners = np.full(a_labels.shape, -1, np.int32)
    flat_labels = a_labels.ravel()
    order = np.argsort(flat_labels, kind="stable")
    sorted_labels = flat_labels[order]
    bounds = np.searchsorted(sorted_labels, np.arange(a_labels.max() + 2))
    seed_order = np.argsort(a_seed_region, kind="stable")
    seed_bounds = np.searchsorted(
        a_seed_region[seed_order], np.arange(a_labels.max() + 2)
    )
    w = a_labels.shape[1]
    out = owners.ravel()
    for region in range(1, a_labels.max() + 1):
        s0, s1 = seed_bounds[region], seed_bounds[region + 1]
        if s0 == s1:
            continue
        idx = order[bounds[region] : bounds[region + 1]]
        seed_ids = seed_order[s0:s1]
        tree = cKDTree(a_seeds[seed_ids])
        pts = np.stack([idx % w, idx // w], 1) + 0.5
        _, nearest = tree.query(pts)
        out[idx] = seed_ids[nearest]
    return owners


def fill_unowned(a_owners: np.ndarray, a_sea: np.ndarray) -> np.ndarray:
    """Give sea pixels without an owner the owner of the nearest owned pixel."""
    missing = a_sea & (a_owners < 0)
    if not missing.any():
        return a_owners
    _, (iy, ix) = ndimage.distance_transform_edt(
        a_owners < 0, return_indices=True
    )
    filled = a_owners[iy, ix]
    return np.where(missing, filled, a_owners)


def split_components(a_owners: np.ndarray, a_sea: np.ndarray) -> np.ndarray:
    """Label 4-connected same-owner sea pixels. Land and unowned pixels get 0."""
    comp = np.zeros(a_owners.shape, np.int32)
    count = 0
    slices = ndimage.find_objects(np.where(a_sea, a_owners + 1, 0))
    for owner_index, sl in enumerate(slices):
        if sl is None:
            continue
        sub = (a_owners[sl] == owner_index) & a_sea[sl]
        labeled, n = ndimage.label(sub)
        if n == 0:
            continue
        view = comp[sl]
        view[labeled > 0] = labeled[labeled > 0] + count
        count += n
    return comp


def contour_protected(
    a_comp: np.ndarray, a_labels: np.ndarray, a_min_region: int
) -> np.ndarray:
    """Flag components that are (nearly) a whole contour-bounded sea region.

    A region of at least a_min_region pixels that one component covers by 90%
    or more stays its own province, however small it is.
    """
    protected = np.zeros(a_comp.max() + 1, bool)
    region_area = np.bincount(a_labels.ravel())
    both = (a_comp > 0) & (a_labels > 0)
    pair = a_comp[both].astype(np.int64) * (a_labels.max() + 1) + a_labels[both]
    uniq, counts = np.unique(pair, return_counts=True)
    comp_ids, region_ids = uniq // (a_labels.max() + 1), uniq % (a_labels.max() + 1)
    hit = (region_area[region_ids] >= a_min_region) & (counts >= 0.9 * region_area[region_ids])
    protected[comp_ids[hit]] = True
    return protected


def merge_small(
    a_comp: np.ndarray, a_min_pixels: int, a_protected: np.ndarray | None = None
) -> np.ndarray:
    """Merge components smaller than a_min_pixels into their longest-border neighbour."""
    comp = a_comp.copy()
    for _ in range(6):
        sizes = np.bincount(comp.ravel())
        a = np.concatenate([comp[:, :-1].ravel(), comp[:-1, :].ravel()])
        b = np.concatenate([comp[:, 1:].ravel(), comp[1:, :].ravel()])
        keep = (a > 0) & (b > 0) & (a != b)
        a, b = a[keep], b[keep]
        pairs = np.concatenate([np.stack([a, b], 1), np.stack([b, a], 1)])
        uniq, counts = np.unique(pairs, axis=0, return_counts=True)
        small = sizes < a_min_pixels
        small[0] = False
        if a_protected is not None:
            small &= ~a_protected[: len(sizes)]
        remap = np.arange(len(sizes))
        best_count = np.zeros(len(sizes), np.int64)
        for (src, dst), n in zip(uniq, counts):
            # merge only toward a bigger label (ties by id) so chains never cycle
            bigger = (sizes[dst], dst) > (sizes[src], src)
            if small[src] and bigger and n > best_count[src]:
                best_count[src] = n
                remap[src] = dst
        if (remap == np.arange(len(sizes))).all():
            break
        # resolve chains
        for _ in range(8):
            remap = remap[remap]
        comp = remap[comp].astype(np.int32)
    return comp


def assign_ids(
    a_comp: np.ndarray,
    a_old_ids: np.ndarray,
    a_old_sea_ids: set[int],
    a_next_id: int,
) -> tuple[dict[int, int], dict[int, int]]:
    """Map component -> province id by largest overlap. Returns (comp->id, comp->source old id)."""
    sea = a_comp > 0
    comps = a_comp[sea].astype(np.int64)
    olds = a_old_ids[sea].astype(np.int64)
    pair = comps * (1 << 25) + olds
    uniq, counts = np.unique(pair, return_counts=True)
    ucomp, uold = uniq >> 25, uniq & ((1 << 25) - 1)
    order = np.argsort(-counts, kind="stable")
    comp_to_id: dict[int, int] = {}
    comp_source: dict[int, int] = {}
    used: set[int] = set()
    for i in order:
        c, o = int(ucomp[i]), int(uold[i])
        if c not in comp_source:
            comp_source[c] = o
        if c in comp_to_id or o in used or o not in a_old_sea_ids:
            continue
        comp_to_id[c] = o
        used.add(o)
    next_id = a_next_id
    for c in sorted(set(ucomp.tolist())):
        if c not in comp_to_id:
            comp_to_id[c] = next_id
            next_id += 1
    return comp_to_id, comp_source


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--start", type=Path, default=START)
    p.add_argument("--height", type=Path, default=HEIGHT)
    p.add_argument("--cell-size", type=int, default=40)
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--contour-interval", type=int, default=16)
    p.add_argument("--min-pixels", type=int, default=600)
    p.add_argument(
        "--min-contour-pixels",
        type=int,
        default=150,
        help="contour-bounded regions at least this big stay their own province",
    )
    p.add_argument(
        "--tectonic",
        action="store_true",
        help="cut regions at plate boundaries and gradient-tectonic contour layers",
    )
    p.add_argument("--tectonic-data", type=Path, default=ROOT / "data" / "PB2002_boundaries.json")
    p.add_argument("--sea-contour-interval", type=int, default=64)
    p.add_argument("--inland-contour-interval", type=int, default=16)
    p.add_argument("--mediterranean-contour-interval", type=int, default=32)
    p.add_argument("--blur", type=float, default=2.0)
    p.add_argument("--lat-top", type=float, default=91.22)
    p.add_argument("--lat-bottom", type=float, default=-92.29)
    p.add_argument("--lon-left", type=float, default=-169.48)
    p.add_argument("--lon-span", type=float, default=357.98)
    p.add_argument("--dry-run", action="store_true", help="skip rewriting start files")
    args = p.parse_args()

    regions_path = args.start / "regions.png"
    provinces_path = args.start / "provinces.json"
    provinces = json.loads(provinces_path.read_text())
    old_ids = load_ids(regions_path)
    h, w = old_ids.shape

    sea_ids = {int(k) for k, v in provinces.items() if v.get("type", SEA_TYPE) == SEA_TYPE}
    sea = np.isin(old_ids, np.fromiter(sea_ids, np.int64))

    with Image.open(args.height) as img:
        height = np.asarray(img.convert("L"))
    projected = reproject_height(
        height, (h, w), args.lat_top, args.lat_bottom, args.lon_left, args.lon_span
    )
    projected = ndimage.gaussian_filter(projected, args.blur)
    Image.fromarray(np.clip(projected, 0, 255).astype(np.uint8)).save(
        ROOT / "height_modernday.png"
    )

    tectonic = np.zeros((h, w), bool)
    if args.tectonic:
        from . import tectonic as tectonic_layers

        # Same layers as gradient-tectonic: coarse sea contours, fine contours
        # in enclosed seas, plate boundaries. The game's sea mask replaces the
        # Natural Earth coastline.
        quantized = np.clip(projected, 0, 255).astype(np.uint8).astype(np.float32)
        contours = contour_mask(quantized, args.sea_contour_interval) & sea
        inland = tectonic_layers.inland_mask(
            (h, w),
            "gall",
            args.lat_top,
            args.lat_bottom,
            args.lon_left,
            args.lon_span,
            tectonic_layers.OTHER_INLAND_SEAS,
        )
        mediterranean = tectonic_layers.inland_mask(
            (h, w),
            "gall",
            args.lat_top,
            args.lat_bottom,
            args.lon_left,
            args.lon_span,
            (tectonic_layers.MEDITERRANEAN,),
        )
        contours |= contour_mask(quantized, args.inland_contour_interval) & inland & sea
        contours |= (
            contour_mask(quantized, args.mediterranean_contour_interval)
            & mediterranean
            & sea
        )
        project = tectonic_layers.make_projector(
            "gall", (w, h), args.lat_top, args.lat_bottom, args.lon_left, args.lon_span
        )
        tectonic = tectonic_layers.raster_lines(
            (w, h), tectonic_layers.geojson_lines(args.tectonic_data), project, 1
        )
        tectonic &= sea
        barriers = contours | tectonic
    else:
        contours = contour_mask(projected, args.contour_interval)
        barriers = contours
    labels, _ = ndimage.label(sea & ~barriers)
    rng = np.random.default_rng(args.seed)
    seeds, seed_region = jittered_seeds(labels, args.cell_size, rng)
    owners = voronoi_owners(labels, seeds, seed_region)
    owners = fill_unowned(owners, sea)

    comp = split_components(owners, sea)
    protected = contour_protected(comp, labels, args.min_contour_pixels)
    comp = merge_small(comp, args.min_pixels, protected)
    # relabel densely
    _, inverse = np.unique(comp, return_inverse=True)
    comp = inverse.reshape(comp.shape).astype(np.int32)
    comp[~sea] = 0
    # np.unique above makes label of 0 dense=0 only if 0 present; it is (land)

    next_id = max(int(k) for k in provinces) + 1
    comp_to_id, comp_source = assign_ids(comp, old_ids, sea_ids, next_id)

    lookup = np.zeros(comp.max() + 1, np.int64)
    for c, pid in comp_to_id.items():
        lookup[c] = pid
    new_ids = np.where(sea, lookup[comp], old_ids)

    # preview composite
    border = np.zeros((h, w), bool)
    border[:, :-1] |= comp[:, :-1] != comp[:, 1:]
    border[:-1, :] |= comp[:-1, :] != comp[1:, :]
    border &= sea
    preview = np.zeros((h, w, 3), np.uint8)
    preview[contours & sea] = (0, 0, 255)
    preview[tectonic] = (255, 85, 85)
    preview[~sea] = (40, 40, 40)
    preview[border] = (255, 255, 255)
    Image.fromarray(preview).save(ROOT / "voronoi_contour_modernday.png")

    used_old = {pid for pid in comp_to_id.values() if pid in sea_ids}
    removed = sorted(sea_ids - used_old)
    fresh = sorted(pid for pid in comp_to_id.values() if pid not in sea_ids)
    print(
        f"sea provinces: old {len(sea_ids)}, new {len(comp_to_id)} "
        f"(reused {len(used_old)}, fresh {len(fresh)}, removed {len(removed)})"
    )
    print("removed ids:", removed)

    if args.dry_run:
        return

    new_provinces = {k: v for k, v in provinces.items() if int(k) not in sea_ids}
    for c, pid in comp_to_id.items():
        if pid in sea_ids:
            new_provinces[str(pid)] = provinces[str(pid)]
        else:
            entry = copy.deepcopy(provinces[str(comp_source[c])])
            new_provinces[str(pid)] = entry
    save_ids(new_ids, regions_path)
    provinces_path.write_text(
        json.dumps(new_provinces, indent="\t", sort_keys=True, ensure_ascii=False)
    )
    print(f"wrote {regions_path} and {provinces_path}")


if __name__ == "__main__":
    main()
