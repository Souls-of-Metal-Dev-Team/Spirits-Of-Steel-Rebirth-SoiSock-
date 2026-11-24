"""Build map image in layers: contours, tectonic boundaries, coastline, Voronoi.

Land and sea come from Natural Earth 50m land polygons, rasterized in the map
projection. Boundaries come from Bird's PB2002 plate boundaries (GeoJSON from
fraxen/tectonicplates, https://github.com/fraxen/tectonicplates). Contours come
from the height map: smaller interval in the ocean, larger interval on land.
Voronoi cells are flood-filled between contours, tectonic lines and coastline.
"""

from __future__ import annotations

import argparse
import json
import math
from collections.abc import Callable
from pathlib import Path

import numpy as np
from PIL import Image, ImageColor, ImageDraw
from scipy import ndimage

from . import sea_provinces

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "data" / "PB2002_boundaries.json"
LAND_DATA = ROOT / "data" / "ne_50m_land.geojson"
HEIGHT = ROOT / "height.png"
HEIGHT_GALL = ROOT / "height_modernday.png"
OUTPUT_GALL = ROOT / "voronoi_contour_modernday_tectonic.png"
OUTPUT_EQUIRECT = ROOT / "contour_voronoi_tectonic.png"
GALL_K = 1 + math.sqrt(2) / 2
# (lat min, lat max, lon min, lon max) for enclosed seas needing finer contours.
MEDITERRANEAN = (30.0, 46.0, -6.0, 37.0)
OTHER_INLAND_SEAS = (
    (40.0, 47.0, 27.0, 42.0),  # Black Sea
    (53.0, 66.0, 10.0, 31.0),  # Baltic Sea
    (12.0, 30.0, 32.0, 44.0),  # Red Sea
    (23.0, 31.0, 47.0, 57.0),  # Persian Gulf
)
INLAND_SEAS = (MEDITERRANEAN, *OTHER_INLAND_SEAS)
BLUE = (0, 0, 255)
WHITE = (255, 255, 255)
LAND = (40, 40, 40)


def make_projector(
    a_projection: str,
    a_size: tuple[int, int],
    a_lat_top: float,
    a_lat_bottom: float,
    a_lon_left: float,
    a_lon_span: float,
) -> Callable[[float, float], tuple[float, float]]:
    """Return a lon/lat -> pixel mapping matching the map image grid."""
    width, height = a_size
    if a_projection == "gall":
        y_top = GALL_K * math.tan(math.radians(a_lat_top) / 2)
        y_bottom = GALL_K * math.tan(math.radians(a_lat_bottom) / 2)

        def project(lon: float, lat: float) -> tuple[float, float]:
            y_geo = GALL_K * math.tan(math.radians(lat) / 2)
            y = (y_geo - y_top) / (y_bottom - y_top) * height - 0.5
            return (lon - a_lon_left) / a_lon_span * width - 0.5, y

    else:

        def project(lon: float, lat: float) -> tuple[float, float]:
            y = (a_lat_top - lat) / (a_lat_top - a_lat_bottom) * height - 0.5
            return (lon - a_lon_left) / a_lon_span * width - 0.5, y

    return project


def geojson_lines(a_data: Path) -> list[list[tuple[float, float]]]:
    """Read LineString and MultiLineString coordinates from a GeoJSON."""
    features = json.loads(a_data.read_text())["features"]
    lines: list[list[tuple[float, float]]] = []
    for feature in features:
        geometry = feature["geometry"]
        if geometry["type"] == "LineString":
            groups = [geometry["coordinates"]]
        elif geometry["type"] == "MultiLineString":
            groups = geometry["coordinates"]
        else:
            continue
        for group in groups:
            lines.append([(float(lon), float(lat)) for lon, lat in group])
    return lines


def draw_boundaries(
    a_draw: ImageDraw.ImageDraw,
    a_lines: list[list[tuple[float, float]]],
    a_project: Callable[[float, float], tuple[float, float]],
    a_image_width: int,
    a_width_px: int,
    a_fill: int,
) -> int:
    """Draw every line into an L mask; return segments rasterized."""
    segments = 0
    for line in a_lines:
        unwrapped: list[tuple[float, float]] = []
        previous: float | None = None
        for lon, lat in line:
            if previous is not None:
                lon += 360 * round((previous - lon) / 360)
            previous = lon
            unwrapped.append((lon, lat))

        points = [a_project(lon, lat) for lon, lat in unwrapped]
        for start, end in zip(points, points[1:]):
            for shift in (-a_image_width, 0, a_image_width):
                a_draw.line(
                    [(start[0] + shift, start[1]), (end[0] + shift, end[1])],
                    fill=a_fill,
                    width=a_width_px,
                )
            segments += 1
    return segments


def raster_lines(
    a_size: tuple[int, int],
    a_lines: list[list[tuple[float, float]]],
    a_project: Callable[[float, float], tuple[float, float]],
    a_width_px: int,
) -> np.ndarray:
    """Rasterize lon/lat lines as a boolean mask."""
    mask = Image.new("L", a_size, 0)
    draw_boundaries(ImageDraw.Draw(mask), a_lines, a_project, a_size[0], a_width_px, 255)
    return np.asarray(mask) > 0


def land_mask(
    a_size: tuple[int, int],
    a_data: Path,
    a_project: Callable[[float, float], tuple[float, float]],
) -> np.ndarray:
    """Rasterize Natural Earth land polygons (exterior rings, then holes)."""
    features = json.loads(a_data.read_text())["features"]
    image = Image.new("L", a_size, 0)
    draw = ImageDraw.Draw(image)
    width = a_size[0]
    for feature in features:
        geometry = feature["geometry"]
        if geometry["type"] == "Polygon":
            polygons = [geometry["coordinates"]]
        elif geometry["type"] == "MultiPolygon":
            polygons = geometry["coordinates"]
        else:
            continue
        for polygon in polygons:
            for ring_index, ring in enumerate(polygon):
                points = [a_project(lon, lat) for lon, lat in ring]
                unwrapped: list[tuple[float, float]] = []
                previous: float | None = None
                for x, y in points:
                    if previous is not None:
                        x += width * round((previous - x) / width)
                    previous = x
                    unwrapped.append((x, y))
                for shift in (-width, 0, width):
                    draw.polygon(
                        [(x + shift, y) for x, y in unwrapped],
                        fill=255 if ring_index == 0 else 0,
                    )
    return np.asarray(image) > 0


def coastline(a_land: np.ndarray, a_wrap_x: bool) -> np.ndarray:
    """Pixel boundary of the land mask."""
    line = np.zeros(a_land.shape, bool)
    line[:, :-1] |= a_land[:, :-1] != a_land[:, 1:]
    line[:-1, :] |= a_land[:-1, :] != a_land[1:, :]
    if a_wrap_x:
        line[:, -1] |= a_land[:, -1] != a_land[:, 0]
    return line


def mixed_contours(
    a_height: np.ndarray,
    a_sea: np.ndarray,
    a_sea_interval: int,
    a_land_interval: int,
    a_wrap_x: bool,
) -> np.ndarray:
    """Quantize height with a finer interval in the ocean, then find level changes."""
    levels = np.where(
        a_sea, a_height // a_sea_interval, a_height // a_land_interval
    ).astype(np.int32)
    mask = np.zeros(levels.shape, bool)
    mask[:, :-1] |= levels[:, :-1] != levels[:, 1:]
    mask[:-1, :] |= levels[:-1, :] != levels[1:, :]
    mask[:-1, :-1] |= levels[:-1, :-1] != levels[1:, 1:]
    if a_wrap_x:
        mask[:, -1] |= levels[:, -1] != levels[:, 0]
    return mask


def inland_mask(
    a_size: tuple[int, int],
    a_projection: str,
    a_lat_top: float,
    a_lat_bottom: float,
    a_lon_left: float,
    a_lon_span: float,
    a_boxes=INLAND_SEAS,
) -> np.ndarray:
    """Pixel mask for enclosed seas given as lat/lon boxes."""
    height, width = a_size
    lon = a_lon_left + a_lon_span * (np.arange(width) + 0.5) / width
    if a_projection == "gall":
        y_top = GALL_K * math.tan(math.radians(a_lat_top) / 2)
        y_bottom = GALL_K * math.tan(math.radians(a_lat_bottom) / 2)
        fraction = (np.arange(height) + 0.5) / height
        y_geo = y_top + (y_bottom - y_top) * fraction
        lat = np.degrees(2 * np.arctan(y_geo / GALL_K))
    else:
        lat = a_lat_top + (a_lat_bottom - a_lat_top) * (
            np.arange(height) + 0.5
        ) / height

    mask = np.zeros((height, width), bool)
    rows = lat[:, None]
    columns = lon[None, :]
    for lat_min, lat_max, lon_min, lon_max in a_boxes:
        mask |= (
            (rows >= lat_min)
            & (rows <= lat_max)
            & (columns >= lon_min)
            & (columns <= lon_max)
        )
    return mask


def voronoi_borders(
    a_valid: np.ndarray,
    a_barriers: np.ndarray,
    a_cell_size: int,
    a_seed: int,
) -> np.ndarray:
    """Draw 1px borders between cells, touching barriers without crossing them."""
    labels, _ = ndimage.label(a_valid & ~a_barriers)
    rng = np.random.default_rng(a_seed)
    seeds, seed_region = sea_provinces.jittered_seeds(labels, a_cell_size, rng)
    owners = sea_provinces.voronoi_owners(labels, seeds, seed_region)

    def different_owners(
        a_label_a: np.ndarray,
        a_owner_a: np.ndarray,
        a_label_b: np.ndarray,
        a_owner_b: np.ndarray,
    ) -> np.ndarray:
        return (
            (a_label_a > 0)
            & (a_label_a == a_label_b)
            & (a_owner_a >= 0)
            & (a_owner_b >= 0)
            & (a_owner_a != a_owner_b)
        )

    border = np.zeros(owners.shape, bool)
    border[:, :-1] |= different_owners(
        labels[:, :-1], owners[:, :-1], labels[:, 1:], owners[:, 1:]
    )
    border[:-1, :] |= different_owners(
        labels[:-1, :], owners[:-1, :], labels[1:, :], owners[1:, :]
    )
    return border


def layered_image(
    a_size: tuple[int, int],
    a_contours: np.ndarray,
    a_borders: np.ndarray,
    a_tectonic: np.ndarray,
    a_coast: np.ndarray,
    a_sea: np.ndarray,
    a_color: tuple[int, int, int],
    a_coast_color: tuple[int, int, int],
    a_show_land: bool,
) -> Image.Image:
    """Composite in order: base, contours, tectonic, coast, Voronoi borders."""
    height, width = a_size
    rgb = np.zeros((height, width, 3), np.uint8)
    if a_show_land:
        rgb[~a_sea] = LAND
    rgb[a_contours] = BLUE
    rgb[a_tectonic] = a_color
    rgb[a_coast] = a_coast_color
    rgb[a_borders] = WHITE
    rgba = np.empty((height, width, 4), np.uint8)
    rgba[..., :3] = rgb
    rgba[..., 3] = 255
    return Image.fromarray(rgba, "RGBA")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--projection", choices=("gall", "equirect"), default="gall")
    p.add_argument("--output", type=Path, help="default: projection-specific file")
    p.add_argument("--data", type=Path, default=DATA)
    p.add_argument("--land-data", type=Path, default=LAND_DATA)
    p.add_argument("--sea-contour-interval", type=int, default=64)
    p.add_argument("--inland-contour-interval", type=int, default=16)
    p.add_argument("--mediterranean-contour-interval", type=int, default=32)
    p.add_argument("--land-contour-interval", type=int, default=128)
    p.add_argument("--cell-size", type=int, default=32)
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--color", default="#ff5555")
    p.add_argument("--coast-color", default="#ffff00")
    p.add_argument("--width", type=int, default=1)
    p.add_argument("--lat-top", type=float, default=90.0)
    p.add_argument("--lat-bottom", type=float, default=-90.0)
    p.add_argument("--lon-left", type=float, default=-170.0)
    p.add_argument("--lon-span", type=float, default=360.0)
    args = p.parse_args()

    for name in (
        "sea_contour_interval",
        "inland_contour_interval",
        "mediterranean_contour_interval",
        "land_contour_interval",
        "cell_size",
        "width",
    ):
        if getattr(args, name) < 1:
            p.error(f"--{name.replace('_', '-')} must be at least 1")

    if args.projection == "gall":
        with Image.open(HEIGHT_GALL) as image:
            height = np.asarray(image.convert("L")).astype(np.float32)
        output = args.output or OUTPUT_GALL
        show_land = True
    else:
        with Image.open(HEIGHT) as image:
            height = np.asarray(image.convert("L")).astype(np.float32)
        height = ndimage.gaussian_filter(height, 1)
        output = args.output or OUTPUT_EQUIRECT
        show_land = False

    size = (height.shape[1], height.shape[0])
    project = make_projector(
        args.projection,
        size,
        args.lat_top,
        args.lat_bottom,
        args.lon_left,
        args.lon_span,
    )
    land = land_mask(size, args.land_data, project)
    sea = ~land
    coast = coastline(land, args.projection == "equirect")

    def contour_layer(a_interval: int) -> np.ndarray:
        if args.projection == "gall":
            return sea_provinces.contour_mask(height, a_interval)
        return mixed_contours(
            height,
            sea,
            a_interval,
            args.land_contour_interval,
            True,
        )

    contours = contour_layer(args.sea_contour_interval) & sea

    inland = (
        inland_mask(
            height.shape,
            args.projection,
            args.lat_top,
            args.lat_bottom,
            args.lon_left,
            args.lon_span,
            OTHER_INLAND_SEAS,
        )
        & sea
    )
    mediterranean = (
        inland_mask(
            height.shape,
            args.projection,
            args.lat_top,
            args.lat_bottom,
            args.lon_left,
            args.lon_span,
            (MEDITERRANEAN,),
        )
        & sea
    )
    contours |= contour_layer(args.inland_contour_interval) & inland
    contours |= contour_layer(args.mediterranean_contour_interval) & mediterranean

    tectonic = raster_lines(size, geojson_lines(args.data), project, args.width)
    barriers = contours | tectonic | coast
    valid = sea & ~barriers
    borders = voronoi_borders(valid, barriers, args.cell_size, args.seed)

    result = layered_image(
        height.shape,
        contours,
        borders,
        tectonic,
        coast,
        sea,
        ImageColor.getrgb(args.color),
        ImageColor.getrgb(args.coast_color),
        show_land,
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    result.save(output)
    print(
        f"Saved {output} ({size[0]}x{size[1]}): "
        f"{int(contours.sum())} contour px, {int(tectonic.sum())} tectonic px, "
        f"{int(coast.sum())} coast px, {int(borders.sum())} Voronoi px"
    )


if __name__ == "__main__":
    main()
