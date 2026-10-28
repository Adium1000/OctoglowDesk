# Third-party notices

Octoglow Connect's own code is licensed under the MIT License in `LICENSE`.
That license does not relicense the third-party components below. Their
original license terms and notices are included in the `Licenses` folder,
which is shipped with the application.

## Microsoft Edge WebView2 SDK 1.0.3179.45

Used by the embedded web view. The Windows App SDK WinUI package brings in
Microsoft.Web.WebView2 as a NuGet dependency. Distributed files include
`Microsoft.Web.WebView2.Core.dll`,
`Microsoft.Web.WebView2.Core.Projection.dll`, and `WebView2Loader.dll`.

Copyright (C) Microsoft Corporation. All rights reserved.
License: the three-clause BSD-style terms supplied with the NuGet package.
The complete copyright notice, conditions, disclaimer, and third-party
notices are in:

- `Licenses/Microsoft.Web.WebView2-1.0.3179.45-LICENSE.txt`
- `Licenses/Microsoft.Web.WebView2-1.0.3179.45-NOTICE.txt`

Package: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3179.45
The WebView2 Runtime is installed separately on the user's machine; this app
does not bundle a fixed-version browser runtime.

## Microsoft Windows App SDK 1.8.251106002 / WinUI 1.8.251105000

Used for the WinUI 3 interface and application runtime. Distributed files
include `Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll`
and `Microsoft.WindowsAppRuntime.Bootstrap.dll`, among other Windows App SDK
assemblies. These packaged binaries are governed by the Microsoft Software
License Terms supplied with the NuGet packages, not by the MIT license for
the Windows App SDK source repository or by Octoglow Connect's MIT license.
The package notices are supplied alongside the terms:

- `Licenses/Microsoft.WindowsAppSDK-1.8.251106002-LICENSE.txt`
- `Licenses/Microsoft.WindowsAppSDK-1.8.251106002-NOTICE.txt`
- `Licenses/Microsoft.WindowsAppSDK.WinUI-1.8.251105000-LICENSE.txt`
- `Licenses/Microsoft.WindowsAppSDK.WinUI-1.8.251105000-NOTICE.txt`

Packages: https://www.nuget.org/packages/Microsoft.WindowsAppSDK/1.8.251106002
and https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/1.8.251105000

## SCSSdkClient / scs-sdk-plugin

Octoglow Connect reads Euro Truck Simulator 2's speed from the shared memory
written by [RenCloud/scs-sdk-plugin](https://github.com/RenCloud/scs-sdk-plugin),
following the layout that project defines. The plugin itself is installed by
the user in the game's folder and is not part of this distribution.

License: MIT License

Copyright (c) 2014 Hans

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
