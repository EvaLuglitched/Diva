# Diva CJK subset

visionOS keeps its Chinese system font (PingFang) in a private folder that TextMesh Pro cannot load, so Chinese
text showed as boxes on Vision Pro. The show now falls back to `Resources/DivaCJK.ttf`, a ~90 KB subset of
Source Han Sans CN Heavy (SIL OFL 1.1) with only the characters in the UI strings.

After adding new Chinese UI text, rebuild it (fonttools: `pip install fonttools`):

```bash
python3 Assets/DivaShow/Fonts/make_cjk_subset.py /path/to/SourceHanSansCN-Heavy.ttf
```
