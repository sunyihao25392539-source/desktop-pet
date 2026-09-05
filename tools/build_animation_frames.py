from pathlib import Path

from PIL import Image, ImageDraw


root = Path(__file__).resolve().parents[1]
source = root / "assets" / "sprites" / "girlfriend_cherry_front.png"
output = root / "assets" / "sprites" / "girlfriend_cherry_v2"
output.mkdir(parents=True, exist_ok=True)

base = Image.open(source).convert("RGBA")
target_height = 180
target_width = round(base.width * target_height / base.height)

face = (255, 250, 241, 255)
eye = (61, 34, 15, 255)
eye_boxes = ((52, 96, 77, 116), (111, 96, 136, 116))


def clean_eyes(image: Image.Image) -> ImageDraw.ImageDraw:
    draw = ImageDraw.Draw(image)
    for box in eye_boxes:
        draw.rectangle(box, fill=face)
    return draw


def save(image: Image.Image, name: str) -> None:
    resized = image.resize((target_width, target_height), Image.Resampling.NEAREST)
    resized.save(output / name)


save(base.copy(), "idle.png")

half = base.copy()
draw = clean_eyes(half)
for left, _, right, _ in eye_boxes:
    draw.rectangle((left + 4, 103, right - 4, 109), fill=eye)
    draw.rectangle((left + 6, 102, right - 6, 102), fill=eye)
save(half, "blink_half.png")

closed = base.copy()
draw = clean_eyes(closed)
for left, _, right, _ in eye_boxes:
    draw.rectangle((left + 4, 106, right - 4, 109), fill=eye)
save(closed, "blink_closed.png")

happy = base.copy()
draw = clean_eyes(happy)
for left, _, right, _ in eye_boxes:
    center = (left + right) // 2
    draw.line((left + 4, 109, center, 104), fill=eye, width=3)
    draw.line((center, 104, right - 4, 109), fill=eye, width=3)
save(happy, "happy.png")

print(f"created 4 frames at {target_width}x{target_height} in {output}")
