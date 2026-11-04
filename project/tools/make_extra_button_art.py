"""Generate authored larger L1/R1, L2/R2 and PS guide DXT5 art; Pillow is developer-only.

Run from the repo root. Existing MIT PlayStation L1/R1 data supplies the bumper
style; trigger outlines and lettering below are project-authored vector paths.
No game pixels or extracted textures are read or distributed by this generator.
"""
from pathlib import Path
import io
import re
import struct
from PIL import Image, ImageDraw

TARGET = Path(__file__).resolve().parents[1] / 'src/button_prompt_art.inc'
MARKER = '// Additional authored PlayStation button sizes and triggers.'


def decode(source, name, width, height):
    body = re.search(r'constexpr uint8_t ' + name + r'\[\d+\] = \{(.*?)\};', source, re.S)[1]
    guest = bytes(int(v, 16) for v in re.findall(r'0x([0-9A-Fa-f]{2})', body))
    blocks = bytearray(guest)
    blocks[::2], blocks[1::2] = guest[1::2], guest[::2]
    header = bytearray(128)
    header[:4] = b'DDS '
    struct.pack_into('<7I', header, 4, 124, 0x81007, height, width, len(blocks), 0, 0)
    struct.pack_into('<II4s', header, 76, 32, 4, b'DXT5')
    struct.pack_into('<I', header, 108, 0x1000)
    return Image.open(io.BytesIO(header + blocks)).convert('RGBA')


def trigger(right):
    scale = 4
    image = Image.new('RGBA', (64*scale, 64*scale))
    draw = ImageDraw.Draw(image)
    points = [(13,6),(43,6),(48,12),(48,50),(42,57),(12,57),(8,51),(8,12)]
    draw.polygon([(x*scale,y*scale) for x,y in points], fill='#d8dbdf', outline='#25272b', width=2*scale)
    draw.line([(15*scale,9*scale),(42*scale,9*scale),(45*scale,13*scale)],fill='white',width=scale)
    def line(points):
        draw.line([(x*scale,y*scale) for x,y in points], fill='#17191d', width=2*scale, joint='curve')
    if right:
        line([(16,31),(16,17),(23,17),(26,20),(26,23),(23,25),(16,25)])
        line([(22,25),(27,31)])
    else:
        line([(16,17),(16,31),(25,31)])
    line([(30,19),(32,17),(37,17),(39,19),(39,23),(30,31),(40,31)])
    return image.resize((64,64), Image.Resampling.LANCZOS)


def guide():
    scale = 4
    image = Image.new('RGBA',(128*scale,128*scale))
    draw = ImageDraw.Draw(image)
    def ellipse(box,fill,outline=None,width=1):
        draw.ellipse(tuple(v*scale for v in box),fill=fill,outline=outline,width=width*scale)
    ellipse((5,5,123,123),'#252932','#c5cbd2',3)
    ellipse((12,12,116,116),'#10141c','#518dc4',2)
    ellipse((27,27,101,101),'#303741','#6a7380',1)
    draw.arc((30*scale,30*scale,98*scale,98*scale),195,340,fill='#7e8996',width=2*scale)
    white='#f1f3f6'
    draw.rounded_rectangle((37*scale,43*scale,60*scale,67*scale),radius=6*scale,outline=white,width=4*scale)
    draw.line([(39*scale,45*scale),(39*scale,84*scale)],fill=white,width=4*scale)
    draw.line([(84*scale,47*scale),(78*scale,43*scale),(70*scale,43*scale),
               (65*scale,48*scale),(65*scale,55*scale),(80*scale,65*scale),
               (85*scale,72*scale),(81*scale,80*scale),(73*scale,83*scale),
               (65*scale,79*scale)],fill=white,width=4*scale,joint='curve')
    return image.resize((128,128),Image.Resampling.LANCZOS)


def encode(name, image):
    output = io.BytesIO()
    image.save(output, format='DDS', pixel_format='DXT5')
    raw = output.getvalue()
    assert raw[84:88] == b'DXT5'
    blocks = raw[128:]
    assert len(blocks) == image.width*image.height
    swapped = bytearray(blocks)
    swapped[::2], swapped[1::2] = blocks[1::2], blocks[::2]
    lines = [f'constexpr uint8_t {name}[{len(swapped)}] = {{']
    for i in range(0,len(swapped),24):
        lines.append('    ' + ', '.join(f'0x{v:02X}' for v in swapped[i:i+24]) + ',')
    return '\n'.join(lines + ['};',''])


if __name__ == '__main__':
    source = TARGET.read_text().split(MARKER)[0].rstrip() + '\n\n'
    art = []
    for name in ('LB','RB'):
        image = decode(source,'kPlayStation'+name,64,32).resize((128,64),Image.Resampling.LANCZOS)
        art.append(encode('kPlayStation'+name+'Large',image))
    art += [encode('kPlayStationLT',trigger(False)),encode('kPlayStationRT',trigger(True)),
            encode('kPlayStationGuide',guide())]
    TARGET.write_text(source+MARKER+'\n'+'\n'.join(art),encoding='utf-8',newline='\n')
