"""se7en Voice — icône dans la DA « Centrale » (matériel audio + HUD).

Châssis noir arrondi avec biseau, « 7 » Michroma couleur bone avec halo blanc,
coin HUD coupé et rangée de LEDs façon vumètre (blanc, blanc, blanc, orange, rouge).

  python tools/make_icon.py <dossier de sortie>
    -> src/Se7enVoice/Assets/app.ico (16 à 256 px) et <sortie>/se7en-voice.png (256 px, pour Seventh OS)
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / 'src' / 'Se7enVoice' / 'Assets' / 'Fonts' / 'Michroma-400.ttf'

BONE = (230, 231, 235)
ORANGE = (255, 106, 61)
RED = (255, 61, 51)


def icon(size=1024, small=False):
    S = size * 2  # suréchantillonnage, réduit à la fin
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    pad, rad = S * 0.04, S * 0.2

    # Châssis : dégradé ink-200 -> noir, liseré clair en haut, bord noir
    body = Image.new('RGBA', (S, S))
    g = ImageDraw.Draw(body)
    for y in range(S):
        t = y / S
        c = tuple(int(a + (b - a) * t) for a, b in zip((34, 34, 40), (6, 6, 8)))
        g.line([(0, y), (S, y)], fill=c + (255,))
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((pad, pad, S - pad, S - pad), rad, fill=255)
    im.paste(body, (0, 0), mask)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((pad, pad, S - pad, S - pad), rad, outline=(0, 0, 0, 255), width=max(2, S // 256))
    d.rounded_rectangle((pad + S * 0.012, pad + S * 0.012, S - pad - S * 0.012, S - pad - S * 0.012), rad - S * 0.012,
                        outline=(255, 255, 255, 34), width=max(2, S // 300))

    # Coin HUD (haut gauche) : filet blanc fin à 45°
    if not small:
        w = max(2, S // 220)
        x0, y0, L = S * 0.17, S * 0.17, S * 0.09
        d.line([(x0, y0 + L), (x0, y0 + L * 0.35), (x0 + L * 0.35, y0), (x0 + L, y0)], fill=(255, 255, 255, 150), width=w, joint='curve')

    # « 7 » Michroma, épaissi, centré
    font = ImageFont.truetype(str(FONT), int(S * (0.78 if small else 0.66)))
    sw = int(S * (0.03 if small else 0.018))
    glyph = Image.new('L', (S, S), 0)
    gd = ImageDraw.Draw(glyph)
    l, t, r, b = gd.textbbox((0, 0), '7', font=font, stroke_width=sw)
    cy = S * (0.5 if small else 0.45)
    pos = ((S - (r - l)) / 2 - l, cy - (b - t) / 2 - t)
    gd.text(pos, '7', font=font, fill=255, stroke_width=sw, stroke_fill=255)
    glow = glyph.filter(ImageFilter.GaussianBlur(S * 0.03)).point(lambda v: int(v * 0.45))
    im = Image.alpha_composite(im, Image.merge('RGBA', [Image.new('L', (S, S), 255)] * 3 + [glow]))
    im = Image.alpha_composite(im, Image.merge('RGBA', [Image.new('L', (S, S), c) for c in BONE] + [glyph]))

    # Vumètre : 5 LEDs rondes, les deux dernières chaudes
    if not small:
        d = ImageDraw.Draw(im)
        r0, gap, y = S * 0.026, S * 0.085, S * 0.80
        cols = [BONE, BONE, BONE, ORANGE, RED]
        for i, col in enumerate(cols):
            cx = S / 2 + (i - 2) * gap
            if i >= 3:
                halo = Image.new('RGBA', (S, S), (0, 0, 0, 0))
                ImageDraw.Draw(halo).ellipse((cx - r0 * 2.4, y - r0 * 2.4, cx + r0 * 2.4, y + r0 * 2.4), fill=col + (110,))
                im = Image.alpha_composite(im, halo.filter(ImageFilter.GaussianBlur(r0)))
                d = ImageDraw.Draw(im)
            d.ellipse((cx - r0, y - r0, cx + r0, y + r0), fill=col + (255,))

    # Découpe finale aux bords du châssis
    out = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    out.paste(im, (0, 0), mask)
    return out.resize((size, size), Image.LANCZOS)


def main():
    out_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'dist'
    out_dir.mkdir(parents=True, exist_ok=True)
    big = icon(1024)
    big.resize((256, 256), Image.LANCZOS).save(out_dir / 'se7en-voice.png')
    big.save(out_dir / 'se7en-voice-1024.png')
    # Petites tailles : version simplifiée (7 plus gros, sans LEDs ni coin) pour rester lisible
    frames = {s: (big if s >= 48 else icon(256, small=True)).resize((s, s), Image.LANCZOS) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)}
    frames[256].save(ROOT / 'src' / 'Se7enVoice' / 'Assets' / 'app.ico', sizes=[(s, s) for s in frames],
                     append_images=[frames[s] for s in frames if s != 256])
    print('ok', out_dir)


if __name__ == '__main__':
    main()
