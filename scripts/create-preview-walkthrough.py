"""Build the README's 30-second preview from verified Windows screenshots.

Requires Pillow. Run from the repository root after capturing the three JPEGs.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
MEDIA = ROOT / "docs" / "media"
OUTPUT = MEDIA / "windows-preview-walkthrough.gif"

SLIDES = [
    ("dashboard-windows.jpg", "1 / 3   CONNECT A TARGET", 8000),
    ("core-inventory-windows.jpg", "2 / 3   CHOOSE CORES", 10000),
    ("review-changes-windows.jpg", "3 / 3   REVIEW FILE CHANGES", 12000),
]

WIDTH = 1200
HEADER = 62
FOOTER = 38
CAPTION = "Windows technical preview v0.1.6  |  Sample folder  |  No changes applied"
FONT_PATH = "C:/Windows/Fonts/segoeui.ttf"


def make_frame(filename: str, heading: str) -> Image.Image:
    screenshot = Image.open(MEDIA / filename).convert("RGB")
    height = round(screenshot.height * WIDTH / screenshot.width)
    screenshot = screenshot.resize((WIDTH, height), Image.Resampling.LANCZOS)
    frame = Image.new("RGB", (WIDTH, HEADER + height + FOOTER), "#111111")
    frame.paste(screenshot, (0, HEADER))
    draw = ImageDraw.Draw(frame)
    draw.text((24, 15), heading, fill="white", font=ImageFont.truetype(FONT_PATH, 29))
    draw.text(
        (24, HEADER + height + 9),
        CAPTION,
        fill="#dddddd",
        font=ImageFont.truetype(FONT_PATH, 17),
    )
    return frame.quantize(colors=128, method=Image.Quantize.MEDIANCUT)


frames = [make_frame(filename, heading) for filename, heading, _ in SLIDES]
durations = [duration for _, _, duration in SLIDES]
frames[0].save(
    OUTPUT,
    format="GIF",
    save_all=True,
    append_images=frames[1:],
    duration=durations,
    loop=0,
    optimize=True,
)
print(OUTPUT)
