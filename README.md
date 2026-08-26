# TIFF Town
<img align="right" src="https://github.com/DerekPascarella/TIFF-Town/blob/main/screenshots/screenshot.png?raw=true" width="200">TIFF Town is a cross-platform image converter that turns modern image formats into TIFF files readable by TownsOS on the FM Towns and FM Towns Marty.

TIFF Town lets users load an image in a variety of formats, preview exactly what the Towns will display, adjust settings, then export to TIFF.

## Table of Contents

- [Current Version](#current-version)
- [Changelog](#changelog)
- [Supported Platforms](#supported-platforms)
- [Supported Image Formats](#supported-image-formats)
- [Towns TIFF Variants](#towns-tiff-variants)
- [Basic Usage](#basic-usage)
  - [Loading an Image](#loading-an-image)
  - [Wallpaper Tab](#wallpaper-tab)
  - [Advanced Tab](#advanced-tab)
  - [Viewer Tab](#viewer-tab)
  - [Install Tab](#install-tab)
  - [Comparing Against the Original](#comparing-against-the-original)
  - [Saving](#saving)
- [Installing a Wallpaper on the Towns](#installing-a-wallpaper-on-the-towns)
- [Legal and Licensing](#legal-and-licensing)
  - [TIFF Town](#tiff-town-1)
  - [Third-Party Components](#third-party-components)

## Current Version
TIFF Town is currently at version [1.1.0](https://github.com/DerekPascarella/TIFF-Town/releases/tag/1.1.0).

## Changelog
- **Version 1.1.0 (2026-08-26)**
  - Cleaned up dialog box messages.
  - Added an FM Towns TIFF viewer tab.
  - Added an Install tab to easily inject TMENU.TIF into HDD image files.
  - Fixed 16-color output, the palette is now snapped to the FM Towns' 4-bit-per-channel hardware palette before dithering, so smooth gradients no longer come out banded or color-cast.
- **Version 1.0.0 (2026-08-25)**
  - Initial release.

## Supported Platforms

| Platform | Architecture | Download |
|----------|-------------|----------|
| Windows | x64 | `.zip` |
| Windows | x86 | `.zip` |
| macOS | Apple Silicon | `.tar.gz` (`.app` bundle) |
| macOS | Intel | `.tar.gz` (`.app` bundle) |
| Linux | x64 | `.tar.gz` |

## Supported Image Formats

| Format | Extension(s) | Notes |
|--------|-------------|-------|
| PNG | `.png` | Transparency is composited over black |
| JPEG | `.jpg`, `.jpeg` | |
| GIF | `.gif` | First frame only |
| BMP | `.bmp` | |
| TIFF | `.tif`, `.tiff` | Standard TIFF, not Towns TIFF. Already have a Towns TIFF? Use the [Viewer tab](#viewer-tab) instead |
| WebP | `.webp` | |
| TGA | `.tga` | |
| QOI | `.qoi` | |
| PBM | `.pbm`, `.pgm`, `.ppm` | |
| ICO | `.ico` | |

## Towns TIFF Variants

TownsOS reads four flavors of TIFF, and TIFF Town writes all of them.

| Depth | Bits per pixel | Notes |
|-------|----------------|-------|
| 16 colors | 4 | Palette. The depth TownsOS uses for 640x480 wallpaper |
| 256 colors | 8 | Palette |
| 32,768 colors | 16 | Towns VRAM layout, unreadable by general-purpose TIFF software |
| 24-bit | 24 | Full RGB. Not valid for wallpaper |

The 32,768-color variant is the interesting one. Its pixels are 16-bit little-endian words in Towns hardware order (green in bits 14-10, red in 9-5, blue in 4-0) behind a TIFF header that declares the file grayscale. Nothing outside the Towns world will decode it correctly, which is why a converter like this one needs to exist in the first place.

Files are written uncompressed by default. Standard TIFF LZW is available as an option, and TownsOS reads it, but a handful of third-party Towns loaders do not.

## Basic Usage

### Loading an Image
Click **Load Image...** on the Wallpaper or Advanced tab, or launch the application with an image file as its argument.

Once an image is loaded, every change to the settings re-runs the conversion in the background and updates the preview. The **Result** block shows the source dimensions and unique color count, along with the mode the converter settled on and the real encoded size of the file that would be written.

### Wallpaper Tab
TownsOS is a bit picky about desktop wallpaper. It displays exactly two combinations of resolution and color depth, and anything else renders as solid black, including wallpaper files written by Fujitsu's own tools.

The Wallpaper tab exists to make this as easy as possible for users. It offers those two combinations and nothing else.

- **640x480, 16 colors** - Sharp and standard. The right choice for most wallpapers.
- **320x240, 32,768 colors** - Full color, slightly softer. TownsOS pixel-doubles it to fill the screen.

It's important to note, however, that TownsOS high-resolution mode supports wallpapers at 1024x768 in 16 colors and 512x384 in 32,768 colors. These high-resolution wallpapers can be created using the [Advanced Tab](#advanced-tab).

Three fit choices control how the source image lands on the screen.

- **Fill screen** - Covers the whole screen, trimming whatever sticks out.
- **Fit whole image** - Keeps the entire picture, filling the leftover space with a border color.
- **Stretch** - Matches the exact shape, squashing or stretching as needed.

Dithering, resampling, and compression aren't exposed here, because a wallpaper conversion has one correct answer for each. The **Save TMENU.TIF...** button writes the file under the name TownsOS looks for. To write it directly into an HDD image instead, see the [Install tab](#install-tab).

### Advanced Tab
The Advanced tab is the full converter, for images that aren't wallpaper.

**Color depth** defaults to **Auto**, which counts the unique colors left after any resizing and picks the smallest variant that holds them all. Up to 16 colors produces a 16-color TIFF, up to 256 produces 256-color, and anything beyond that produces 32,768-color. Auto never selects 24-bit, since a 24-bit file is no use as wallpaper. Any depth can be forced manually.

**Resolution** defaults to the source image's own dimensions. Choosing **Custom** enables the width and height fields, both capped at 1024. The **640x480** and **320x240** buttons fill in a wallpaper size and lock the matching color depth for it, though the fields stay editable afterward.

**Scaling** becomes available once a custom resolution is set.

- **Stretch** - Fills the exact size, distorting if the shape differs.
- **Fit** - Keeps the aspect ratio and paints the margins with the fill color.
- **Crop** - Keeps the aspect ratio, scales to cover, and trims the overflow.
- **Center** - No scaling at all, padding around the image with the fill color.

The fill color is painted into the image before conversion, so in the palette modes it occupies a palette slot like any other color. **Resampling** offers Smooth (bicubic) for photographs and Sharp (nearest neighbor) for pixel art. Center never scales, so resampling doesn't apply to it.

**Dithering** is on by default and uses Floyd-Steinberg error diffusion. It only comes into play when colors are actually being reduced, so an image that already fits the target depth converts exactly, palette and all.

**LZW compression** is off by default. See [Towns TIFF Variants](#towns-tiff-variants) for why.

### Viewer Tab
The Viewer tab reads a Towns TIFF back instead of converting one. Click **Load Towns TIFF...** and pick a file; TIFF Town decodes it directly rather than through the same path used for source images, so it displays correctly even though a generic TIFF reader can't make sense of most of these files (see [Towns TIFF Variants](#towns-tiff-variants)).

The **Result** block reports the file name, dimensions, color depth, and compression. **Save as PNG...** exports the decoded image as a normal PNG once a file has loaded.

There's nothing to convert here, so the depth, resolution, and scaling controls from the other tabs don't apply, and hold-to-compare has nothing to compare against, so it stays disabled on this tab.

### Install Tab
The Install tab writes a wallpaper straight into an FM Towns hard-disk image, the kind used by SCSI drive emulators like BlueSCSI, ZuluSCSI, ArdSCSino, and Henkan Bancho, as well as by software emulators like Tsugaru and Unz. There's no need to boot an emulator or mount anything just to copy one file.

Click **Load HDD Image...** and pick the image. TIFF Town finds the Towns partition table by reading the file itself, so the extension doesn't matter (`.hda`, `.hds`, `.hdd`, `.h0`, and so on are all the same raw format). Fixed VHD images and the T98-Next NHD and Anex86 HDI container formats are also recognized. The **HDD image** and **Target** blocks report what was found, including which partition holds a bootable TownsOS system.

Click **Load Wallpaper TIFF...** and pick the TIFF to install, such as one saved from the [Wallpaper tab](#wallpaper-tab). The file is checked against the two combinations TownsOS actually displays as wallpaper (640x480 in 16 colors, 320x240 in 32,768 colors) and refused otherwise, since anything else would come up as a black desktop. The preview shows the loaded file.

**Install Wallpaper...** writes the file into the root of the bootable TownsOS partition as `TMENU.TIF`, replacing any `TMENU.TIF` already there. Nothing else on the image is touched, and the written copy is read back and verified byte-for-byte. A confirmation dialog names the image and partition before anything is written.

### Comparing Against the Original
Press and hold **Show original (hold)** beneath the preview to swap in the source image, and release to go back to the converted result. This is the quickest way to judge dithering and palette reduction, especially at 16 colors.

### Saving
Clicking **Save As...** on the Advanced tab opens a save dialog prefilled with an 8.3 DOS name derived from the source file name, uppercased and stripped of characters MS-DOS won't accept, with a `.TIF` extension. The Wallpaper tab's **Save TMENU.TIF...** button prefills `TMENU.TIF` instead. Both default to the folder the source image came from.

A confirmation dialog reports the full path and byte count once the file is written.

## Installing a Wallpaper on the Towns
<img align="right" src="https://github.com/DerekPascarella/TIFF-Town/blob/main/screenshots/menu.png?raw=true" width="150">For an HDD image, the [Install tab](#install-tab) does all of this automatically. On real hardware, or with a disk mounted some other way, copy `TMENU.TIF` to the root of the bootable TownsOS partition. The file name has to be exactly that, and it has to sit in the root, not in a subdirectory.

Then enable the background from within TownsMENU by opening the settings menu and checking the background display option, as shown to the right.

If the desktop comes up black, the file is the wrong resolution or color depth for the wallpaper layer. Reconvert it from the Wallpaper tab, which can only produce combinations that work.

## Legal and Licensing

### TIFF Town
**Copyright (C) 2026, Derek Pascarella (ateam)**

Licensed under the GNU General Public License v3.0 (GPL-3.0).

Repository: https://github.com/DerekPascarella/TIFF-Town

For the full license text, see [LICENSE.txt](LICENSE.txt).

### Third-Party Components
- [Avalonia UI](https://avaloniaui.net/) (MIT) - cross-platform GUI framework, including its Fluent theme, color picker, and the Inter typeface (SIL OFL 1.1)
- [ImageSharp](https://github.com/SixLabors/ImageSharp) (Six Labors Split License) - source image decoding, resampling, and color quantization
- [MessageBox.Avalonia](https://github.com/AvaloniaCommunity/MessageBox.Avalonia) (MIT) - modal dialogs