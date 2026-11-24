"""Fill contour regions with Voronoi cell outlines.

The blue channel of ``gradient.png`` holds contour lines. Flood fill splits the
rest of the image into regions at those lines. Each region then gets one jittered
grid seed per grid cell. Each pixel gets its nearest seed in the same region;
borders between neighbouring owners become 1px lines. Written to a transparent
PNG overlay.
"""

from __future__ import annotations

import argparse
import random
from array import array
from collections import defaultdict, deque
from pathlib import Path

from PIL import Image, ImageColor

INPUT = Path(__file__).resolve().parents[2] / "gradient.png"
OUTPUT = INPUT.with_name("voronoi.png")
CONTOUR_THRESHOLD = 128
DEFAULT_CELL_SIZE = 64


def contour_regions(a_contours: Image.Image) -> tuple[array[int], int]:
    """Flood fill non-contour pixels; return a label per pixel and region count.

    Label 0 means contour. The x axis wraps, matching the height map gradient.
    """
    width, height_px = a_contours.size
    mask = a_contours.tobytes()
    labels = array("i", [-1]) * (width * height_px)
    left = [(x - 1) % width for x in range(width)]
    right = [(x + 1) % width for x in range(width)]

    queue: deque[int] = deque()
    region_count = 0
    for start in range(width * height_px):
        if labels[start] != -1:
            continue
        if mask[start] > CONTOUR_THRESHOLD:
            labels[start] = 0
            continue

        region_count += 1
        labels[start] = region_count
        queue.append(start)
        while queue:
            index = queue.popleft()
            row = index - index % width
            x = index - row
            neighbors = (
                row + left[x],
                row + right[x],
                index - width if row else -1,
                index + width if row + width < width * height_px else -1,
            )
            for neighbor in neighbors:
                if (
                    neighbor >= 0
                    and labels[neighbor] == -1
                    and mask[neighbor] <= CONTOUR_THRESHOLD
                ):
                    labels[neighbor] = region_count
                    queue.append(neighbor)

    return labels, region_count


def jittered_seeds(
    a_labels: array[int],
    a_width: int,
    a_height: int,
    a_cell_size: int,
    a_rng: random.Random,
) -> dict[int, list[tuple[float, float]]]:
    """Pick one seed per (region, grid cell), two pixels away from contours."""
    picks: dict[tuple[int, int, int], tuple[float, float]] = {}
    counts: dict[tuple[int, int, int], int] = {}
    left = [(x - 1) % a_width for x in range(a_width)]
    right = [(x + 1) % a_width for x in range(a_width)]

    for index, label in enumerate(a_labels):
        if label <= 0:
            continue
        row = index - index % a_width
        x = index - row
        y = row // a_width
        if (
            a_labels[row + left[x]] <= 0
            or a_labels[row + right[x]] <= 0
            or (y > 0 and a_labels[index - a_width] <= 0)
            or (y + 1 < a_height and a_labels[index + a_width] <= 0)
        ):
            continue

        key = (label, x // a_cell_size, y // a_cell_size)
        count = counts.get(key, 0) + 1
        counts[key] = count
        point = (x + a_rng.random(), y + a_rng.random())
        if count == 1 or a_rng.randrange(count) == 0:
            picks[key] = point

    seeds: dict[int, list[tuple[float, float]]] = defaultdict(list)
    for (label, _, _), point in picks.items():
        seeds[label].append(point)
    return seeds


def voronoi_alpha(
    a_labels: array[int],
    a_seeds: dict[int, list[tuple[float, float]]],
    a_width: int,
    a_height: int,
    a_cell_size: int,
) -> Image.Image:
    """Return 1px cell borders as an L-mode mask.

    Nearest-seed search uses only seeds of the pixel's region, so borders stop
    at contours. Borders come from comparing adjacent owner pixels, so they
    stay continuous instead of relying on clipped polygon edges.
    """
    columns = -(-a_width // a_cell_size)
    rows = -(-a_height // a_cell_size)
    search_radius = 3

    cell_grid: dict[tuple[int, int], list[tuple[int, int, float, float]]] = defaultdict(list)
    seed_id = 0
    for region, points in a_seeds.items():
        for x, y in points:
            cell_grid[(int(x) // a_cell_size, int(y) // a_cell_size)].append(
                (seed_id, region, x, y)
            )
            seed_id += 1

    candidates: dict[tuple[int, int], dict[int, list[tuple[int, float, float]]]] = {}
    for cy in range(rows):
        for cx in range(columns):
            center_x = cx * a_cell_size + a_cell_size / 2
            by_region: dict[int, list[tuple[int, float, float]]] = defaultdict(list)
            for nx in range(max(0, cx - search_radius), min(columns, cx + search_radius + 1)):
                for ny in range(max(0, cy - search_radius), min(rows, cy + search_radius + 1)):
                    for seed_id, region, sx, sy in cell_grid.get((nx, ny), ()):
                        wrapped_x = sx + a_width * round((center_x - sx) / a_width)
                        by_region[region].append((seed_id, wrapped_x, sy))
            candidates[(cx, cy)] = by_region

    owners = array("i", [-1]) * (a_width * a_height)
    for cy in range(rows):
        for cx in range(columns):
            region_seeds = candidates[(cx, cy)]
            x_start = cx * a_cell_size
            x_end = min(x_start + a_cell_size, a_width)
            y_start = cy * a_cell_size
            y_end = min(y_start + a_cell_size, a_height)
            for y in range(y_start, y_end):
                row = y * a_width
                for x in range(x_start, x_end):
                    index = row + x
                    region = a_labels[index]
                    if region <= 0:
                        continue
                    group = region_seeds.get(region)
                    if not group:
                        continue
                    best_id = -1
                    best_distance = 0.0
                    for seed_id, sx, sy in group:
                        distance = (sx - x) ** 2 + (sy - y) ** 2
                        if best_id < 0 or distance < best_distance:
                            best_id = seed_id
                            best_distance = distance
                    owners[index] = best_id

    alpha = bytearray(a_width * a_height)
    for y in range(a_height):
        row = y * a_width
        for x in range(a_width):
            index = row + x
            owner = owners[index]
            if owner < 0:
                continue
            right = owners[row + ((x + 1) % a_width)]
            down = owners[row + a_width + x] if y + 1 < a_height else -1
            if (right >= 0 and right != owner) or (down >= 0 and down != owner):
                alpha[index] = 255

    return Image.frombytes("L", (a_width, a_height), bytes(alpha))


def voronoi_overlay(a_alpha: Image.Image, a_color: tuple[int, int, int]) -> Image.Image:
    """Return a transparent overlay from the clipped line alpha."""
    overlay = Image.new("RGBA", a_alpha.size, a_color + (0,))
    overlay.putalpha(a_alpha)
    return overlay


def contour_composite(
    a_contours: Image.Image,
    a_alpha: Image.Image,
    a_color: tuple[int, int, int],
) -> Image.Image:
    """Return an RGBA image with blue contours and colored Voronoi lines."""
    contour_alpha = a_contours.point(
        lambda value: 255 if value > CONTOUR_THRESHOLD else 0
    )
    background = Image.new("RGBA", a_contours.size, (0, 0, 0, 255))
    blue = Image.new("RGBA", a_contours.size, (0, 0, 255, 255))
    base = Image.composite(blue, background, contour_alpha)
    return Image.alpha_composite(base, voronoi_overlay(a_alpha, a_color))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=INPUT)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--cell-size", type=int, default=DEFAULT_CELL_SIZE)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--color", default="white")
    parser.add_argument(
        "--composite", type=Path, help="also save a contour + Voronoi image here"
    )
    args = parser.parse_args()

    if args.cell_size < 2:
        parser.error("--cell-size must be at least 2")

    color = ImageColor.getrgb(args.color)
    with Image.open(args.input) as image:
        contours = image.getchannel("B")
        labels, region_count = contour_regions(contours)
        seeds = jittered_seeds(
            labels, *contours.size, args.cell_size, random.Random(args.seed)
        )
        alpha = voronoi_alpha(labels, seeds, *contours.size, args.cell_size)
        overlay = voronoi_overlay(alpha, color)
        composite = (
            contour_composite(contours, alpha, color) if args.composite else None
        )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    overlay.save(args.output)
    seed_count = sum(len(group) for group in seeds.values())
    print(
        f"Saved {args.output} ({overlay.size[0]}x{overlay.size[1]}): {region_count} regions, {seed_count} seeds"
    )
    if composite is not None and args.composite is not None:
        args.composite.parent.mkdir(parents=True, exist_ok=True)
        composite.save(args.composite)
        print(f"Saved {args.composite}")


if __name__ == "__main__":
    main()
