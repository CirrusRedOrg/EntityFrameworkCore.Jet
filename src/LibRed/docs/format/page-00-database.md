# Page 0 — database definition

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 2. Page 0 — database definition

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type, `0x00` |
| `0x01` | 3 | Unknown (not decoded) |
| `0x04` | 15 | Format identifier ASCII: `Standard Jet DB` or `Standard ACE DB` |
| `0x13` | 1 | Unknown — string padding/terminator (not decoded) |
| `0x14` | 1 | Version byte (see below). mdbtools reads `jet_version` as a 4-byte word at `0x14`; the version is its low byte and `0x15`–`0x17` are zero |
| `0x15` | 3 | Unknown — upper bytes of the version word (zero observed; not decoded) |
| `0x18`+ | … | Obfuscated/encrypted (code page, collation, creation date, password) — **not decoded** |

Version byte → format:

| `0x14` | Version | Page size | Family |
| --- | --- | --- | --- |
| `0x00` | Jet 3 (Access 97) | 2048 | MDB |
| `0x01` | Jet 4 (Access 2000–2003) | 4096 | MDB |
| `0x02` | ACE 12 (Access 2007) | 4096 | ACCDB |
| `0x03` | ACE 14 (Access 2010) | 4096 | ACCDB |
| `0x05` | ACE 16 (Access 2016) | 4096 | ACCDB |
| `0x06` | ACE 17 (Access 2019+) | 4096 | ACCDB |

Everything from `0x18` onward on page 0 is obfuscated; LibRed reads only the identifier and
version. (Page-level encryption for password-protected files is not implemented.)

