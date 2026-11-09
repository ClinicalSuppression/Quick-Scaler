"""Generate QuickScaler (formerly WindowsQuickScaler) / Crispy_Bills / Dupe_Check icon options (pure Pillow).

House style (matches SystemInfo docs/icon.png):
  - full-bleed rounded-square squircle, deep navy vertical gradient
  - one thin muted periwinkle inset outline
  - bold flat off-white glyphs, soft drop shadow
  - row of small teal->green gradient tick bars along the bottom
Outputs per option: 1024px PNG + multi-size ICO. A contact-sheet.png is written too.
"""
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))
S = 1024                       # canvas
RX = 225                       # squircle radius
IX, IW = 17, 7                 # inset outline offset + width
BG_TOP, BG_BOT = (27, 30, 51), (21, 24, 41)
KNOCK = (24, 28, 48)           # "cut-out" colour
OUTLINE = (72, 78, 112)
GLYPH = (226, 232, 244)
TEAL, GREEN = (63, 220, 190), (70, 223, 156)
FOUNDRY = r"C:\Windows\Fonts"

def rr(draw, box, r, **kw):
    draw.rounded_rectangle(box, radius=r, **kw)

def bg_layer():
    """Navy vertical gradient clipped to the squircle + inset outline stroke."""
    t = np.linspace(0, 1, S, dtype=np.float32)[:, None, None]
    top = np.array(BG_TOP, np.float32); bot = np.array(BG_BOT, np.float32)
    grad = (top[None, None, :] * (1 - t) + bot[None, None, :] * t).astype(np.uint8)
    grad = np.repeat(grad, S, axis=1)
    img = Image.fromarray(grad, "RGB").convert("RGBA")
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=RX, fill=255)
    img.putalpha(mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([IX, IX, S - 1 - IX, S - 1 - IX], radius=RX - IX,
                        outline=OUTLINE, width=IW)
    return img

def tick_row():
    """Equalizer-style teal->green tick bars, centred, bottoms aligned at y=902."""
    heights = [52, 96, 132, 152, 78]
    w, gap, y_bot = 64, 34, 902
    total = len(heights) * w + (len(heights) - 1) * gap
    x = (S - total) // 2
    mask = Image.new("L", (S, S), 0)
    dm = ImageDraw.Draw(mask)
    for h in heights:
        dm.rounded_rectangle([x, y_bot - h, x + w, y_bot], radius=w // 2, fill=255)
        x += w + gap
    xs, xe = (S - total) // 2, (S + total) // 2
    row = np.linspace(0, 1, xe - xs, dtype=np.float32)[:, None]
    a = np.array(TEAL, np.float32)[None, :]; b = np.array(GREEN, np.float32)[None, :]
    grad = np.repeat((a * (1 - row) + b * row).astype(np.uint8)[None, :, :], S, axis=0)
    tick_img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    tile = Image.fromarray(grad, "RGB").convert("RGBA")
    tick_img.paste(tile, (xs, 0))
    tick_img.putalpha(mask)
    return tick_img

def double_arrow(draw, x1, y1, x2, y2, color, width, head_len=95, head_half=52):
    dx, dy = x2 - x1, y2 - y1
    length = math.hypot(dx, dy); ux, uy = dx / length, dy / length; nx, ny = -uy, ux
    draw.line([x1, y1, x2, y2], fill=color, width=width)
    for (tipx, tipy, ax_, ay_) in ((x2, y2, ux, uy), (x1, y1, -ux, -uy)):
        bx, by = tipx - ax_ * head_len, tipy - ay_ * head_len
        draw.polygon([(tipx, tipy),
                      (bx + nx * head_half, by + ny * head_half),
                      (bx - nx * head_half, by - ny * head_half)], fill=color)

def rounded_line(draw, x1, y1, x2, y2, color, width):
    draw.line([x1, y1, x2, y2], fill=color, width=width)
    r = width // 2
    draw.ellipse([x1 - r, y1 - r, x1 + r, y1 + r], fill=color)
    draw.ellipse([x2 - r, y2 - r, x2 + r, y2 + r], fill=color)

def glyph_canvas():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))

def compose(name, glyph_img):
    base = bg_layer()
    # soft drop shadow from the glyph silhouette
    alpha = glyph_img.getchannel("A").filter(ImageFilter.GaussianBlur(16))
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    black = Image.new("RGBA", (S, S), (0, 0, 0, 255))
    shadow.paste(black, (0, 9), alpha.point(lambda a: int(a * 0.45)))
    base.alpha_composite(shadow)
    base.alpha_composite(glyph_img)
    base.alpha_composite(tick_row())
    folder = os.path.join(OUT, name.split("/")[0])
    os.makedirs(folder, exist_ok=True)
    png = os.path.join(OUT, name + ".png")
    base.save(png)
    base.save(os.path.join(OUT, name + ".ico"),
              sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", png)

# ---------------- QuickScaler ----------------
# Option 1: monitor with knocked-out diagonal scaling double arrow
g = glyph_canvas(); d = ImageDraw.Draw(g)
rr(d, [232, 212, 792, 564], 40, fill=GLYPH)
d.rectangle([472, 564, 552, 616], fill=GLYPH)
rr(d, [372, 616, 652, 646], 15, fill=GLYPH)
double_arrow(d, 335, 476, 689, 272, KNOCK, 46, 80, 45)
compose("QuickScaler/option1-monitor-scale", g)

# Option 2: oversized percent sign whose slash IS a double-headed arrow
g = glyph_canvas(); d = ImageDraw.Draw(g)
d.ellipse([392 - 74, 330 - 74, 392 + 74, 330 + 74], fill=GLYPH)
d.ellipse([632 - 74, 570 - 74, 632 + 74, 570 + 74], fill=GLYPH)
double_arrow(d, 300, 612, 726, 268, GLYPH, 52, 102, 56)
compose("QuickScaler/option2-percent-arrow", g)

# Option 3: WQ monogram + small diagonal double arrow
g = glyph_canvas(); d = ImageDraw.Draw(g)
try:
    font = ImageFont.truetype(os.path.join(FOUNDRY, "ariblk.ttf"), 265)
except OSError:
    font = ImageFont.truetype(os.path.join(FOUNDRY, "arialbd.ttf"), 265)
d.text((432, 330), "WQ", font=font, fill=GLYPH, anchor="mm")
double_arrow(d, 700, 452, 862, 330, GLYPH, 30, 56, 32)
compose("QuickScaler/option3-wq-arrow", g)

# ---------------- Crispy_Bills ----------------
# Option 1: stack of three bill notes, coin knocked out of the top one
g = glyph_canvas(); d = ImageDraw.Draw(g)
for i, (dx, dy) in enumerate([(-46, 40), (0, 0), (46, -40)]):
    op = int(255 * (0.45 + 0.275 * i))
    rr(d, [272 + dx, 280 + dy, 752 + dx, 550 + dy], 30, fill=GLYPH + (op,))
d.ellipse([430, 333, 594, 497], fill=KNOCK)
d.ellipse([478, 381, 546, 449], fill=GLYPH + (230,))
compose("Crispy_Bills/option1-bill-stack", g)

# Option 2: paper receipt with teal-green check
g = glyph_canvas(); d = ImageDraw.Draw(g)
zigzag = [(672, 600), (632, 566), (592, 600), (552, 566), (512, 600),
          (472, 566), (432, 600), (392, 566), (352, 600)]
d.polygon([(352, 198), (672, 198)] + zigzag, fill=GLYPH)
rr(d, [408, 292, 616, 326], 17, fill=KNOCK)
rr(d, [408, 364, 548, 398], 17, fill=KNOCK)
rounded_line(d, 408, 470, 446, 508, TEAL, 30)
rounded_line(d, 446, 508, 516, 428, TEAL, 30)
compose("Crispy_Bills/option2-receipt-check", g)

# Option 3: calendar page with a teal-green coin
g = glyph_canvas(); d = ImageDraw.Draw(g)
rr(d, [272, 238, 752, 638], 36, fill=GLYPH)
rr(d, [372, 176, 406, 286], 17, fill=GLYPH)
rr(d, [618, 176, 652, 286], 17, fill=GLYPH)
d.rectangle([272, 330, 752, 356], fill=KNOCK)
d.ellipse([440, 410, 584, 554], fill=TEAL)
d.ellipse([466, 436, 558, 528], outline=KNOCK, width=16)
compose("Crispy_Bills/option3-calendar-coin", g)

# ---------------- Dupe_Check ----------------
# Option 1: four text lines; the duplicate bottom line is struck through in green
g = glyph_canvas(); d = ImageDraw.Draw(g)
widths = [300, 236, 268, 268]
y = 208
for w in widths:
    rr(d, [(S - w) // 2, y, (S + w) // 2, y + 52], 26, fill=GLYPH)
    y += 96
rounded_line(d, 336, 548, 688, 460, TEAL, 34)
compose("Dupe_Check/option1-duplicate-strike", g)

# Option 2: magnifying glass over lines; two identical rows highlighted teal
g = glyph_canvas(); d = ImageDraw.Draw(g)
rr(d, [252, 236, 582, 282], 23, fill=GLYPH)
rr(d, [252, 352, 492, 398], 23, fill=TEAL)
rr(d, [252, 468, 492, 514], 23, fill=TEAL)
d.ellipse([492, 264, 748, 520], fill=KNOCK + (140,), outline=GLYPH, width=44)
rounded_line(d, 712, 484, 838, 610, GLYPH, 52)
compose("Dupe_Check/option2-magnifier-dupes", g)

# Option 3: two overlapping pages, green minus badge
g = glyph_canvas(); d = ImageDraw.Draw(g)
rr(d, [308, 172, 638, 592], 30, fill=KNOCK, outline=GLYPH + (140,), width=18)
rr(d, [386, 252, 716, 672], 30, fill=GLYPH)
rr(d, [452, 438, 652, 484], 23, fill=TEAL)
compose("Dupe_Check/option3-pages-minus", g)

# ---------------- contact sheet ----------------
names = ["QuickScaler/option1-monitor-scale", "QuickScaler/option2-percent-arrow",
         "QuickScaler/option3-wq-arrow", "Crispy_Bills/option1-bill-stack",
         "Crispy_Bills/option2-receipt-check", "Crispy_Bills/option3-calendar-coin",
         "Dupe_Check/option1-duplicate-strike", "Dupe_Check/option2-magnifier-dupes",
         "Dupe_Check/option3-pages-minus"]
tile = 360
sheet = Image.new("RGB", (3 * tile + 80, 3 * tile + 80), (16, 16, 25))
for i, n in enumerate(names):
    t = Image.open(os.path.join(OUT, n + ".png")).resize((tile, tile), Image.LANCZOS)
    sheet.paste(t, (20 + (i % 3) * tile, 20 + (i // 3) * tile), t)
sheet.save(os.path.join(OUT, "contact-sheet.png"))
print("wrote contact-sheet.png")
