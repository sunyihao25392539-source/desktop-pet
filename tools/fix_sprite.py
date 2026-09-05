from collections import deque
from pathlib import Path

from PIL import Image


root = Path(__file__).resolve().parents[1]
source = root / "assets" / "sprites" / "girlfriend_cherry_front.png"
backup = root / "assets" / "sprites" / "girlfriend_cherry_front_before_white_fix.png"

image = Image.open(source).convert("RGBA")
if not backup.exists():
    image.save(backup)

pixels = image.load()
width, height = image.size
outside = set()
queue = deque()

for x in range(width):
    queue.append((x, 0))
    queue.append((x, height - 1))
for y in range(height):
    queue.append((0, y))
    queue.append((width - 1, y))

while queue:
    x, y = queue.popleft()
    if (x, y) in outside or pixels[x, y][3] >= 32:
        continue
    outside.add((x, y))
    if x:
        queue.append((x - 1, y))
    if x + 1 < width:
        queue.append((x + 1, y))
    if y:
        queue.append((x, y - 1))
    if y + 1 < height:
        queue.append((x, y + 1))

for y in range(height):
    for x in range(width):
        if pixels[x, y][3] < 32 and (x, y) not in outside:
            pixels[x, y] = (255, 250, 241, 255)

target_height = 220
target_width = round(width * target_height / height)
image = image.resize((target_width, target_height), Image.Resampling.NEAREST)
image.save(source)
print(f"saved {source} at {image.size[0]}x{image.size[1]}")
