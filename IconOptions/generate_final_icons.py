"""Generate the final uniform icon family (pure Pillow + numpy).

Uniform layout for every app:
  - full-bleed navy squircle, thin periwinkle inset outline
  - big off-white monogram (Arial Black) in the upper half
  - one purpose object centred in the lower half (teal->green or off-white)
  - soft drop shadow, matching SystemInfo's existing look

Outputs per app to IconOptions/Final/<App>/: 1024px PNG + multi-size ICO.
Also emits for Crispy_Bills the Android adaptive-icon vector set (appicon.svg,
appiconfg.svg) built from true Arial Black glyph outlines.
"""
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Final")
S = 1024
RX = 225
IX, IW = 17, 7
BG_TOP, BG_BOT = (27, 30, 51), (21, 24, 41)
KNOCK = (24, 28, 48)
OUTLINE = (72, 78, 112)
GLYPH = (226, 232, 244)
TEAL, GREEN = (63, 220, 190), (70, 223, 156)
FONTS = r"C:\Windows\Fonts"
ARIBLK = os.path.join(FONTS, "ariblk.ttf")

def bg_layer(rounded=True):
    t = np.linspace(0, 1, S, dtype=np.float32)[:, None, None]
    top = np.array(BG_TOP, np.float32); bot = np.array(BG_BOT, np.float32)
    grad = np.repeat((top[None, None, :] * (1 - t) + bot[None, None, :] * t).astype(np.uint8), S, axis=1)
    img = Image.fromarray(grad, "RGB").convert("RGBA")
    if rounded:
        mask = Image.new("L", (S, S), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=RX, fill=255)
        img.putalpha(mask)
        ImageDraw.Draw(img).rounded_rectangle([IX, IX, S - 1 - IX, S - 1 - IX],
                                              radius=RX - IX, outline=OUTLINE, width=IW)
    return img

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

def ticks_mask_draw(dm, y_bot=908, heights=(60, 108, 150, 168, 88), w=70, gap=38):
    total = len(heights) * w + (len(heights) - 1) * gap
    x = (S - total) // 2
    for h in heights:
        dm.rounded_rectangle([x, y_bot - h, x + w, y_bot], radius=w // 2, fill=255)
        x += w + gap
    return total

def tick_row():
    heights, w, gap, y_bot = (60, 108, 150, 168, 88), 70, 38, 908
    total = len(heights) * w + (len(heights) - 1) * gap
    mask = Image.new("L", (S, S), 0)
    ticks_mask_draw(ImageDraw.Draw(mask), y_bot, heights, w, gap)
    xs, xe = (S - total) // 2, (S + total) // 2
    row = np.linspace(0, 1, xe - xs, dtype=np.float32)[:, None]
    a = np.array(TEAL, np.float32)[None, :]; b = np.array(GREEN, np.float32)[None, :]
    grad = np.repeat((a * (1 - row) + b * row).astype(np.uint8)[None, :, :], S, axis=0)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(Image.fromarray(grad, "RGB").convert("RGBA"), (xs, 0))
    img.putalpha(mask)
    return img

def monogram(draw, text, cx, cy, size, fill=GLYPH):
    font = ImageFont.truetype(ARIBLK, size)
    draw.text((cx, cy), text, font=font, fill=fill, anchor="mm")

def compose(app, name, glyph_img, with_ticks=False):
    base = bg_layer()
    alpha = glyph_img.getchannel("A").filter(ImageFilter.GaussianBlur(16))
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    black = Image.new("RGBA", (S, S), (0, 0, 0, 255))
    shadow.paste(black, (0, 9), alpha.point(lambda a: int(a * 0.45)))
    base.alpha_composite(shadow)
    base.alpha_composite(glyph_img)
    if with_ticks:
        base.alpha_composite(tick_row())
    folder = os.path.join(OUT, app)
    os.makedirs(folder, exist_ok=True)
    png = os.path.join(folder, name + ".png")
    base.save(png)
    base.save(os.path.join(folder, name + ".ico"),
              sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", png)

def glyph_layer():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))

# ---------- SystemInfo: SI over teal->green bars ----------
g = glyph_layer(); d = ImageDraw.Draw(g)
monogram(d, "SI", 512, 360, 290)
compose("SystemInfo", "SystemInfo", g, with_ticks=True)

# ---------- QuickScaler: QS over a display with a scaling double arrow ----------
g = glyph_layer(); d = ImageDraw.Draw(g)
monogram(d, "QS", 512, 330, 265)
rr_mon = [330, 560, 694, 782]
ImageDraw.Draw(g).rounded_rectangle(rr_mon, radius=30, fill=GLYPH)
d.rectangle([480, 782, 544, 818], fill=GLYPH)
ImageDraw.Draw(g).rounded_rectangle([412, 818, 612, 840], radius=11, fill=GLYPH)
double_arrow(d, 396, 726, 628, 606, KNOCK, 32, 54, 33)
compose("QuickScaler", "QuickScaler", g)

# ---------- Crispy_Bills: CB over a banknote with a teal coin ----------
g = glyph_layer(); d = ImageDraw.Draw(g)
monogram(d, "CB", 512, 330, 265)
d.rounded_rectangle([262, 575, 762, 807], radius=30, fill=GLYPH)
d.ellipse([512 - 70, 691 - 70, 512 + 70, 691 + 70], fill=KNOCK)
d.ellipse([512 - 42, 691 - 42, 512 + 42, 691 + 42], fill=TEAL)
# small bill notch circles left/right of the coin
d.ellipse([262 - 24, 691 - 24, 262 + 24, 691 + 24], fill=KNOCK)
d.ellipse([762 - 24, 691 - 24, 762 + 24, 691 + 24], fill=KNOCK)
compose("Crispy_Bills", "Crispy_Bills", g)

# ---------- Dupe_Check: DC over duplicate lines, one struck through ----------
g = glyph_layer(); d = ImageDraw.Draw(g)
monogram(d, "DC", 512, 330, 265)
for y, w in ((560, 300), (660, 246), (760, 246)):
    d.rounded_rectangle([(S - w) // 2, y, (S + w) // 2, y + 46], radius=23, fill=GLYPH)
rounded_line(d, 392, 800, 632, 722, TEAL, 26)
compose("Dupe_Check", "Dupe_Check", g)

# ---------- glyph-only transparent splash for Crispy_Bills (Android/iOS splash) ----------
g = glyph_layer(); d = ImageDraw.Draw(g)
monogram(d, "CB", 512, 380, 330)
d.rounded_rectangle([282, 630, 742, 852], radius=34, fill=GLYPH)
d.ellipse([512 - 76, 741 - 76, 512 + 76, 741 + 76], fill=KNOCK)
d.ellipse([512 - 46, 741 - 46, 512 + 46, 741 + 46], fill=TEAL)
d.ellipse([282 - 26, 741 - 26, 282 + 26, 741 + 26], fill=KNOCK)
d.ellipse([742 - 26, 741 - 26, 742 + 26, 741 + 26], fill=KNOCK)
splash_folder = os.path.join(OUT, "Crispy_Bills")
os.makedirs(splash_folder, exist_ok=True)
g.save(os.path.join(splash_folder, "crispy_bills_glyph.png"))
print("wrote", os.path.join(splash_folder, "crispy_bills_glyph.png"))

# ========== Android adaptive icon SVGs for Crispy_Bills ==========
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen

def glyph_path_d(font, ch, matrix):
    from fontTools.pens.transformPen import TransformPen
    glyphset = font.getGlyphSet()
    cmap = font.getBestCmap()
    name = cmap[ord(ch)]
    pen = SVGPathPen(glyphset)
    glyphset[name].draw(TransformPen(pen, matrix))
    return pen.getCommands(), font["hmtx"][name][0]

font = TTFont(ARIBLK)
upem = font["head"].unitsPerEm
scale = 300.0 / upem
from fontTools.misc.transform import Transform

# preliminary pass to measure advances at the target scale
d_c, w_c = glyph_path_d(font, "C", Transform(scale, 0, 0, -scale, 0, 0))
d_b, w_b = glyph_path_d(font, "B", Transform(scale, 0, 0, -scale, 0, 0))
total = (w_c + w_b) * scale + 60
x0 = 512 - total / 2
baseline_y = 505
# bake translate+flip into the path coordinates so the SVG needs no transform attribute
d_c, _ = glyph_path_d(font, "C", Transform(scale, 0, 0, -scale, x0, baseline_y))
d_b, _ = glyph_path_d(font, "B", Transform(scale, 0, 0, -scale, x0 + w_c * scale + 60, baseline_y))
svg_letters = [
    f'<path d="{d_c}" fill="#E2E8F4"/>',
    f'<path d="{d_b}" fill="#E2E8F4"/>',
]
fg_banknote = (
    '<rect x="322" y="580" width="380" height="150" rx="22" fill="#E2E8F4"/>'
    '<circle cx="512" cy="655" r="52" fill="#181C30"/>'
    '<circle cx="512" cy="655" r="31" fill="#3FDCBE"/>'
    '<circle cx="322" cy="655" r="18" fill="#181C30"/>'
    '<circle cx="702" cy="655" r="18" fill="#181C30"/>'
)
fg = (
    '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
    '<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">\n'
    + "\n".join(svg_letters) + "\n" + fg_banknote + "\n</svg>\n"
)
bg = (
    '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
    '<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">\n'
    '<rect width="1024" height="1024" fill="#1B1E33"/>\n</svg>\n'
)
sq_folder = os.path.join(OUT, "Crispy_Bills")
for fname, content in (("appicon.svg", bg), ("appiconfg.svg", fg)):
    p = os.path.join(sq_folder, fname)
    with open(p, "w", encoding="utf-8") as f:
        f.write(content)
    print("wrote", p)

# contact sheet of the four finals
names = [("SystemInfo", "SystemInfo"), ("QuickScaler", "QuickScaler"),
         ("Crispy_Bills", "Crispy_Bills"), ("Dupe_Check", "Dupe_Check")]
tile = 400
sheet = Image.new("RGB", (2 * tile + 60, 2 * tile + 60), (16, 16, 25))
for i, (app, nm) in enumerate(names):
    t = Image.open(os.path.join(OUT, app, nm + ".png")).resize((tile, tile), Image.LANCZOS)
    sheet.paste(t, (20 + (i % 2) * tile, 20 + (i // 2) * tile), t)
sheet.save(os.path.join(OUT, "final-contact-sheet.png"))
print("wrote final-contact-sheet.png")
