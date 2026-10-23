# ZKitaplastir

A small Windows tool that turns an open **z-kitap** reader page-by-page into a high-quality **PDF**.

While the reader shows a book, the tool advances pages automatically, captures each page image directly from the reader's memory (full resolution, no screen capture, no UI elements), removes duplicate captures, and writes a PDF next to the executable.

## Requirements

- Windows 10/11 (x64)
- .NET Framework 4.x (built into Windows)
- Administrator rights (the tool reads another process's memory; a UAC prompt appears)
- A supported z-kitap reader with a book open (e.g. `AriKutuphane.exe`, `HizKutuphanesi.exe`, ...)

## Usage

1. Open the z-kitap reader and open the book. Leave the reader window open (do not minimize).
2. **Hide the answers** in the reader before capturing, if you do not want them in the PDF. The tool captures exactly what the reader renders.
3. Run `ZKitaplastir.exe` (double-click). Accept the UAC prompt.
4. Type the number of pages to advance (press **Enter** to auto-detect and stop when pages stop changing).
5. Type the page-turn delay in milliseconds, e.g. `600`, `800`, `1000` (press **Enter** for 800).
6. Wait. When finished, the PDF is written to `pdfler\<book name>.pdf` in the same folder as the executable.

The tool prints progress to the console and can write a log with `-log <path>`.

## Output

- Folder: `pdfler\` next to `ZKitaplastir.exe`
- File: `<book name>.pdf` (book name is detected from the reader; falls back to the window title)
- Size: about 30 MB for ~200 pages (page images are downscaled to 1000 px wide and JPEG-encoded at quality 68). Adjust with `-w` and `-q`.
- Temporary captures are stored in `pdfler\_raw` and deleted automatically after the PDF is created (use `-keepraw` to keep them).

## Building

On Windows, run `build.bat`. It uses the .NET Framework compiler (`csc.exe`) and the UIAutomation assemblies from the GAC, and embeds `dwebp.exe` into the executable as a resource.

## Command-line options

| Option | Description |
|---|---|
| `-pages N` | Advance exactly N pages (skip the prompt) |
| `-settle MS` | Wait after each page turn before capturing (default 650) |
| `-k N` | Auto mode: stop after N consecutive turns with no new page (default 10) |
| `-rewind N` | Press PageUp N times before starting (go back to the first page) |
| `-w PX` | Target image width in pixels (default 1000) |
| `-q N` | JPEG quality 0-100 (default 68) |
| `-dup T` | Near-duplicate threshold (default 5; lower = stricter) |
| `-out PATH` | Explicit output PDF path |
| `-log PATH` | Write a log file |
| `-keepraw` | Do not delete `pdfler\_raw` after building |
| `-nopause` | Do not wait for key presses (unattended) |

## Notes and limitations

- The tool captures the page images the reader decrypts and renders; it does not break any DRM.
- Page turning uses the PageDown key delivered to the reader window.
- If the reader window is closed, the tool stops.
- Each page is usually captured twice: once with the answer overlay and once clean. Near-identical captures are grouped, and the tool keeps the version with the **least ink** (the clean page) for every group, so answers are removed automatically.

## Third-party

`dwebp.exe` from libwebp 1.4.0, BSD 3-Clause License. See `THIRD-PARTY-NOTICES.txt`.
