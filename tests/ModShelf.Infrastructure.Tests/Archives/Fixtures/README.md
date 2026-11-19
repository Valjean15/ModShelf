# Archive fixtures

The archives `ArchiveReaderTests` reads. Each holds the same small mod, `Better Lanterns`: a readme, an `.esp`, a
texture, and notes with a Cyrillic name.

| File | Made with | What it is |
|---|---|---|
| `Lanterns.7z` | py7zr 1.1.4 | A solid 7z archive. |
| `Locked.7z` | py7zr 1.1.4, password `lantern`, `header_encryption=True` | Its list of files needs the password. |
| `LockedFiles.7z` | py7zr 1.1.4, password `lantern` | Its files need the password; its list doesn't. |
| `LockedFiles.zip` | Info-ZIP `zip -r -P lantern` | ZipCrypto: its files need the password. |
| `Lanterns.rar` | `make_rar5.py` | A RAR 5 archive of stored files, written by the script beside it from RARLAB's description of the format, since WinRAR's `rar` isn't free to run in CI. Its `Data/Textures` folder is only implied by its file's path. |
| `Locked.rar` | SharpCompress's test archive `Rar5.encrypted_filesAndHeader.rar` | A RAR 5 archive whose list of files needs a password. From [SharpCompress](https://github.com/adamhathcock/sharpcompress)'s `tests/TestArchives`, MIT licensed, Copyright (c) 2014 Adam Hathcock. |

Damaged archives are made by the tests, by cutting these short.

To make them again (Python 3, `pip install py7zr==1.1.4`, Info-ZIP's `zip`): `make_rar5.py` holds the mod's files itself; the others take them from a folder holding `Better Lanterns`, with the same files.

```bash
python3 make_rar5.py Lanterns.rar
python3 -c "import py7zr; py7zr.SevenZipFile('Lanterns.7z', 'w').writeall('Better Lanterns')"
zip -r -P lantern LockedFiles.zip "Better Lanterns"
```
