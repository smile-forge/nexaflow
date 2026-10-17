"""The sample pages the reference is made from, all from Unlimited-OCR's own paper (MIT, in the makers' repository):

    paper-p1, paper-p3, paper-p5   three of its pages at 200 dpi — a title page with a figure, a page of prose and maths,
                                   a page with figures and their captions
    small-print                    its text from pages 3 to 9 set on one page in small print: a page long enough that the
                                   model writes past its 128-token window many times over
    two-columns                    its text from pages 10 to 12 in two columns, scanned: a page read across its columns
    no-text                        shapes and gradients with no writing, for the text finder

    make_samples.py <Unlimited-OCR.pdf> <out folder> <a TrueType font>"""
import io
import random
import sys
from pathlib import Path

import pymupdf
from PIL import Image, ImageDraw, ImageFilter, ImageFont

pdf, out, font_path = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
out.mkdir(parents=True, exist_ok=True)
doc = pymupdf.open(str(pdf))
for number in (1, 3, 5):
    doc[number - 1].get_pixmap(matrix=pymupdf.Matrix(200 / 72, 200 / 72)).save(str(out / f"paper-p{number}.png"))


def words(first, last):
    return " ".join(" ".join(doc[i - 1].get_text().split()) for i in range(first, last + 1)).split()


def wrap(draw, ws, font, width):
    lines, line = [], ""
    for word in ws:
        trial = f"{line} {word}".strip()
        if draw.textlength(trial, font=font) <= width:
            line = trial
        else:
            lines.append(line)
            line = word
    return lines + ([line] if line else [])


page = Image.new("RGB", (2480, 3508), "white")
draw = ImageDraw.Draw(page)
font = ImageFont.truetype(font_path, 30)
y = 140
for line in wrap(draw, words(3, 9), font, 2200)[:88]:
    draw.text((140, y), line, fill="black", font=font)
    y += 37
page.save(out / "small-print.png")

random.seed(7)
page = Image.new("RGB", (1700, 2200), "white")
draw = ImageDraw.Draw(page)
font = ImageFont.truetype(font_path, 26)
lines = wrap(draw, words(10, 12), font, 700)
for column, x in ((lines[:62], 100), (lines[62:124], 900)):
    y = 120
    for line in column:
        draw.text((x, y), line, fill="black", font=font)
        y += 32
page = page.rotate(0.6, resample=Image.BICUBIC, fillcolor=(250, 250, 248)).filter(ImageFilter.GaussianBlur(0.6))
pixels = page.load()
for _ in range(page.width * page.height // 60):
    px, py = random.randrange(page.width), random.randrange(page.height)
    v = max(0, min(255, pixels[px, py][0] + random.randint(-30, 30)))
    pixels[px, py] = (v, v, v)
buffer = io.BytesIO()
page.convert("L").save(buffer, "JPEG", quality=60)
Image.open(io.BytesIO(buffer.getvalue())).convert("RGB").save(out / "two-columns.png")

page = Image.new("RGB", (1600, 1200))
draw = ImageDraw.Draw(page)
for row in range(1200):
    draw.line([(0, row), (1600, row)], fill=(40 + row // 8, 90 + row // 12, 160 - row // 10))
for i in range(14):
    cx, cy, r = random.randrange(1600), random.randrange(1200), random.randrange(30, 220)
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(random.randrange(256), random.randrange(256), random.randrange(256)))
page.filter(ImageFilter.GaussianBlur(3)).save(out / "no-text.png")
print(sorted(p.name for p in out.iterdir()))
