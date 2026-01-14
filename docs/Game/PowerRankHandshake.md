# Power Rank Handshake System

## Overview

The Power Rank Handshake system is modeled after the Talent System handshake, providing a client-server communication protocol for assigning power ranks (leveling up powers) in Marvel Heroes.

## How It Works

### Talent System Pattern (Reference)

The talent system follows this flow:

1. **Client Request**: Client sends `NetMessageEnableTalentPower` with:
   - `avatarId`: ID of the avatar
   - `prototypeId`: ID of the talent power
   - `enable`: Whether to enable or disable
   - `spec`: Which power spec this applies to

2. **Server Validation**: Server calls `CanToggleTalentPower()` which checks:
   - Avatar is not in combat
   - Avatar meets level requirements
   - No restrictive conditions prevent toggling
   - Returns `CanToggleTalentResult`

3. **Server Execution**: If validation passes, server calls `EnableTalentPower()` which:
   - Assigns or unassigns the talent power
   - Updates the avatar's power collection
   - Sends response to client

### Power Rank System (New Implementation)

The power rank system follows the same pattern:

1. **Client Request**: Client sends `NetMessageAssignPowerRank` with:
   - `avatarId`: ID of the avatar
   - `powerProtoId`: ID of the power to rank up
   - `powerSpec`: Which power spec this applies to

2. **Server Validation**: Server calls `CanAssignPowerRank()` which checks:
   - Avatar is not in combat
   - Power is in power progression (not a talent or mapped power)
   - Avatar meets level requirements
   - Power is not already at maximum rank
   - Player has unspent power points (TODO)
   - Returns `CanAssignPowerRankResult`

3. **Server Execution**: If validation passes, server calls `AssignPowerRank()` which:
   - Increments the power's `PowerRankBase` property by 1
   - Calls `UpdatePowerRank()` to trigger recalculation of `PowerRankCurrentBest`
   - Deducts power points from player (TODO)
   - Updates the avatar's power collection
   - Sends confirmation to client via power collection updates

## Protocol Definition

### Protobuf Message (ClientToGameServer.proto)

```protobuf
message NetMessageAssignPowerRank {
    required uint64 avatarId     = 1;
    required uint64 powerProtoId = 2;
    required uint32 powerSpec    = 3;
}
```

### Message Handler (PlayerConnection.cs)

```csharp
private bool OnAssignPowerRank(MailboxMessage message)
{
    var assignPowerRank = message.As<NetMessageAssignPowerRank>();
    if (assignPowerRank == null) 
        return Logger.WarnReturn(false, $"OnAssignPowerRank(): Failed to retrieve message");

    Avatar avatar = Game.EntityManager.GetEntity<Avatar>(assignPowerRank.AvatarId);
    if (avatar == null) 
        return Logger.WarnReturn(false, "OnAssignPowerRank(): avatar == null");

    Player owner = avatar.GetOwnerOfType<Player>();
    if (owner != Player)
        return Logger.WarnReturn(false, $"OnAssignPowerRank(): Player [{Player}] is attempting to assign power rank for avatar [{avatar}] that belongs to another player");

    PrototypeId powerProtoRef = (PrototypeId)assignPowerRank.PowerProtoId;
    int specIndex = (int)assignPowerRank.PowerSpec;

    if (avatar.CanAssignPowerRank(powerProtoRef, specIndex) != CanAssignPowerRankResult.Success)
        return false;

    avatar.AssignPowerRank(powerProtoRef, specIndex);
    return true;
}
```

## Validation Results

The `CanAssignPowerRankResult` enum provides detailed feedback:

- **Success**: Power rank can be assigned
- **InCombat**: Avatar is currently in combat
- **LevelRequirement**: Avatar doesn't meet the level requirement
- **InsufficientPoints**: Player doesn't have enough unspent power points
- **MaxRankReached**: Power is already at maximum rank for current level
- **PowerNotInProgression**: Power is not in the progression system (e.g., it's a talent or mapped power)
- **GenericError**: Generic error occurred

## Implementation Status

### ✅ Completed

- Protobuf message definition in `ClientToGameServer.proto`
- Enum entry in `ProtocolEnums.cs`
- `CanAssignPowerRankResult` enum in `AvatarEnums.cs`
- `CanAssignPowerRank()` validation method in `Avatar.cs`
- `AssignPowerRank()` execution method in `Avatar.cs`
- Message handler structure in `PlayerConnection.cs`

### ⏳ Pending

1. **Protobuf Code Generation**: The C# classes need to be generated from the `.proto` files using protobuf compiler (protogen or protoc). This is typically done on Windows with the protobuf-net tooling.

2. **Power Points System**: Need to implement tracking of available/unspent power points:
   - Add property to track unspent power points
   - Calculate power points earned from leveling
   - Deduct power points when assigning ranks
   - Handle power point refunds on respec

3. **Client Integration**: The Marvel Heroes client needs to be configured to send the `NetMessageAssignPowerRank` message when the player clicks to rank up a power.

4. **Testing**: Need to test with an actual game client to ensure the handshake works correctly.

## Code Files Modified

- `proto/ClientToGameServer.proto` - Added message definition
- `src/Gazillion/ProtocolEnums.cs` - Added enum entry
- `src/Gazillion/ClientToGameServer.cs` - Needs protobuf class generation
- `src/MHServerEmu.Games/Entities/Avatars/AvatarEnums.cs` - Added result enum
- `src/MHServerEmu.Games/Entities/Avatars/Avatar.cs` - Added validation and assignment methods
- `src/MHServerEmu.Games/Network/PlayerConnection.cs` - Added message handler

## Future Enhancements

1. **Power Point Management**: Implement a comprehensive power point system with earning, spending, and refunding
2. **Respec Support**: Handle power rank resets when player respecs
3. **UI Feedback**: Send appropriate error messages to client for each validation failure
4. **Logging**: Add comprehensive logging for debugging
5. **Metrics**: Track power rank assignments for analytics

## Related Systems

- **Talent System**: The reference implementation for this handshake pattern
- **Power Progression**: The underlying system that tracks power ranks
- **Power Collection**: Where active powers and their ranks are stored
- **Respec System**: Needs to reset power ranks appropriately

## References

- Talent system implementation: `Avatar.cs` lines 2270-2422
- Power rank calculation: `Agent.cs` lines 858-1193  
- Power progression info: `PowerProgressionInfo.cs`
