# dnSpyEx

dnSpyEx is an unofficial continuation of the [dnSpy](https://github.com/dnSpy/dnSpy) project which is a debugger and .NET assembly editor. You can use it to edit and debug assemblies even if you don't have any source code available. Main features:

- Debug .NET and Unity assemblies
- Edit .NET and Unity assemblies
- Light and dark themes

See below for more features

![debug-animated](images/debug-animated.gif)

![edit-code-animated](images/edit-code-animated.gif)

## Binaries

Latest stable release: https://github.com/dnSpyEx/dnSpy/releases

If you like living on the edge you can use the latest "beta" builds from:
[![](https://github.com/dnSpyEx/dnSpy/workflows/GitHub%20CI/badge.svg)](https://github.com/dnSpyEx/dnSpy/actions)

## Building

```PS
git clone --recursive https://github.com/dnSpyEx/dnSpy.git
cd dnSpy
# or dotnet build
./build.ps1 -NoMsbuild
```

### Linux / macOS

The GUI requires Windows (see [Running the GUI with Wine](#running-the-gui-with-wine) and the [cross-platform browser preview](#cross-platform-browser-preview)), but the command line decompiler (`dnSpy.Console`) also targets `net10.0` and runs on Windows, Linux and macOS. You need the .NET 10 SDK.

```sh
git clone --recursive https://github.com/dnSpyEx/dnSpy.git
cd dnSpy
./build.sh
dnSpy/dnSpy/bin/Release/net10.0/dnSpy.Console -o out MyApp.dll
```

`./build.sh` builds a framework dependent `dnSpy.Console` that needs the .NET 10 runtime. If that runtime isn't in your default .NET install (eg. you installed the SDK to `~/.dotnet`), run it with `DOTNET_ROOT` pointing at that install.

To get a self-contained build that runs without .NET installed, publish it for one or more [runtime identifiers](https://learn.microsoft.com/dotnet/core/rid-catalog). With no runtime identifier it publishes for the current OS and architecture.

```sh
./build.sh publish                      # current OS/arch, eg. linux-x64
./build.sh publish linux-arm64 osx-arm64 win-x64
dnSpy/dnSpy/bin/Release/net10.0/linux-x64/publish/dnSpy.Console -o out MyApp.dll
```

On Windows use `./build.ps1 console -Rid win-x64,win-arm64`. Publish `osx-*` on macOS, the SDK can only sign the executable there and macOS on Apple silicon won't run unsigned executables. On Linux the self-contained build needs `libstdc++` and ICU (eg. `apk add libstdc++ icu-libs` on Alpine, use the `linux-musl-*` runtime identifiers there). The CI uploads self-contained builds as `dnSpy-console-<rid>` artifacts, and a framework dependent build as `dnSpy-console-net`.

`./build.sh all` builds the whole solution, including the Windows-only GUI targets (they compile on Linux but don't run there).

Decompiling BAML to XAML needs the .NET Framework WPF assemblies (`PresentationFramework`, `PresentationCore`, `WindowsBase` 4.0.0.0), which aren't installed on Linux or macOS. The [Microsoft.NETFramework.ReferenceAssemblies.net48](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48) package (already in your NuGet cache after building) provides them:

```sh
refs=~/.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8
dnSpy/dnSpy/bin/Release/net10.0/dnSpy.Console --asm-path "$refs:$refs/Facades" -o out MyWpfApp.dll
```

To work in JetBrains Rider, open `dnSpy.sln` and use the shared `dnSpy.Console (net10.0)` run configuration. If Rider picks up an older SDK, point *Settings | Build, Execution, Deployment | Toolset and Build | .NET CLI executable path* at a .NET 10 `dotnet`.

### Cross-platform browser (preview)

`dnSpy.Avalonia` is an early [Avalonia](https://avaloniaui.net/) port of the GUI that runs on Windows, Linux and macOS. It's a read-only browser: open assemblies (File menu, command line or drag and drop), browse the assembly tree and read the decompiled code in any of the decompiler languages. Click a reference to go to its definition, `Alt+Left` / `Alt+Right` go back and forward. There's no debugger, editing, search, tabs or extension support yet. It remembers the open assemblies in the same settings file as the WPF GUI.

```sh
./build.sh avalonia
dnSpy/dnSpy/bin/Release/net10.0/dnSpy.Avalonia MyApp.dll
./build.sh avalonia-publish             # self-contained, eg. dnSpy/dnSpy/bin/Release/net10.0/linux-x64/publish/dnSpy.Avalonia
```

### Running the GUI with Wine

The GUI is WPF and only runs on Windows, but it can be run on Linux and macOS with [Wine](https://www.winehq.org/). This isn't officially supported and hasn't been tested much, expect rendering glitches.

- Use the self-contained `dnSpy-net-win64` build, it doesn't need .NET to be installed in the Wine prefix. The `dnSpy-netframework` build needs .NET Framework 4.8 (`winetricks -q dotnet48`) and replaces Wine Mono.
- Use a 64-bit prefix and a recent Wine version.
- Install the core fonts if text is missing or looks wrong: `winetricks -q corefonts`
- If windows are black or don't redraw, disable WPF hardware acceleration:

  ```sh
  wine reg add 'HKCU\Software\Microsoft\Avalon.Graphics' /v DisableHWAcceleration /t REG_DWORD /d 1 /f
  ```

- Debugging .NET programs depends on Windows debugging APIs and is unlikely to work under Wine.

If you only need to decompile, prefer the native `dnSpy.Console` builds above.

To debug Unity games, you need this repo too: https://github.com/dnSpyEx/dnSpy-Unity-mono

# Debugger

- Debug .NET Framework, .NET and Unity game assemblies, no source code required
- Set breakpoints and step into any assembly
- Locals, watch, autos windows
- Variables windows support saving variables (eg. decrypted byte arrays) to disk or view them in the hex editor (memory window)
- Object IDs
- Multiple processes can be debugged at the same time
- Break on module load
- Tracepoints and conditional breakpoints
- Export/import breakpoints and tracepoints
- Optional Just My Code (JMC) stepping filters for system libraries
- Call stack, threads, modules, processes windows
- Break on thrown exceptions (1st chance)
- Variables windows support evaluating C# / Visual Basic expressions
- Dynamic modules can be debugged (but not dynamic methods due to CLR limitations)
- Output window logs various debugging events, and it shows timestamps by default :)
- Assemblies that decrypt themselves at runtime can be debugged, dnSpy will use the in-memory image. You can also force dnSpy to always use in-memory images instead of disk files.
- Bypasses for common debugger detection techniques
- Public API, you can write an extension or use the C# Interactive window to control the debugger

# Assembly Editor

- All metadata can be edited
- Edit methods and classes in C# or Visual Basic with IntelliSense, no source code required
- Add new methods, classes or members in C# or Visual Basic
- IL editor for low-level IL method body editing
- Low-level metadata tables can be edited. This uses the hex editor internally.

# Hex Editor

- Click on an address in the decompiled code to go to its IL code in the hex editor
- The reverse of the above, press F12 in an IL body in the hex editor to go to the decompiled code or other high-level representation of the bits. It's great to find out which statement a patch modified.
- Highlights .NET metadata structures and PE structures
- Tooltips show more info about the selected .NET metadata / PE field
- Go to position, file, RVA
- Go to .NET metadata token, method body, #Blob / #Strings / #US heap offset or #GUID heap index
- Follow references (Ctrl+F12)

# Other

- BAML decompiler and disassembler
- Blue, light and dark themes (and a dark high contrast theme)
- Bookmarks
- C# Interactive window can be used to script dnSpy
- Search assemblies for classes, methods, strings, etc
- Analyze class and method usage, find callers, etc
- Multiple tabs and tab groups
- References are highlighted, use Tab / Shift+Tab to move to the next reference
- Go to the entry point and module initializer commands
- Go to metadata token or metadata row commands
- Code tooltips (C# and Visual Basic)
- Export to project

# List of other open source libraries used by dnSpy

- [ILSpy decompiler engine](https://github.com/icsharpcode/ILSpy) (C# and Visual Basic decompilers)
- [Roslyn](https://github.com/dotnet/roslyn) (C# and Visual Basic compilers)
- [dnlib](https://github.com/0xd4d/dnlib) (.NET metadata reader/writer which can also read obfuscated assemblies)
- [VS MEF](https://github.com/microsoft/vs-mef) (Faster MEF equals faster startup)
- [ClrMD](https://github.com/microsoft/clrmd) (Access to lower level debugging info not provided by the CorDebug API)
- [Iced](https://github.com/icedland/iced) (x86/x64 disassembler)
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) (JSON serializer & deserializer)
- [NuGet.Configuration](https://github.com/NuGet/NuGet.Client) (NuGet configuration file reader)

# Translating dnSpy

[Click here](https://crowdin.com/project/dnspy) if you want to help with translating dnSpy to your native language.

# Wiki

See the [Wiki](https://github.com/dnSpyEx/dnSpy/wiki) for build instructions and other documentation.

# License

dnSpy is licensed under [GPLv3](dnSpy/dnSpy/LicenseInfo/GPLv3.txt).

# [Credits](dnSpy/dnSpy/LicenseInfo/CREDITS.txt)
