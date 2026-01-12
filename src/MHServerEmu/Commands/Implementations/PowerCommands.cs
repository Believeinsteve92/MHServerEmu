using System.Text;
using Gazillion;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Common;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.GameData.PatchManager;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("power")]
    [CommandGroupDescription("Commands related to the power system.")]
    [CommandGroupUserLevel(AccountUserLevel.Admin)]
    public class PowerCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        [Command("print")]
        [CommandDescription("Prints the power collection for the current avatar to the console.")]
        [CommandUsage("power print")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Print(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Avatar avatar = playerConnection.Player.CurrentAvatar;

            StringBuilder sb = new();
            sb.AppendLine($"------ Power Collection for Avatar {avatar} ------");
            foreach (var record in avatar.PowerCollection)
                sb.AppendLine(record.Value.ToString());
            sb.AppendLine($"Total Powers: {avatar.PowerCollection.PowerCount}");

            AdminCommandManager.SendAdminCommandResponseSplit(playerConnection, sb.ToString());
            return "Power collection information printed to the console.";
        }

        [Command("cooldownreset")]
        [CommandDescription("Resets all cooldowns and charges.")]
        [CommandUsage("power cooldownreset")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string CooldownReset(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;

            // Player cooldowns
            Player player = playerConnection.Player;
            foreach (PropertyEnum cooldownProperty in Property.CooldownProperties)
                player.Properties.RemovePropertyRange(cooldownProperty);

            // Avatar cooldowns
            Avatar avatar = player.CurrentAvatar;
            foreach (PropertyEnum cooldownProperty in Property.CooldownProperties)
                avatar.Properties.RemovePropertyRange(cooldownProperty);

            // Avatar charges
            Dictionary<PropertyId, PropertyValue> setDict = DictionaryPool<PropertyId, PropertyValue>.Instance.Get();
            foreach (var kvp in avatar.Properties.IteratePropertyRange(PropertyEnum.PowerChargesMax))
            {
                Property.FromParam(kvp.Key, 0, out PrototypeId powerProtoRef);
                if (powerProtoRef == PrototypeId.Invalid)
                    continue;

                setDict[new(PropertyEnum.PowerChargesAvailable, powerProtoRef)] = kvp.Value;
            }

            foreach (var kvp in setDict)
                avatar.Properties[kvp.Key] = kvp.Value;

            DictionaryPool<PropertyId, PropertyValue>.Instance.Return(setDict);

            return $"All cooldowns and charges have been reset.";
        }

        [Command("stealpowers")]
        [CommandDescription("Unlocks all stolen powers.")]
        [CommandUsage("power stealpowers")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string StealPowers(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Avatar avatar = playerConnection.Player.CurrentAvatar;

            AvatarPrototype avatarProto = avatar.AvatarPrototype;
            if (avatarProto.StealablePowersAllowed.IsNullOrEmpty())
                return "No stealable powers available for the current avatar.";

            int count = 0;
            foreach (PrototypeId stealablePowerInfoRef in avatarProto.StealablePowersAllowed)
            {
                StealablePowerInfoPrototype stealablePowerInfoProto = stealablePowerInfoRef.As<StealablePowerInfoPrototype>();
                PrototypeId stolenPowerRef = stealablePowerInfoProto.Power;

                if (avatar.IsStolenPowerAvailable(stolenPowerRef))
                    continue;

                avatar.Properties[PropertyEnum.StolenPowerAvailable, stolenPowerRef] = true;
                count++;
            }

            if (count == 0)
                return "All stolen powers are already unlocked for the current avatar.";

            return $"Unlocked {count} stolen powers.";
        }

        [Command("stealavatarpowers")]
        [CommandDescription("Unlocks avatar stolen powers.")]
        [CommandUsage("power stealavatarpowers")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string StealAvatarPowers(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Avatar avatar = playerConnection.Player.CurrentAvatar;

            AvatarPrototype currentAvatarProto = avatar.AvatarPrototype;

            int count = 0;
            foreach (PrototypeId avatarProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<AvatarPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                AvatarPrototype avatarProto = avatarProtoRef.As<AvatarPrototype>();

                // e.g. Vision/Ultron don't have valid stealable powers
                if (currentAvatarProto.StealablePowersAllowed.Contains(avatarProto.StealablePower) == false)
                    continue;

                StealablePowerInfoPrototype stealablePowerInfoProto = avatarProto.StealablePower.As<StealablePowerInfoPrototype>();
                if (stealablePowerInfoProto == null)
                    continue;

                PrototypeId stolenPowerRef = stealablePowerInfoProto.Power;

                if (avatar.IsStolenPowerAvailable(stolenPowerRef))
                    continue;

                avatar.Properties[PropertyEnum.StolenPowerAvailable, stolenPowerRef] = true;
                count++;
            }

            if (count == 0)
                return "All avatar stolen powers are already unlocked for the current avatar.";

            return $"Unlocked {count} stolen powers.";
        }

        [Command("forgetstolenpowers")]
        [CommandDescription("Locks all unlocked stolen powers.")]
        [CommandUsage("power forgetstolenpowers")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ForgetStolenPowers(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Avatar avatar = playerConnection.Player.CurrentAvatar;

            AvatarPrototype avatarProto = avatar.AvatarPrototype;
            if (avatarProto.StealablePowersAllowed.IsNullOrEmpty())
                return "No stealable powers available for the current avatar.";

            int count = 0;
            foreach (PrototypeId stealablePowerInfoRef in avatarProto.StealablePowersAllowed)
            {
                StealablePowerInfoPrototype stealablePowerInfoProto = stealablePowerInfoRef.As<StealablePowerInfoPrototype>();
                PrototypeId stolenPowerRef = stealablePowerInfoProto.Power;

                if (avatar.IsStolenPowerAvailable(stolenPowerRef) == false)
                    continue;

                avatar.Properties.RemoveProperty(new(PropertyEnum.StolenPowerAvailable, stolenPowerRef));
                if (avatar.HasMappedPower(stolenPowerRef))
                    avatar.UnassignMappedPower(stolenPowerRef);

                count++;
            }

            if (count == 0)
                return "No stolen powers are currently unlocked for the current avatar.";

            return $"Forgotten {count} stolen powers.";
        }

        [Command("cost")]
        [CommandDescription("Downloads endurance (spirit) and secondary-resource cost debug information for a specific power prototype id on the current avatar.")]
        [CommandUsage("power cost <powerPrototypeId>")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Cost(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Avatar avatar = playerConnection.Player.CurrentAvatar;

            if (@params.Length < 1 || ulong.TryParse(@params[0], out ulong rawProtoId) == false)
                return "Usage: power cost <powerPrototypeId>";

            PrototypeId protoId = (PrototypeId)rawProtoId;

            Power power = avatar.GetPower(protoId);
            if (power == null)
                return $"Power not found on current avatar: {rawProtoId}";

            PowerPrototype powerProto = power.Prototype;
            if (powerProto == null)
                return $"Power prototype not loaded: {rawProtoId}";

            PrototypeId protoIdFromName = PrototypeId.Invalid;
            try
            {
                protoIdFromName = GameDatabase.GetPrototypeRefByName(powerProto.ToString());
            }
            catch
            {
                // Best-effort only.
            }

            bool hasPatchEntriesForDataRef = PrototypePatchManager.Instance.TryGetPatchEntryCount(powerProto.DataRef, out int patchEntryCountForDataRef);
            bool hasPatchEntriesForNameRef = PrototypePatchManager.Instance.TryGetPatchEntryCount(protoIdFromName, out int patchEntryCountForNameRef);

            bool hasPatchProperties = PrototypePatchManager.Instance.CheckProperties(powerProto.DataRef, out PropertyCollection patchProperties);
            bool hasPatchPropertiesDebug = PrototypePatchManager.Instance.TryGetPropertiesPatchDebug(powerProto.DataRef, out PropertyCollection patchPropertiesDebug, out string patchPropertiesDebugText);

            string fileRelativePath = $"Download/PowerCost_{(ulong)powerProto.DataRef}_{DateTime.UtcNow.ToString(FileHelper.FileNameDateFormat)}.txt";
            string fileFullPath = Path.Combine(FileHelper.ServerRoot, fileRelativePath);

            try
            {
                // Create folder up-front so we can tell whether this command executed at all.
                Directory.CreateDirectory(Path.GetDirectoryName(fileFullPath)!);
            }
            catch (Exception dirEx)
            {
                Logger.Warn($"power cost: failed to create download directory for '{fileFullPath}': {dirEx.Message}");
            }

            Logger.Info($"power cost invoked for power={(ulong)powerProto.DataRef}, report='{fileRelativePath}', client={playerConnection}");

            StringBuilder sb = new();
            bool hadException = false;

            try
            {
                sb.AppendLine($"------ Power Cost Debug: {powerProto} ({(ulong)powerProto.DataRef}) ------");
                sb.AppendLine($"PowerType: {powerProto.GetType().Name}");
                sb.AppendLine($"LiveTuning PowerCost Mult: {LiveTuningManager.GetLivePowerTuningVar(powerProto, PowerTuningVar.ePTV_PowerCost)}");

                sb.AppendLine($"PatchManager Debug: DataRef={(ulong)powerProto.DataRef}, NameRef={(ulong)protoIdFromName}, Equal={powerProto.DataRef == protoIdFromName}");
                sb.AppendLine($"PatchManager Debug: EntryCount(DataRef)={(hasPatchEntriesForDataRef ? patchEntryCountForDataRef : 0)}");
                if (protoIdFromName != PrototypeId.Invalid && protoIdFromName != powerProto.DataRef)
                    sb.AppendLine($"PatchManager Debug: EntryCount(NameRef)={(hasPatchEntriesForNameRef ? patchEntryCountForNameRef : 0)}");

                sb.AppendLine($"PatchManager HasPropertiesPatch: {hasPatchProperties}");
                sb.AppendLine($"PatchManager Properties Debug: {hasPatchPropertiesDebug} ({patchPropertiesDebugText})");

                bool canPreviewPatchProperties = hasPatchProperties;

                // If CheckProperties() failed but debug succeeded, use the debug materialized properties for preview.
                if (canPreviewPatchProperties == false && hasPatchPropertiesDebug && patchProperties == null)
                {
                    patchProperties = patchPropertiesDebug;
                    canPreviewPatchProperties = patchProperties != null;
                }

                if (canPreviewPatchProperties)
                {
                    // Show what the patch system *thinks* it is applying, for quick diagnosis.
                    // Note: EnduranceCost is param'd by mana type. Many patches fail simply because the param0 is wrong.
                    sb.AppendLine("PatchManager Properties Preview:");

                    HashSet<ManaType> manaTypes = new();
                    foreach (PrimaryResourceManaBehaviorPrototype behaviorProto in avatar.GetPrimaryResourceManaBehaviors())
                        manaTypes.Add(behaviorProto.ManaType);

                    // Also include 0 as a sanity check (common mistake is using 0 when you intended Type1).
                    manaTypes.Add((ManaType)0);

                    foreach (ManaType mt in manaTypes)
                    {
                        PropertyId patchEnduranceCostId = new(PropertyEnum.EnduranceCost, (PropertyParam)mt);
                        CurveId patchEnduranceCostCurveId = patchProperties.GetCurveIdForCurveProperty(patchEnduranceCostId);
                        string patchEnduranceCostCurveName = GameDatabase.GetCurveName(patchEnduranceCostCurveId);
                        PropertyId patchEnduranceCostIndexId = patchProperties.GetIndexPropertyIdForCurveProperty(patchEnduranceCostId);
                        bool patchEnduranceCostRequired = patchProperties[PropertyEnum.EnduranceCostRequired];

                        sb.AppendLine($"  ManaType: {mt}");
                        sb.AppendLine($"    EnduranceCost PatchCurveId: {(ulong)patchEnduranceCostCurveId} ({patchEnduranceCostCurveName}), IndexProp: {patchEnduranceCostIndexId}");
                        sb.AppendLine($"    EnduranceCostRequired (bool): {patchEnduranceCostRequired}");
                    }
                }

                // Secondary resource cost
                float secondaryResourceCost = power.GetSecondaryResourceCost();
                sb.AppendLine($"SecondaryResourceCost: {secondaryResourceCost}");

                // Endurance (spirit/primary resources)
                foreach (PrimaryResourceManaBehaviorPrototype primaryManaBehaviorProto in avatar.GetPrimaryResourceManaBehaviors())
                {
                    ManaType manaType = primaryManaBehaviorProto.ManaType;

                    float currentEndurance = avatar.Properties[PropertyEnum.Endurance, manaType];
                    bool noEnduranceCosts = avatar.Properties[PropertyEnum.NoEnduranceCosts, manaType];

                    float baseCost = power.Properties[PropertyEnum.EnduranceCost, manaType];
                    float finalCost = power.GetEnduranceCost(manaType, true);

                    PropertyId enduranceCostId = new(PropertyEnum.EnduranceCost, (PropertyParam)manaType);
                    CurveId enduranceCostCurveId = power.Properties.GetCurveIdForCurveProperty(enduranceCostId);
                    string enduranceCostCurveName = GameDatabase.GetCurveName(enduranceCostCurveId);
                    PropertyId enduranceCostIndexId = power.Properties.GetIndexPropertyIdForCurveProperty(enduranceCostId);

                    PropertyId enduranceRequiredId = new(PropertyEnum.EnduranceCostRequired, (PropertyParam)manaType);
                    bool enduranceCostRequired = power.Properties[PropertyEnum.EnduranceCostRequired];

                    float baseRecurring = power.GetEnduranceCostRecurring(manaType, applyModifiers: false, canSkipCost: false);
                    float finalRecurring = power.GetEnduranceCostRecurring(manaType, applyModifiers: true, canSkipCost: true);

                    float overridePct = avatar.Properties[PropertyEnum.EnduranceCostChangePctOverride, manaType];
                    float overrideAllPct = avatar.Properties[PropertyEnum.EnduranceCostChangePctOverride, ManaType.TypeAll];

                    sb.AppendLine($"ManaType: {manaType}");
                    sb.AppendLine($"  EnduranceNow: {currentEndurance}");
                    sb.AppendLine($"  NoEnduranceCosts: {noEnduranceCosts}");
                    sb.AppendLine($"  EnduranceCost Base: {baseCost}");
                    sb.AppendLine($"  EnduranceCost CurveId: {(ulong)enduranceCostCurveId} ({enduranceCostCurveName})");
                    sb.AppendLine($"  EnduranceCost CurveIndexProp: {enduranceCostIndexId}");
                    sb.AppendLine($"  EnduranceCost Final: {finalCost}");
                    sb.AppendLine($"  EnduranceCostRequired (bool): {enduranceCostRequired}");
                    sb.AppendLine($"  RecurringCost Base: {baseRecurring}");
                    sb.AppendLine($"  RecurringCost Final: {finalRecurring}");
                    sb.AppendLine($"  EnduranceCostChangePctOverride[{manaType}]: {overridePct}");
                    sb.AppendLine($"  EnduranceCostChangePctOverride[TypeAll]: {overrideAllPct}");
                }
            }
            catch (Exception ex)
            {
                hadException = true;
                Logger.ErrorException(ex, "power cost: exception while building report");
                sb.AppendLine();
                sb.AppendLine("!!!!! EXCEPTION !!!!!");
                sb.AppendLine(ex.ToString());
            }

            try
            {
                File.WriteAllText(fileFullPath, sb.ToString());
            }
            catch (Exception ioEx)
            {
                Logger.ErrorException(ioEx, $"power cost: failed to write report '{fileFullPath}'");
                return $"power cost failed to write report file. See server logs. (Target: {fileRelativePath})";
            }

            return hadException
                ? $"Power cost report saved to {fileRelativePath} (exception occurred; see report/server logs)"
                : $"Power cost report saved to {fileRelativePath}";
        }
    }
}
