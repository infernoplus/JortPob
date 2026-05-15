using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WitchyFormats;

namespace JortPob.Common
{
    public class SillyJsonUtils
    {
        // For soundbank shit: Sorts a JsonArray of numbers
        public static void SortUInt(JsonNode jsonNode)
        {
            JsonArray temp = jsonNode.AsArray();
            List<uint> sorted = temp.Select(x => x.GetValue<uint>()).ToList();
            sorted.Sort();
            temp.Clear();
            sorted.ForEach(temp.Add);
        }

        // int32
        public static void SetField(Paramanager paramanager, Paramanager.ParamType paramType, int rowId, string fieldName, int value)
        {
            FsParam param = paramanager.param[paramType];
            FsParam.Row row = paramanager.GetRow(param, rowId);
            FsParam.Cell cell = (FsParam.Cell)row[fieldName];
            cell.SetValue(value);
        }

        // ushort
        public static void SetField(Paramanager paramanager, Paramanager.ParamType paramType, int rowId, string fieldName, ushort value)
        {
            FsParam param = paramanager.param[paramType];
            FsParam.Row row = paramanager.GetRow(param, rowId);
            FsParam.Cell cell = (FsParam.Cell)row[fieldName];
            cell.SetValue(value);
        }

        public static void ModifyRow(FsParam.Row row, Dictionary<string, string> data)
        {
            foreach (KeyValuePair<string, string> property in data)
            {
                string key = property.Key;
                string value = property.Value;
                FsParam.Cell cell = (FsParam.Cell)row[key];
                switch (cell.Value.GetType())
                {
                    case Type t when t == typeof(int):
                        cell.SetValue(int.Parse(value));
                        break;
                    case Type t when t == typeof(uint):
                        cell.SetValue(uint.Parse(value));
                        break;
                    case Type t when t == typeof(ushort):
                        cell.SetValue(ushort.Parse(value));
                        break;
                    case Type t when t == typeof(short):
                        cell.SetValue(short.Parse(value));
                        break;
                    case Type t when t == typeof(byte):
                        cell.SetValue(byte.Parse(value));
                        break;
                    case Type t when t == typeof(sbyte):
                        cell.SetValue(sbyte.Parse(value));
                        break;
                    case Type t when t == typeof(float):
                        cell.SetValue(float.Parse(value));
                        break;
                    case Type t when t == typeof(bool):
                        cell.SetValue(bool.Parse(value));
                        break;
                    case Type t when t == typeof(string):
                        cell.SetValue(value);
                        break;
                    default:
                        throw new NotImplementedException($"Type {cell.Value.GetType()} not implemented in ModifyRow.");
                }
            }
        }

        public static void CopyRowAndModify(Paramanager paramanager, SpeffManager speffManager, Paramanager.ParamType paramType, string name, int sourceRow, int destRow, Dictionary<string, string> data)
        {
            FsParam param = paramanager.param[paramType];
            FsParam.Row row = paramanager.CloneRow(param[sourceRow], name, destRow);

            /* List of named speff fields that should be resolved before applying */
            List<string> equipSpeffFieldNames = [
                "spEffectBehaviorId0", "spEffectBehaviorId1", "spEffectBehaviorId2",  // these 6 are used by weapon/armor/acc
                "residentSpEffectId", "residentSpEffectId1", "residentSpEffectId2",
            ];
            const string accSpeffFieldName = "refId";     // only accessorys use this
            const string goodFieldName = "refId_default";  // only goods use this (ex: potions)


            foreach (KeyValuePair<string, string> property in data)
            {
                /* Resolve speff ids where applicable */
                string key = property.Key;
                string value;

                if (Utility.StringIsInteger(property.Value)) { value = property.Value; }
                else
                {
                    switch (paramType)
                    {
                        case Paramanager.ParamType.EquipParamWeapon:
                            if (equipSpeffFieldNames.Contains(key))
                            {
                                SpeffManager.Speff speff = speffManager.GetSpeff(property.Value);
                                value = speff.row.ToString();
                                break;
                            }
                            goto default;
                        case Paramanager.ParamType.EquipParamProtector:
                            if (equipSpeffFieldNames.Contains(key))
                            {
                                SpeffManager.Speff speff = speffManager.GetSpeff(property.Value);
                                value = speff.row.ToString();
                                break;
                            }
                            goto default;
                        case Paramanager.ParamType.EquipParamAccessory:
                            if (equipSpeffFieldNames.Contains(key) || accSpeffFieldName == key)
                            {
                                SpeffManager.Speff speff = speffManager.GetSpeff(property.Value);
                                value = speff.row.ToString();
                                break;
                            }
                            goto default;
                        case Paramanager.ParamType.EquipParamGoods:
                            if (goodFieldName == key)
                            {
                                SpeffManager.Speff speff = speffManager.GetSpeff(property.Value);
                                value = speff.row.ToString();
                                break;
                            }
                            goto default;
                        default:
                            value = property.Value;
                            break;
                    }
                }

                /* Apply values from json to the param */
                FsParam.Cell cell = (FsParam.Cell)row[key];
                switch (cell.Value.GetType())
                {
                    case Type t when t == typeof(int):
                        cell.SetValue(int.Parse(value));
                        break;
                    case Type t when t == typeof(uint):
                        cell.SetValue(uint.Parse(value));
                        break;
                    case Type t when t == typeof(ushort):
                        cell.SetValue(ushort.Parse(value));
                        break;
                    case Type t when t == typeof(short):
                        cell.SetValue(short.Parse(value));
                        break;
                    case Type t when t == typeof(byte):
                        cell.SetValue(byte.Parse(value));
                        break;
                    case Type t when t == typeof(sbyte):
                        cell.SetValue(sbyte.Parse(value));
                        break;
                    case Type t when t == typeof(float):
                        cell.SetValue(float.Parse(value));
                        break;
                    case Type t when t == typeof(bool):
                        cell.SetValue(bool.Parse(value));
                        break;
                    case Type t when t == typeof(string):
                        cell.SetValue(value);
                        break;
                    default:
                        throw new NotImplementedException($"Type {cell.Value.GetType()} not implemented in CopyRowAndModify.");
                }
            }

            paramanager.AddOrReplaceRow(param, row);
        }
    }
}
