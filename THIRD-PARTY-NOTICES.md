# Third-party components

This editor uses the following components. The linked projects contain their complete license texts and notices.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| AvalonEdit | 6.3.1.120 | MIT | https://github.com/icsharpcode/AvalonEdit |
| WpfMath / XamlMath | 2.1.0 | MIT and OFL-1.1 (font notices included) | https://github.com/ForNeVeR/xaml-math |
| SharpVectors.Wpf | 1.8.5 | BSD-3-Clause | https://github.com/ElinamLLC/SharpVectors |
| PDFtoImage | 5.4.0 | MIT | https://github.com/sungaila/PDFtoImage |
| SkiaSharp | 4.150.1 | MIT and bundled third-party notices | https://github.com/mono/SkiaSharp |
| bblanchon.PDFium.Win32 / PDFium | 152.0.7961 | NuGet Apache-2.0 metadata; packaging source MIT; PDFium BSD-3-Clause and bundled third-party licenses | https://github.com/bblanchon/pdfium-binaries |
| .NET / WPF | 10 | MIT | https://github.com/dotnet/wpf |
| semojum-braille | 3.4.24 | MIT OR Apache-2.0 | https://github.com/Semojum/braille |
| braillify | 2.0.1 | See bundled engine NOTICE and third_party | https://pypi.org/project/braillify/2.0.1/ |
| Python | 3.13 | PSF | https://docs.python.org/3/license.html |
| pydantic | 2.14.1 | MIT | https://github.com/pydantic/pydantic |
| PyInstaller (build tool) | 6.22.3 | GPL with distribution exception | https://pyinstaller.org/ |

License texts and font notices are retained in the `licenses` directory. The self-contained Windows distribution also includes the .NET runtime and its bundled third-party notices.

The engine distribution retains upstream NOTICE.md, LICENSE-MIT, LICENSE-APACHE, third_party notices and dependency licenses under engine. The optional kiwi dependency is neither installed nor bundled.

The desktop composition adapter (`scripts/editor_composition.py`) reuses the pinned semojum-braille `assist` and BRF helpers and follows its pagination structure. The upstream license and NOTICE files are included with the engine distribution.
