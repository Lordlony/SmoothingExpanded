# Building Smoothing Expanded 1.0.0

Requirements:

- RimWorld 1.6 installed locally.
- Visual Studio 2022 with .NET Framework 4.7.2 targeting support.
- Harmony installed only when building the optional Harmony assembly.

From the mod root, build the dependency-free assembly first, followed by the
optional integration:

```powershell
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild Source\SmoothingExpanded.csproj /t:Build /p:Configuration=Release
& $msbuild Optional\OptionalHarmony\Source\SmoothingExpanded.Harmony.csproj /t:Build /p:Configuration=Release
```

The projects default to the standard paths used by this development checkout.
Other installations can supply paths explicitly:

```powershell
& $msbuild Source\SmoothingExpanded.csproj /t:Build /p:Configuration=Release /p:RimWorldDir="D:\Games\RimWorld"
& $msbuild Optional\OptionalHarmony\Source\SmoothingExpanded.Harmony.csproj /t:Build /p:Configuration=Release /p:RimWorldDir="D:\Games\RimWorld" /p:HarmonyDir="D:\RimWorldMods\Harmony\Current"
```

Outputs are written directly to `Assemblies` and
`Optional/OptionalHarmony/Assemblies`. Validate XML, translation keys and
formatting placeholders after building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Validate.ps1
```

The project does not yet declare an open-source license. Source availability
alone does not grant redistribution rights; the author should select a license
before accepting external redistribution or derivative releases.
