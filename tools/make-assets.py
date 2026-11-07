"""Regenerate Nessafy's embedded assets from the sources in assets/.

Run from the repo root:  python -I tools/make-assets.py   (needs Pillow)

Reads:
  assets/brand/mark.png             the vampire-mouth mark (RGBA)
  assets/theme/lace-source.jpg      lace texture
  assets/theme/photo-source.jpg     background photo
  assets/fonts/*.woff2              Cormorant Garamond (SIL OFL), Latin subset, variable weight

Writes:
  theme/assets.css                  @font-face rules and image custom properties
  installer/build/icon.ico          installer icon
  installer/build/appicon.png       mark shown in the installer window
"""
import base64, io, os, struct
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = lambda *p: os.path.join(ROOT, "assets", *p)


def b64(data: bytes) -> str:
    return base64.b64encode(data).decode("ascii")


def encode(img: Image.Image, fmt: str, **kw) -> bytes:
    buf = io.BytesIO()
    img.save(buf, fmt, **kw)
    return buf.getvalue()


def write(path: str, text: str):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(f"  {os.path.relpath(path, ROOT)}  {len(text.encode()) / 1024:.1f} KB")


# ── Mark ──────────────────────────────────────────────────────────────────────────────
def without_outline(img: Image.Image) -> Image.Image:
    """The source has a speckled white outline around the lips. Keep only the lips and what they enclose."""
    alpha = img.getchannel("A")
    dark = ImageChops.multiply(alpha.point(lambda v: 255 if v > 128 else 0),
                               img.convert("L").point(lambda v: 255 if v < 110 else 0))
    # Flood the background in from the edges: whatever it can't reach is inside the lips.
    fill = Image.new("L", (img.width + 2, img.height + 2), 0)
    fill.paste(dark, (1, 1))
    ImageDraw.floodfill(fill, (0, 0), 128)
    outside = fill.crop((1, 1, img.width + 1, img.height + 1)).point(lambda v: 255 if v == 128 else 0)
    # Two pixels further, for specks that only touch the background at a corner.
    inside = ImageChops.invert(outside.filter(ImageFilter.MaxFilter(5)))
    out = img.copy()
    out.putalpha(ImageChops.multiply(alpha, inside))
    return out


src = without_outline(Image.open(A("brand", "mark.png")).convert("RGBA"))
art = src.crop(src.getchannel("A").getbbox())
side = int(max(art.size) * 1.06)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.paste(art, ((side - art.width) // 2, (side - art.height) // 2), art)
mark = lambda px: square.resize((px, px), Image.LANCZOS)
mark96 = "data:image/webp;base64," + b64(encode(mark(96), "WEBP", quality=92, method=6))


def ico_with_bitmaps(sizes) -> bytes:
    """Plain 32-bit bitmaps up to 64px (read by every Windows icon reader), PNG above that."""
    images = []
    for s in sizes:
        img = mark(s)
        if s > 64:
            images.append((s, encode(img, "PNG")))
            continue
        pixels = img.tobytes("raw", "BGRA")
        rows = b"".join(pixels[y * s * 4:(y + 1) * s * 4] for y in reversed(range(s)))
        mask = b"\0" * (((s + 31) // 32) * 4 * s)   # transparency comes from the alpha channel
        header = struct.pack("<IiiHHIIiiII", 40, s, s * 2, 1, 32, 0, len(rows) + len(mask), 0, 0, 0, 0)
        images.append((s, header + rows + mask))

    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for s, data in images:
        out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    return out + b"".join(data for _, data in images)


os.makedirs(os.path.join(ROOT, "installer", "build"), exist_ok=True)
with open(os.path.join(ROOT, "installer", "build", "icon.ico"), "wb") as f:
    f.write(ico_with_bitmaps([16, 20, 24, 32, 40, 48, 64, 96, 128, 256]))
mark(512).save(os.path.join(ROOT, "installer", "build", "appicon.png"), optimize=True)

# ── Lace: greyscale, darkened, mirrored into a tile that repeats without seams ─────────
lace = ImageOps.grayscale(Image.open(A("theme", "lace-source.jpg")))
lace = lace.resize((int(lace.width * 0.8), int(lace.height * 0.8)), Image.LANCZOS)
# Map the lace's own range onto near-black, so it reads through the UI without CSS opacity.
lace = ImageOps.autocontrast(lace, cutoff=1).point(lambda v: int(v * 46 / 255))
w, h = lace.size
tile = Image.new("L", (w * 2, h * 2))
tile.paste(lace, (0, 0))
tile.paste(ImageOps.mirror(lace), (w, 0))
tile.paste(ImageOps.flip(lace), (0, h))
tile.paste(ImageOps.flip(ImageOps.mirror(lace)), (w, h))
lace_bytes = encode(tile.convert("RGB"), "WEBP", quality=55, method=6)

# ── Photo: kept greyscale, gently darkened ────────────────────────────────────────────
photo = ImageOps.grayscale(Image.open(A("theme", "photo-source.jpg"))).point(lambda v: int(v * 0.92))
photo_bytes = encode(photo.convert("RGB"), "WEBP", quality=82, method=6)


# ── Fonts ─────────────────────────────────────────────────────────────────────────────
def font(name: str) -> str:
    with open(A("fonts", name), "rb") as f:
        return "data:font/woff2;base64," + b64(f.read())


roman = font("CormorantGaramond-latin.woff2")
italic = font("CormorantGaramond-Italic-latin.woff2")
latin_range = ("U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, "
               "U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD")

write(os.path.join(ROOT, "theme", "assets.css"), f"""/* Generated by tools/make-assets.py. Do not edit by hand.
   Cormorant Garamond: Copyright 2015 the Cormorant Project Authors. SIL Open Font License 1.1.
   Cormorant runs small next to Spotify's own font; size-adjust evens out the x-height. */

@font-face {{
    font-family: "Nessafy Serif";
    font-style: normal;
    font-weight: 300 700;
    font-display: swap;
    size-adjust: 114%;
    src: url("{roman}") format("woff2");
    unicode-range: {latin_range};
}}

@font-face {{
    font-family: "Nessafy Serif";
    font-style: italic;
    font-weight: 300 700;
    font-display: swap;
    size-adjust: 114%;
    src: url("{italic}") format("woff2");
    unicode-range: {latin_range};
}}

:root {{
    --nessafy-lace: url("data:image/webp;base64,{b64(lace_bytes)}");
    --nessafy-lace-size: {w * 2}px {h * 2}px;
    --nessafy-photo: url("data:image/webp;base64,{b64(photo_bytes)}");
    --nessafy-mark: url("{mark96}");
}}
""")

print(f"lace tile {tile.size} {len(lace_bytes) / 1024:.1f} KB, photo {photo.size} {len(photo_bytes) / 1024:.1f} KB")
