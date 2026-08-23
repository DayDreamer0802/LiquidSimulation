from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


HERE = Path(__file__).resolve().parent
SOURCE_SHEET = HERE / "enemy_standard_sheet_source.png"
FRAME_INPUTS = {
    1: HERE / "strict_frame_01.png",
    2: HERE / "strict_frame_02.png",
    3: HERE / "strict_frame_03.png",
    4: HERE / "strict_frame_04.png",
    5: HERE / "strict_frame_05.png",
    6: HERE / "strict_frame_06.png",
    7: HERE / "strict_frame_07.png",
    8: HERE / "strict_frame_08.png",
}


def largest_component_bbox(mask: np.ndarray) -> tuple[int, int, int, int]:
    """Return the largest 8-connected component bbox as (left, top, right, bottom)."""
    height, width = mask.shape
    seen = np.zeros((height, width), dtype=np.uint8)
    best_size = 0
    best_bbox = (0, 0, width, height)

    for y in range(height):
        for x in range(width):
            if not mask[y, x] or seen[y, x]:
                continue

            queue = deque([(x, y)])
            seen[y, x] = 1
            size = 0
            min_x = max_x = x
            min_y = max_y = y

            while queue:
                cx, cy = queue.popleft()
                size += 1
                min_x = min(min_x, cx)
                max_x = max(max_x, cx)
                min_y = min(min_y, cy)
                max_y = max(max_y, cy)

                for nx, ny in (
                    (cx - 1, cy - 1), (cx, cy - 1), (cx + 1, cy - 1),
                    (cx - 1, cy),                         (cx + 1, cy),
                    (cx - 1, cy + 1), (cx, cy + 1), (cx + 1, cy + 1),
                ):
                    if (
                        0 <= nx < width
                        and 0 <= ny < height
                        and mask[ny, nx]
                        and not seen[ny, nx]
                    ):
                        seen[ny, nx] = 1
                        queue.append((nx, ny))

            if size > best_size:
                best_size = size
                best_bbox = (min_x, min_y, max_x + 1, max_y + 1)

    return best_bbox


def extract_real_transparency(image: Image.Image) -> Image.Image:
    """Remove the baked light-gray checkerboard while retaining enclosed white armor."""
    rgb = image.convert("RGB")
    pixels = np.asarray(rgb).astype(np.int16)
    chroma = pixels.max(axis=2) - pixels.min(axis=2)

    # Checkerboard cells are bright and neutral. Colored highlights receive a score
    # of zero, so the flood fill cannot pass through the robot's dark outline.
    neutral_score = np.where(chroma <= 18, pixels.min(axis=2), 0).astype(np.uint8)
    flood = Image.fromarray(neutral_score, mode="L")
    draw = ImageDraw.Draw(flood)
    del draw
    ImageDraw.floodfill(flood, (0, 0), value=1, thresh=60)
    background = np.asarray(flood) == 1

    # Remove one edge pixel to avoid a pale fringe from the baked checkerboard,
    # then restore a softly antialiased alpha edge.
    background_img = Image.fromarray((background * 255).astype(np.uint8), mode="L")
    background_img = background_img.filter(ImageFilter.MaxFilter(3))
    alpha = Image.eval(background_img, lambda value: 255 - value)
    alpha = alpha.filter(ImageFilter.GaussianBlur(0.55))

    rgba = rgb.convert("RGBA")
    rgba.putalpha(alpha)
    return rgba


def fit_to_source_frame(redraw: Image.Image, source: Image.Image) -> Image.Image:
    source_alpha = np.asarray(source.getchannel("A")) > 24
    target_bbox = largest_component_bbox(source_alpha)

    redraw_alpha = np.asarray(redraw.getchannel("A")) > 24
    redraw_bbox = largest_component_bbox(redraw_alpha)
    redraw = redraw.crop(redraw_bbox)

    target_width = target_bbox[2] - target_bbox[0]
    target_height = target_bbox[3] - target_bbox[1]
    scale = min(target_width / redraw.width, target_height / redraw.height)
    scaled_size = (
        max(1, round(redraw.width * scale)),
        max(1, round(redraw.height * scale)),
    )
    redraw = redraw.resize(scaled_size, Image.Resampling.LANCZOS)

    target_center_x = (target_bbox[0] + target_bbox[2]) / 2
    target_center_y = (target_bbox[1] + target_bbox[3]) / 2
    left = round(target_center_x - redraw.width / 2)
    top = round(target_center_y - redraw.height / 2)

    frame = Image.new("RGBA", (480, 480), (0, 0, 0, 0))
    frame.alpha_composite(redraw, (left, top))
    return frame


def main() -> None:
    source_sheet = Image.open(SOURCE_SHEET).convert("RGBA")
    final_frames: list[Image.Image] = []

    for index in range(1, 9):
        source = source_sheet.crop(((index - 1) * 480, 0, index * 480, 480))
        redraw = extract_real_transparency(Image.open(FRAME_INPUTS[index]))
        frame = fit_to_source_frame(redraw, source)
        frame.save(HERE / f"enemy_standard_frame_{index:02d}_final_v3.png")
        final_frames.append(frame)

    sheet = Image.new("RGBA", (3840, 480), (0, 0, 0, 0))
    for index, frame in enumerate(final_frames):
        sheet.alpha_composite(frame, (index * 480, 0))

    sheet.save(HERE / "enemy_standard_sheet_redrawn_final_v3.png")

    game_sheet = Image.new("RGBA", (1024, 128), (0, 0, 0, 0))
    for index, frame in enumerate(final_frames):
        game_frame = frame.resize((128, 128), Image.Resampling.LANCZOS)
        game_sheet.alpha_composite(game_frame, (index * 128, 0))

    game_sheet.save(HERE / "enemy_standard_sheet_redrawn_game_1024x128_v3.png")


if __name__ == "__main__":
    main()
