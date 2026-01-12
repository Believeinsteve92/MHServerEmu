# 2026-01-11 — PatchData Properties + Power Cost Curve Fixes

This note records the changes made to support PatchData `ValueType: "Properties"` patches (including curve properties), stabilize `!power cost` diagnostics, and apply an Endurance/Spirit cost curve to Captain America’s Furious Lunge.

## Outcome

- PatchData can apply curve properties via `ValueType: "Properties"` without crashing at startup.
- `!power cost <powerPrototypeId>` no longer disconnects the client; it writes a report file to the server `Download` folder.
- A Properties patch can set `EnduranceCost10.curve` for Furious Lunge and cost is reflected in-game.

## Key discovery: ManaType values

In this codebase:

- `ManaType.Type1 = 0`
- `ManaType.Type2 = 1`

See [src/MHServerEmu.Games/GameData/Prototypes/ManaBehaviorPrototype.cs](src/MHServerEmu.Games/GameData/Prototypes/ManaBehaviorPrototype.cs#L8-L16).

This matters for curve properties that are parameterized by `ManaType`.

## PatchData JSON used (runtime)

Runtime location (not in the git repo by default):

- `D:\Marvel Heroes Server Emulator\MHServerEmu 0.8.1 Test\MHServerEmu\Data\Game\Patches\PatchDataPowerEnduranceCurves.json`

Working content:

```json
[
  {
    "Enabled": true,
    "Prototype": "Powers/Player/CaptainAmerica/FuriousLunge.prototype",
    "Path": "Properties",
    "Description": "Apply EnduranceCost10.curve to Furious Lunge (Type1 mana) so it costs spirit like older versions.",
    "ValueType": "Properties",
    "Value": {
      "EnduranceCost": [0, "Powers/Curves/EnduranceCosts/EnduranceCost10.curve"],
      "EnduranceCostRequired": true
    }
  }
]
```

Notes:

- `EnduranceCost` is a **curve property** (array format `[param0, curve]` where param0 is `ManaType`).
- `EnduranceCostRequired` is a **boolean**, not a curve; it must be `true/false` (not an array).

## Code changes (repo)

### PatchData / Properties parsing

- [src/MHServerEmu.Games/GameData/PatchManager/PrototypePatchEntry.cs](src/MHServerEmu.Games/GameData/PatchManager/PrototypePatchEntry.cs)
  - `ValueType: "Properties"` is stored lazily (raw JSON) and materialized later to avoid initialization-order issues.
  - Curve properties are applied via `PropertyCollection.SetCurveProperty(...)`.
  - Asset-typed property params accept small numeric values as direct enum values (important for `ManaType`).

### Prevent crashes during asset enum lookup

- [src/MHServerEmu.Games/GameData/Calligraphy/AssetDirectory.cs](src/MHServerEmu.Games/GameData/Calligraphy/AssetDirectory.cs)
  - `GetEnumValue(AssetId)` now guards against `GetAssetType(assetId)` returning `null`.

### Power cost diagnostics

- [src/MHServerEmu/Commands/Implementations/PowerCommands.cs](src/MHServerEmu/Commands/Implementations/PowerCommands.cs)
  - `!power cost` writes a report to `Download\PowerCost_<id>_<timestamp>.txt`.
  - Added PatchManager diagnostics (entry counts, whether a Properties patch exists, and debug info).
  - Cleaned up the report so `EnduranceCostRequired` is printed as a boolean (it is not a curve property).

### Property safety / robustness

- [src/MHServerEmu.Games/Properties/PropertyInfoTable.cs](src/MHServerEmu.Games/Properties/PropertyInfoTable.cs)
  - Hardened property info lookup to avoid crashes when `PropertyEnum.Invalid` or out-of-range enums appear.

- [src/MHServerEmu.Games/Properties/PropertyId.cs](src/MHServerEmu.Games/Properties/PropertyId.cs)
  - `ToString()` made safe when property info lookup fails.

## Deploy steps (Release)

Build:

- `dotnet build MHServerEmu.sln -c Release`

Deploy (copy server exe/dlls to runtime folder, exclude config):

- Copy from `src\MHServerEmu\bin\x64\Release\net8.0\*`
- To `D:\Marvel Heroes Server Emulator\MHServerEmu 0.8.1 Test\MHServerEmu\`
- Excluding `Config*.ini`

## Verification

Run:

- `!power cost 4030192067333526726`

Expected:

- PatchManager reports `HasPropertiesPatch: True`
- Patch preview shows `EnduranceCost10.curve` for `ManaType: Type1`
- Runtime power shows `EnduranceCost CurveId` = `EnduranceCost10.curve` and non-zero final cost
