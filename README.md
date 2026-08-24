# TIFF Town
<img align="right" src="https://github.com/DerekPascarella/TIFF-Town/blob/main/screenshots/screenshot.png?raw=true" width="265">TIFF Town is a cross-platform image converter that turns modern image formats into TIFF files readable by TownsOS on the FM Towns and FM Towns Marty.

Load an image, see exactly what the Towns will display, adjust the settings, and save. That's it!

Output matches the byte layout written by Fujitsu's own TownsOS software, including the Towns-specific 32,768-color format that no general-purpose TIFF writer produces.

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
  - [Comparing Against the Original](#comparing-against-the-original)
  - [Saving](#saving)
- [Installing a Wallpaper on the Towns](#installing-a-wallpaper-on-the-towns)
- [Legal and Licensing](#legal-and-licensing)
  - [TIFF Town](#tiff-town-1)
  - [Third-Party Components](#third-party-components)

## Current Version
TIFF Town is currently at version [1.0.0](https://github.com/DerekPascarella/TIFF-Town/releases/tag/1.0.0).

## Changelog
- **Version 1.0.0 (2026-08-24)**
  - Initial release.

## Supported Platforms

| Platform | Architecture | Download | Notes |
|----------|-------------|----------|-------|
| Windows | x64 | `.zip` | Self-contained, no runtime needed |
| Windows | x86 | `.zip` | Self-contained, no runtime needed |
| macOS | Apple Silicon | `.tar.gz` (`.app` bundle) | Self-contained, no runtime needed |
| macOS | Intel | `.tar.gz` (`.app` bundle) | Self-contained, no runtime needed |
| Linux | x64 | `.tar.gz` | Self-contained, no runtime needed |

## Supported Image Formats

| Format | Extension(s) | Notes |
|--------|-------------|-------|
| PNG | `.png` | Transparency is composited over black |
| JPEG | `.jpg`, `.jpeg` | |
| GIF | `.gif` | First frame only |
| BMP | `.bmp` | |
| TIFF | `.tif`, `.tiff` | Standard TIFF, not Towns TIFF |
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
Click **Load Image...** on either tab, or launch the application with an image file as its argument.

Once an image is loaded, every change to the settings re-runs the conversion in the background and updates the preview. The **Result** block shows the source dimensions and unique color count, along with the mode the converter settled on and the real encoded size of the file that would be written.

### Wallpaper Tab
TownsOS is a bit picky about desktop wallpaper. It displays exactly two combinations of resolution and color depth, and anything else renders as solid black, including wallpaper files written by Fujitsu's own tools.

The Wallpaper tab exists so that mistake is impossible to make. It offers those two combinations and nothing else.

- **640 x 480, 16 colors** - Sharp and standard. The right choice for most wallpapers.
- **320 x 240, 32,768 colors** - Full color, slightly softer. TownsOS pixel-doubles it to fill the screen.

Three fit choices control how the source image lands on the screen.

- **Fill screen** - Covers the whole screen, trimming whatever sticks out.
- **Fit whole image** - Keeps the entire picture, filling the leftover space with a border color.
- **Stretch** - Matches the exact shape, squashing or stretching as needed.

Dithering, resampling, and compression aren't exposed here, because a wallpaper conversion has one correct answer for each. The **Save TMENU.TIF...** button writes the file under the name TownsOS looks for.

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

### Comparing Against the Original
Press and hold **Show original (hold)** beneath the preview to swap in the source image, and release to go back to the converted result. This is the quickest way to judge dithering and palette reduction, especially at 16 colors.

### Saving
Clicking **Save As...** on the Advanced tab opens a save dialog prefilled with an 8.3 DOS name derived from the source file name, uppercased and stripped of characters MS-DOS won't accept, with a `.TIF` extension. The Wallpaper tab's **Save TMENU.TIF...** button prefills `TMENU.TIF` instead. Both default to the folder the source image came from.

A confirmation dialog reports the full path and byte count once the file is written.

## Installing a Wallpaper on the Towns
<img align="right" src="https://github.com/DerekPascarella/TIFF-Town/blob/main/screenshots/menu.png?raw=true" width="150">Copy `TMENU.TIF` to the root of the bootable TownsOS partition. The file name has to be exactly that, and it has to sit in the root, not in a subdirectory.

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