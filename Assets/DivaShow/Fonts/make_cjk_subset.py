"""Rebuild Resources/DivaCJK.ttf: the Chinese characters used in UI string literals, cut from Source Han Sans CN Heavy.
Usage: python3 Assets/DivaShow/Fonts/make_cjk_subset.py /path/to/SourceHanSansCN-Heavy.ttf   (run from the project root)
The subset is renamed "Diva CJK": the OFL forbids the reserved name "Source" on modified versions."""
import glob, re, sys, tempfile, os
from fontTools import subset
from fontTools.ttLib import TTFont

chars = set('0123456789一二三四五六七八九十百千万第名次秒分米')
for f in glob.glob('Assets/DivaShow/Runtime/*.cs') + glob.glob('Assets/DivaVR/Runtime/*.cs') + glob.glob('Assets/eva大象/**/*.cs', recursive=True) + glob.glob('Assets/DivaGame/Runtime/*.cs'):
    for lit in re.findall(r'"((?:[^"\\\n]|\\.)*)"', open(f, encoding='utf-8').read()):
        chars |= {c for c in lit if ord(c) >= 0x2E80 or c in '·—…「」『』，。！？：；（）、'}
tmp = os.path.join(tempfile.mkdtemp(), 'sub.ttf')
subset.main([sys.argv[1], '--text=' + ''.join(sorted(chars)), '--output-file=' + tmp, "--layout-features=*", '--no-hinting'])
font = TTFont(tmp)
names = font['name']
for rec in list(names.names):
    if rec.nameID in (1, 3, 4, 6, 16, 17, 18, 21, 22):
        names.removeNames(nameID=rec.nameID)
for pid, eid, lid in ((3, 1, 0x409), (1, 0, 0)):
    names.setName('Diva CJK', 1, pid, eid, lid)
    names.setName('Regular', 2, pid, eid, lid)
    names.setName('Diva CJK Heavy; subset of Source Han Sans CN Heavy', 3, pid, eid, lid)
    names.setName('Diva CJK', 4, pid, eid, lid)
    names.setName('DivaCJK-Heavy', 6, pid, eid, lid)
font.save('Assets/DivaShow/Resources/DivaCJK.ttf')
print(len(chars), 'characters ->', os.path.getsize('Assets/DivaShow/Resources/DivaCJK.ttf'), 'bytes')
