# MeritEvalBench

Merit-function evaluation timing — measures the three primitives the optimizer
is built from (whole-merit **value**, residuals+**jacobian**, and the **GPU**
value kernel), reported per design so CPU and GPU compare directly. Used to
characterize engine throughput across machines (various PCs, cloud GPUs, macOS).

It builds against the **shipped, obfuscated** `engine/LensHH.Core.dll` (public
API names are preserved) plus the public `LensHH.IO` project — so it needs
**only this public repository**, no engine/native source.

## Run it

### Windows (from the installer)
The installer ships a self-contained copy under the install folder:
```
"C:\Program Files\LensHH-LT\tools\bench\MeritEvalBench.exe" --lens MyLens.lhlt --csv pc1.csv
```

### Linux (from the AppImage)
```
./LensHH-LT-x86_64.AppImage --bench --lens MyLens.lhlt --csv box1.csv
```

### macOS (build from this public repo — no other repos required)
Prerequisite: the `engine/osx-arm64/liblenshh_native.dylib` and
`engine/LensHH.Core.dll` shipped in this repo (both are committed). Then:
```
brew install dotnet            # .NET 8 SDK, if not present
cd src/MeritEvalBench
dotnet build -c Release
dotnet run  -c Release -- --lens MyLens.lhlt --csv mac.csv
```

## Options
```
--lens <a.lhlt> [b.lhlt ...]
--mode value|jacobian|gpu|all      (default all)
--seconds <N>                      timed window per cell (default 5)
--cpu-batch <N>                    designs per parallel call (default = GPU fill)
--csv <out.csv>
```

## Notes
- Thread count = logical processors (auto). GPU batch = the device-fill design
  count read from the driver's occupancy for the merit kernel (not hardcoded;
  ≈6,144 on a 4060, larger on an A100).
- The headline GPU figure is GPU ÷ the **best** CPU-all-cores cell.
- Requires an activated engine (license token or trial) — it runs the normal
  product activation flow and prints the license state in its header. C# cells
  run even without the native engine (e.g. before activation).
