# Historical Unicode name mapping inputs

`Unicode11Mappings.bin` is generated offline by
`Tools/generate_name_unicode11.py` from these unmodified data files:

- `HANGUL.TXT`: [Korean Hangul Encoding Conversion Table, 1995-10-04](https://www.unicode.org/Public/MAPPINGS/OBSOLETE/EASTASIA/KSC/HANGUL.TXT). Columns 6 and 7 supply the published Unicode 1.1 old-Hangul and modern-Hangul code points. The obsolete directory is historical reference data, not a current text-encoding recommendation.
- `DerivedAge-18.0.0.txt`: [Unicode Character Database 18.0.0 DerivedAge](https://www.unicode.org/Public/18.0.0/ucd/DerivedAge.txt). Age 1.1 identifies the stable BMP positions admitted by this historical text profile.

Both local source files were checked byte-for-byte against those official URLs
on 2026-09-30. The generated binary contains an assignment bitmap and old
Hangul conversion values; it does not contain a general Unicode text database.
This profile leaves private-use, noncharacter, and unknown historical positions
as raw bytes rather than claiming they have interchangeable modern name text.

The source URLs are under Unicode's `/Public/` tree. Its
[Terms of Use](https://www.unicode.org/terms_of_use.html) define computer data
files there as Unicode Data Files (§02.2) and apply the
[Unicode License v3](https://www.unicode.org/license.txt) unless a specific
restriction, permission, or license is identified at release (§03.3). The
unmodified current license text is included as `LICENSE-Unicode.txt`; retain
the source notices and this license with redistributed copies. The historical
Hangul file credits K.D. Chang, In Sook Choi, and Jung Ho Kim. Neither the
general license nor this README warrants the absence of third-party claims.
