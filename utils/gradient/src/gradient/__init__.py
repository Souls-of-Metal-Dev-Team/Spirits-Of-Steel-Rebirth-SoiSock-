"""Compute a gradient image from a grayscale height map using only PIL.

The red channel holds the change in x (horizontal derivative) and the green
channel holds the change in y (vertical derivative). Only x wraps, since
longitude is cyclic but latitude is not. At the poles, y clamps to the edge
row. Values are normalized so the steepest slope in the image maps to 255
and zero change maps to 0. The blue channel holds continuous contour lines
drawn on quantized height levels; x wraps, so lines stay joined at the seam.
"""

from pathlib import Path

from PIL import Image, ImageFilter

INPUT = Path(__file__).resolve().parents[2] / "height.png"
OUTPUT = INPUT.with_name("gradient.png")
CONTOUR_INTERVAL = 32


def gradient_image(a_heightMap: Image.Image) -> Image.Image:
    """Return an RGBA image encoding the height map's wrapped x/y gradient in R/G."""
    gray = a_heightMap.convert("L")
    width, height_px = gray.size
    gray_values = list(gray.get_flattened_data())
    pixels = [int(255 * (x / 255) ** 0.5) for x in gray_values]
    levels = [value // CONTOUR_INTERVAL for value in gray_values]

    gradients = []
    contours = []
    max_gradient = 0
    for y in range(height_px):
        y_up = max(0, y - 1) * width
        y_down = min(height_px - 1, y + 1) * width
        row = y * width
        for x in range(width):
            x_left = (x - 1) % width
            x_right = (x + 1) % width

            dx = abs(pixels[row + x_right] - pixels[row + x_left]) << 1
            dy = abs(pixels[y_down + x] - pixels[y_up + x]) << 1
            gradients.append((dx, dy))
            max_gradient = max(max_gradient, dx, dy)

            i = row + x
            diagonal = y_down + x_right
            contour = (
                levels[i] != levels[row + x_right]
                or levels[i] != levels[y_down + x]
                or levels[i] != levels[diagonal]
            )
            contours.append(255 if contour else 0)

    scale = 255 / max_gradient if max_gradient else 0
    result = [
        (
            0,
            0,
            contours[i],
            255,
        )
        for i, (dx, dy) in enumerate(gradients)
    ]

    image = Image.new("RGBA", (width, height_px))
    image.putdata(result)
    return image


def main() -> None:
    with Image.open(INPUT) as heightMap:
        result = gradient_image(heightMap.filter(ImageFilter.GaussianBlur(1)))
    result.save(OUTPUT)
    print(f"Saved {OUTPUT} ({result.size[0]}x{result.size[1]}, {result.mode})")


if __name__ == "__main__":
    main()
