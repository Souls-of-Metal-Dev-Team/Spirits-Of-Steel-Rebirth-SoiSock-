# gradient

Utilities for turning `height.png` into map-generation inputs.

## Gradient

Encodes the wrapped x/y height derivative in the red/green channels and
quantized contour lines in the blue channel of `gradient.png`.

```sh
uv run gradient
```

## Voronoi overlay

Flood fills the regions between contour lines, places one jittered-grid seed
per region and cell, and draws 1px Voronoi cell outlines into a transparent
`voronoi.png`. Each pixel takes its nearest seed in the same region, so lines
stay continuous and stop at contours. Longitude wraps at the image seam.

```sh
uv run gradient-voronoi --cell-size 64 --seed 0 --color white --composite contour_voronoi.png
```

`--composite` writes a black image with blue contours and colored Voronoi lines.
Options: `--input`, `--output`, `--cell-size`, `--seed`, `--color`, `--composite`.

## Tectonic boundaries

Builds a composite in layer order: contours, tectonic boundaries, land-sea
lines, Voronoi borders. Contours use interval 64 in the ocean and 128 on land
(`--sea-contour-interval`, `--land-contour-interval`), with interval 16
(`--inland-contour-interval`) added in the Black, Baltic, Red Sea and Persian
Gulf boxes. The Mediterranean uses interval 32
(`--mediterranean-contour-interval`). Land and sea come from Natural Earth 50m
land polygons (`--land-data`); the coastline is their pixel boundary
(`--coast-color`). Voronoi
cells (`--cell-size`, default 32) stop at contours, tectonic and land-sea lines.
Voronoi borders touch those barriers without covering them. No cells or lines are
drawn on land.
Bird's PB2002 plate boundaries come from `fraxen/tectonicplates`. Gall uses
`height_modernday.png`; equirectangular uses `height.png`.

```sh
uv run gradient-tectonic --projection gall
uv run gradient-tectonic --projection equirect --lon-left -180
```

Options: `--output`, `--data`, `--land-data`, `--sea-contour-interval`,
`--inland-contour-interval`, `--mediterranean-contour-interval`,
`--land-contour-interval`, `--cell-size`, `--seed`, `--color`, `--coast-color`,
`--width`, `--projection`, `--lat-top`, `--lat-bottom`, `--lon-left`,
`--lon-span`.

## Sea provinces (ModernDay)

`starts/ModernDay/regions.png` is a Gall stereographic projection (fitted lat 91.22..-92.29,
lon -169.48..188.5, about 96.8% coast agreement at mid latitudes); `height.png` is equirectangular. `gradient-sea` reprojects the
height map onto the regions grid, draws contours, places Voronoi seeds in every
contour-bounded sea region, and rewrites sea (type 0) pixels in `regions.png`
and entries in `provinces.json`. Land is untouched. New cells reuse the old sea
province ID with the largest overlap; others get fresh IDs. Rerunning on its own
output re-splits the already split sea, so restore both files from git first.

```sh
uv run gradient-sea --cell-size 64 --min-pixels 1500 --dry-run
```

Tectonic mode accepts `--mediterranean-contour-interval` too. Writes
`height_modernday.png` and `voronoi_contour_modernday.png` for review.
