# Power Rank Handshake Implementation Summary

## What Was Done

I've successfully implemented the infrastructure for a Power Rank handshake system in the v48 branch, based on the existing Talent System handshake pattern.

## Key Accomplishments

### 1. Found the Talent System Handshake
Located the reference implementation in the codebase:
- **Client Message**: `NetMessageEnableTalentPower` (message #151)
- **Server Handler**: `OnEnableTalentPower()` in `PlayerConnection.cs`
- **Validation**: `CanToggleTalentPower()` returns `CanToggleTalentResult`
- **Execution**: `EnableTalentPower()` performs the actual assignment

### 2. Created Power Rank Handshake Following Same Pattern

#### Protocol Definition
- **File**: `proto/ClientToGameServer.proto`
- **Message**: `NetMessageAssignPowerRank` with fields:
  - `avatarId` - Which avatar is requesting the rank
  - `powerProtoId` - Which power to rank up
  - `powerSpec` - Which power spec to use

#### Enum Definitions
- **File**: `src/Gazillion/ProtocolEnums.cs`
- Added `NetMessageAssignPowerRank` to message enum

- **File**: `src/MHServerEmu.Games/Entities/Avatars/AvatarEnums.cs`
- Added `CanAssignPowerRankResult` enum with:
  - Success
  - InCombat
  - LevelRequirement
  - InsufficientPoints
  - MaxRankReached
  - PowerNotInProgression
  - GenericError

#### Server Logic
- **File**: `src/MHServerEmu.Games/Entities/Avatars/Avatar.cs`
- Added `CanAssignPowerRank()` method that validates:
  - Avatar is not in combat
  - Power is in power progression (not talent/mapped)
  - Level requirements are met
  - Power isn't at max rank
  - (TODO: Check available power points)

- Added `AssignPowerRank()` method that:
  - Validates the request
  - Increments `PowerRankBase` by 1
  - Calls `UpdatePowerRank()` to recalculate
  - (TODO: Deduct power points)
  - Logs the assignment

#### Message Handler
- **File**: `src/MHServerEmu.Games/Network/PlayerConnection.cs`
- Added `OnAssignPowerRank()` handler (placeholder)
- Includes commented implementation showing the intended logic
- Validates avatar ownership
- Calls validation and assignment methods

### 3. Documentation
- **File**: `docs/Game/PowerRankHandshake.md`
- Comprehensive documentation explaining:
  - How the talent system works (reference)
  - How the power rank system works
  - Protocol definitions
  - Implementation details
  - Pending work

## What Still Needs to Be Done

### 1. Protobuf Code Generation ⚠️ **REQUIRED**
The protobuf message definition has been added, but the C# class needs to be generated:

```bash
# On Windows with protogen tool installed:
cd src/Tools/Scripts
protogen.bat
```

This will generate the `NetMessageAssignPowerRank` class in `src/Gazillion/ClientToGameServer.cs`.

After generation, uncomment the code in `OnAssignPowerRank()` in `PlayerConnection.cs`.

### 2. Power Points System
Currently missing:
- Tracking available/unspent power points
- Earning power points from leveling
- Deducting power points when ranking up
- Refunding power points on respec

Suggested property: `PropertyEnum.UnspentPowerPoints`

### 3. Client Integration
The Marvel Heroes client needs to:
- Send `NetMessageAssignPowerRank` when player clicks power rank-up button
- Include correct avatarId, powerProtoId, and powerSpec
- This requires client-side modding/configuration

### 4. Testing
Test the complete flow:
1. Start server with changes
2. Connect with client
3. Attempt to rank up a power
4. Verify server receives message
5. Verify validation works (level checks, combat checks, etc.)
6. Verify power rank increases
7. Verify client UI updates

## How The Handshake Works

### Flow Diagram
```
Client                                    Server
  |                                         |
  | NetMessageAssignPowerRank              |
  | (avatarId, powerProtoId, powerSpec)    |
  |-------------------------------------->  |
  |                                         |
  |                          OnAssignPowerRank()
  |                                   validates request
  |                                         |
  |                          CanAssignPowerRank()
  |                            checks conditions
  |                                         |
  |                          AssignPowerRank()
  |                           increments rank
  |                           updates properties
  |                                         |
  |  <-- Power Collection Update Messages <-|
  |     (NetMessagePowerCollectionAssignPower)
  |                                         |
  |  Client UI shows new rank               |
```

### Comparison with Talent System

| Aspect | Talent System | Power Rank System |
|--------|--------------|-------------------|
| **Message** | NetMessageEnableTalentPower | NetMessageAssignPowerRank |
| **Validation** | CanToggleTalentPower() | CanAssignPowerRank() |
| **Execution** | EnableTalentPower() | AssignPowerRank() |
| **Result Enum** | CanToggleTalentResult | CanAssignPowerRankResult |
| **Property Changed** | AvatarSpecializationPower | PowerRankBase |
| **Toggle On/Off** | Yes (enable parameter) | No (always increment) |
| **Combat Check** | Yes | Yes |
| **Level Check** | Yes | Yes |
| **Points Check** | No | Yes (TODO) |

## Files Changed

1. `proto/ClientToGameServer.proto` - Protocol definition
2. `src/Gazillion/ProtocolEnums.cs` - Message enum
3. `src/MHServerEmu.Games/Entities/Avatars/AvatarEnums.cs` - Result enum
4. `src/MHServerEmu.Games/Entities/Avatars/Avatar.cs` - Validation & assignment
5. `src/MHServerEmu.Games/Network/PlayerConnection.cs` - Message handler
6. `docs/Game/PowerRankHandshake.md` - Documentation

## Build Status

✅ **SUCCESS**: The MHServerEmu.Games project builds successfully with all changes.
- No compilation errors introduced
- Only pre-existing warnings from protobuf-generated code
- Ready for protobuf code generation step

## Next Steps for User

1. **Generate Protobuf Code** (Windows required):
   ```bash
   cd src/Tools/Scripts
   protogen.bat
   ```

2. **Uncomment Handler Code** in `PlayerConnection.cs` line ~2283

3. **Implement Power Points System**:
   - Track unspent points in avatar properties
   - Check points in `CanAssignPowerRank()`
   - Deduct points in `AssignPowerRank()`

4. **Test with Client**:
   - Configure client to send the message
   - Verify the handshake works end-to-end

## Additional Notes

- The implementation follows the exact same pattern as the talent system
- All validation is server-side for security
- The system is designed to be extended with additional checks
- Power points tracking can be added without changing the protocol
- The handshake is synchronous - client sends request, server validates and executes

## Support

For questions or issues:
1. Review `docs/Game/PowerRankHandshake.md` for detailed explanation
2. Compare with talent system implementation in `Avatar.cs` lines 2270-2422
3. Check existing power rank code in `Agent.cs` lines 858-1193

---

**Implementation Date**: January 14, 2026  
**Branch**: copilot/find-handshake-process-v48  
**Status**: Infrastructure Complete, Awaiting Protobuf Generation
