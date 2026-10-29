# Third-party components

The portable build contains these dependencies from NuGet:

- [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp), .NET bindings for LibVLC.
- [LibVLC](https://code.videolan.org/videolan/vlc), media decoding and playback. See VideoLAN's license files and source links for redistribution obligations.
- [Magick.NET](https://github.com/dlemstra/Magick.NET), image decoding, Apache-2.0.
- [ImageMagick](https://imagemagick.org/), used by Magick.NET; its native HEIC decoder uses libheif and libde265.

Before redistributing binaries to others, review the license notices included with the NuGet packages and provide any required notices and source links. The app itself does not download codecs at runtime.
