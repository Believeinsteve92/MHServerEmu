using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MHServerEmu.Games.GameData.PatchManager
{
    public class PrototypePatchEntry
    {
        public bool Enabled { get; }
        public string Prototype { get; }
        public string Path { get; }
        public string Description { get; }
        public ValueBase Value { get; }

        [JsonIgnore]
        public string СlearPath { get; }
        [JsonIgnore]
        public string FieldName { get; }
        [JsonIgnore]
        public bool ArrayValue { get; }
        [JsonIgnore]
        public int ArrayIndex { get; }
        [JsonIgnore]
        public bool Patched { get; set; }

        [JsonConstructor]
        public PrototypePatchEntry(bool enabled, string prototype, string path, string description, ValueBase value)
        {
            Enabled = enabled;
            Prototype = prototype;
            Path = path;
            Description = description;
            Value = value;

            int lastDotIndex = path.LastIndexOf('.');
            if (lastDotIndex == -1)
            {
                СlearPath = string.Empty;
                FieldName = path;
            }
            else
            {
                СlearPath = path[..lastDotIndex];
                FieldName = path[(lastDotIndex + 1)..];
            }

            ArrayIndex = -1;
            ArrayValue = false;
            int index = FieldName.LastIndexOf('[');
            if (index != -1)
            {
                ArrayValue = true;

                int endIndex = FieldName.LastIndexOf(']');
                if (endIndex > index)
                {
                    string indexStr = FieldName.Substring(index + 1, endIndex - index - 1);
                    if (int.TryParse(indexStr, out int parsedIndex))
                        ArrayIndex = parsedIndex;
                }

                FieldName = FieldName[..index];
            }

            Patched = false;
        }
    }

    public class PatchEntryConverter : JsonConverter<PrototypePatchEntry>
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        public override PrototypePatchEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            string valueTypeString = root.GetProperty("ValueType").GetString() ?? string.Empty;
            valueTypeString = valueTypeString.Replace("[]", "Array");
            var valueType = Enum.Parse<ValueType>(valueTypeString);
            var entry = new PrototypePatchEntry
            (
                root.GetProperty("Enabled").GetBoolean(),
                root.GetProperty("Prototype").GetString() ?? string.Empty,
                root.GetProperty("Path").GetString() ?? string.Empty,
                root.GetProperty("Description").GetString() ?? string.Empty,
                GetValueBase(root.GetProperty("Value"), valueType)
            );

            if (valueType == ValueType.Properties) entry.Patched = true;

            return entry;
        }

        public static ValueBase GetValueBase(JsonElement jsonElement, ValueType valueType)
        {
            return valueType switch
            {
                ValueType.String => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.Boolean => new SimpleValue<bool>(jsonElement.GetBoolean(), valueType),
                ValueType.Float => new SimpleValue<float>(jsonElement.GetSingle(), valueType),
                ValueType.Integer => new SimpleValue<int>(jsonElement.GetInt32(), valueType),
                ValueType.Enum => new SimpleValue<string>(jsonElement.GetString(), valueType),
                ValueType.PrototypeGuid => new SimpleValue<PrototypeGuid>((PrototypeGuid)jsonElement.GetUInt64(), valueType),
                ValueType.PrototypeId or 
                ValueType.PrototypeDataRef => new SimpleValue<PrototypeId>((PrototypeId)jsonElement.GetUInt64(), valueType),
                ValueType.LocaleStringId => new SimpleValue<LocaleStringId>((LocaleStringId)jsonElement.GetUInt64(), valueType),
                ValueType.PrototypeIdArray or
                ValueType.PrototypeDataRefArray => new ArrayValue<PrototypeId>(jsonElement, valueType, x => (PrototypeId)x.GetUInt64()),
                ValueType.Prototype => new SimpleValue<Prototype>(ParseJsonPrototype(jsonElement), valueType),
                ValueType.PrototypeArray => new ArrayValue<Prototype>(jsonElement, valueType, ParseJsonPrototype),
                ValueType.Vector3 => new SimpleValue<Vector3>(ParseJsonVector3(jsonElement), valueType),
                // Parsing properties requires GameDatabase.PropertyInfoTable + curve refs to be initialized.
                // PatchManager loads PatchData very early during startup, so we store raw JSON and parse on demand.
                ValueType.Properties => new RawJsonPropertiesValue(jsonElement.GetRawText()),
                _ => throw new NotSupportedException($"Type {valueType} not support.")
            };
        }

        private static Vector3 ParseJsonVector3(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length != 3) 
                throw new InvalidOperationException("Json element is not Vector3");

            return new Vector3(jsonArray[0].GetSingle(), jsonArray[1].GetSingle(), jsonArray[2].GetSingle());
        }

        public static Prototype ParseJsonPrototype(JsonElement jsonElement)
        {

            var referenceType = (PrototypeId)jsonElement.GetProperty("ParentDataRef").GetUInt64();
            Type classType = GameDatabase.DataDirectory.GetPrototypeClassType(referenceType);
            var prototype = GameDatabase.PrototypeClassManager.AllocatePrototype(classType);

            CalligraphySerializer.CopyPrototypeDataRefFields(prototype, referenceType);
            prototype.ParentDataRef = referenceType;

            foreach (var property in jsonElement.EnumerateObject())
            {
                if (property.Name == "ParentDataRef") continue;
                var fieldInfo = prototype.GetType().GetProperty(property.Name);
                if (fieldInfo == null) continue;
                Type fieldType = fieldInfo.PropertyType;
                var element = ParseJsonElement(property.Value, fieldType);
                try
                {
                    object convertedValue = PrototypePatchManager.ConvertValue(element, fieldType);
                    fieldInfo.SetValue(prototype, convertedValue);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, $"ParseJsonPrototype can't convert {element} in {fieldType.Name}");
                }

            }

            return prototype;
        }

        public static PropertyCollection ParseJsonProperties(JsonElement jsonElement)
        {
            PropertyCollection properties = new ();
            var infoTable = GameDatabase.PropertyInfoTable;

            if (infoTable == null)
                throw new InvalidOperationException("ParseJsonProperties(): GameDatabase.PropertyInfoTable is null (PatchData loaded too early)");

            foreach (var property in jsonElement.EnumerateObject())
            {
                var propEnum = (PropertyEnum)Enum.Parse(typeof(PropertyEnum), property.Name);
                PropertyInfo propertyInfo = infoTable.LookupPropertyInfo(propEnum);

                if (propertyInfo == null)
                {
                    Logger.Warn($"ParseJsonProperties(): Property info not found for {propEnum}, skipping");
                    continue;
                }

                PropertyId propId = ParseJsonPropertyId(property.Value, propEnum, propertyInfo);

                // Curve properties (e.g. EnduranceCost) are stored separately as CurveProperty values and
                // need to be set using SetCurveProperty so the CurveId + index property are retained.
                if (propertyInfo.IsCurveProperty)
                {
                    CurveId curveId = ParseJsonCurveId(property.Value, propertyInfo);
                    PropertyId indexPropertyId = propertyInfo.DefaultCurveIndex;

                    if (curveId == CurveId.Invalid)
                        curveId = (CurveId)propertyInfo.DefaultValue;

                    properties.SetCurveProperty(propId, curveId, indexPropertyId, propertyInfo, SetPropertyFlags.None, true);
                    continue;
                }

                PropertyValue propValue = ParseJsonPropertyValue(property.Value, propertyInfo);
                properties.SetProperty(propValue, propId);
            }

            return properties;
        }

        private static CurveId ParseJsonCurveId(JsonElement jsonElement, PropertyInfo propInfo)
        {
            // For properties with params, the JSON is expected to be: [param0, param1, ..., <curve>]
            if (propInfo.ParamCount > 0)
            {
                var jsonArray = jsonElement.EnumerateArray().ToArray();
                if (jsonArray.Length > 0)
                    jsonElement = jsonArray[^1];
            }

            // Accept either a numeric CurveId (ulong) or a curve name string.
            if (jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetUInt64(out ulong curveUlong))
                return (CurveId)curveUlong;

            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                string curveName = jsonElement.GetString();
                if (string.IsNullOrWhiteSpace(curveName) == false)
                {
                    // Some editors/users prefer quoting 64-bit ids to avoid precision issues.
                    if (ulong.TryParse(curveName, out ulong curveUlongFromString))
                        return (CurveId)curveUlongFromString;

                    return GameDatabase.CurveRefManager.GetDataRefByName(curveName);
                }
            }

            return CurveId.Invalid;
        }

        public static PropertyId ParseJsonPropertyId(JsonElement jsonElement, PropertyEnum propEnum, PropertyInfo propInfo)
        {
            int paramCount = propInfo.ParamCount;
            if (paramCount == 0) return new(propEnum);

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            Span<PropertyParam> paramValues = stackalloc PropertyParam[Property.MaxParamCount];
            propInfo.DefaultParamValues.CopyTo(paramValues);

            for (int i = 0; i < paramCount; i++)
            {
                if (i >= 4) break;
                if (i >= jsonArray.Length) continue;

                var paramValue = jsonArray[i];

                switch (propInfo.GetParamType(i))
                {
                    case PropertyParamType.Asset:
                        // PatchData authors often use small ints for enum-like asset params (e.g. ManaType Type1 = 1).
                        // Prefer interpreting small numeric values as enum values directly; otherwise treat as an AssetId.
                        if (paramValue.ValueKind == JsonValueKind.Number && paramValue.TryGetUInt64(out ulong rawUlong))
                        {
                            if (rawUlong <= int.MaxValue)
                                paramValues[i] = (PropertyParam)(int)rawUlong;
                            else
                                paramValues[i] = Property.ToParam((AssetId)rawUlong);
                        }
                        else if (paramValue.ValueKind == JsonValueKind.String)
                        {
                            string rawString = paramValue.GetString();
                            if (int.TryParse(rawString, out int rawInt))
                                paramValues[i] = (PropertyParam)rawInt;
                            else if (ulong.TryParse(rawString, out ulong rawUlongFromString))
                            {
                                if (rawUlongFromString <= int.MaxValue)
                                    paramValues[i] = (PropertyParam)(int)rawUlongFromString;
                                else
                                    paramValues[i] = Property.ToParam((AssetId)rawUlongFromString);
                            }
                        }
                        break;

                    case PropertyParamType.Prototype:
                        var protoRefParam = (PrototypeId)ParseJsonElement(paramValue, typeof(PrototypeId));
                        paramValues[i] = Property.ToParam(propEnum, i, protoRefParam);
                        break;

                    case PropertyParamType.Integer:
                        if (paramValue.TryGetInt64(out long decimalValue))
                            paramValues[i] = (PropertyParam)(int)decimalValue;
                        break;

                    default:
                        throw new InvalidOperationException("Encountered an unknown prop param type in an ParseJsonPropertyId!");
                }
            }

            return new(propEnum, paramValues);
        }

        public static PropertyValue ParseJsonPropertyValue(JsonElement jsonElement, PropertyInfo propInfo)
        {
            if (propInfo.ParamCount > 0)
            {
                var jsonArray = jsonElement.EnumerateArray().ToArray();
                jsonElement = jsonArray[^1];
            }

            switch (propInfo.DataType)
            {
                case PropertyDataType.Integer:
                    if (jsonElement.TryGetInt64(out long decimalValue))
                        return (PropertyValue)decimalValue;
                    break;

                case PropertyDataType.Real:
                    if (jsonElement.TryGetDouble(out double doubleValue))
                        return (PropertyValue)(float)doubleValue;
                    break;

                case PropertyDataType.Boolean:
                    return (PropertyValue)jsonElement.GetBoolean();

                case PropertyDataType.Prototype:
                    var protoRefValue = (PrototypeId)ParseJsonElement(jsonElement, typeof(PrototypeId));
                    return (PropertyValue)protoRefValue;

                case PropertyDataType.Asset:
                    AssetId assetValue = (AssetId)ParseJsonElement(jsonElement, typeof(AssetId));
                    return (PropertyValue)assetValue;

                // Curve properties are handled in ParseJsonProperties() via SetCurveProperty.
                case PropertyDataType.Curve:
                    throw new InvalidOperationException($"[ParseJsonPropertyValue] Curve properties must be set via SetCurveProperty. Property: {propInfo.PropertyName}");

                default:
                    throw new InvalidOperationException($"[ParseJsonPropertyValue] Assignment into invalid property (property type is not int/float/bool)! Property: {propInfo.PropertyName}");
            }

            return propInfo.DefaultValue;
        }

        public static object ParseJsonElement(JsonElement value, Type fieldType)
        {
            if (fieldType == typeof(PrototypeId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (PrototypeId)ulongValue;
            }

            if (fieldType == typeof(AssetId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (AssetId)ulongValue;
            }

            if (fieldType == typeof(PrototypeGuid))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (PrototypeGuid)ulongValue;
            }

            if (fieldType == typeof(LocaleStringId))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong ulongValue))
                    return (LocaleStringId)ulongValue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Number:
                    if (value.TryGetUInt64(out ulong ulongValue))
                        return ulongValue;
                    else if (value.TryGetInt64(out long decimalValue))
                        return decimalValue;
                    else if (value.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    else
                        return value.GetRawText();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetBoolean();
                default:
                    return value.ToString();
            }
        }

        public override void Write(Utf8JsonWriter writer, PrototypePatchEntry value, JsonSerializerOptions options)
        {
            throw new NotImplementedException(); 
        }
    }

    public enum ValueType
    {
        String,
        Boolean,
        Float,
        Integer,
        Enum,
        PrototypeGuid,
        PrototypeId,
        PrototypeIdArray,
        LocaleStringId,
        PrototypeDataRef,
        PrototypeDataRefArray,
        Prototype,
        PrototypeArray,
        Vector3,
        Properties
    }

    public abstract class ValueBase
    {
        public abstract ValueType ValueType { get; }
        public abstract object GetValue();
    }

    public sealed class RawJsonPropertiesValue : ValueBase
    {
        public override ValueType ValueType => ValueType.Properties;

        public string RawJson { get; }

        private PropertyCollection _parsed;
        private bool _hasParsed;

        public RawJsonPropertiesValue(string rawJson)
        {
            RawJson = rawJson ?? "{}";
        }

        public override object GetValue()
        {
            if (_hasParsed)
                return _parsed;

            if (GameDatabase.PropertyInfoTable == null)
                throw new InvalidOperationException("RawJsonPropertiesValue.GetValue(): PropertyInfoTable is not initialized yet");

            using JsonDocument doc = JsonDocument.Parse(RawJson);
            _parsed = PatchEntryConverter.ParseJsonProperties(doc.RootElement);
            _hasParsed = true;
            return _parsed;
        }
    }

    public class SimpleValue<T> : ValueBase
    {
        public override ValueType ValueType { get; }
        public T Value { get; }

        public SimpleValue(T value, ValueType valueType)
        {
            Value = value;
            ValueType = valueType;
        }

        public override object GetValue() => Value;
    }

    public class ArrayValue<T> : SimpleValue<T[]>
    {
        public ArrayValue(JsonElement jsonElement, ValueType valueType, Func<JsonElement, T> elementParser)
            : base(ParseJsonElement(jsonElement, elementParser), valueType) { }

        private static T[] ParseJsonElement(JsonElement jsonElement, Func<JsonElement, T> elementParser)
        {
            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Json element is not array");

            var jsonArray = jsonElement.EnumerateArray().ToArray();
            if (jsonArray.Length == 0) return [];

            var result = new T[jsonArray.Length];
            for (int i = 0; i < jsonArray.Length; i++)
                result[i] = elementParser(jsonArray[i]);

            return result;
        }
    }
}
