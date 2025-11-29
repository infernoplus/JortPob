using JortPob.Common;
using Microsoft.Scripting.Metadata;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WitchyFormats;

namespace JortPob
{
    public class ItemManager
    {
        public enum Type {
            None = -1,
            Goods = 3,
            Weapon = 0,
            Armor = 1,
            Accessory = 2,
            CustomWeapon = 6, // customweapon is a weapon that has a nonstandard skill or is upgraded
            Enchant = 100     // enchant is an ash of war itemy thing
        }

        public enum Infusion
        {
            None = 0,
            Heavy = 100,
            Keen = 200,
            Quality = 300,
            Fire = 400,
            FlameArt = 500,  // I am not entirely sure what this is
            Lightning = 600,
            Sacred = 700,
            Magic = 800,
            Cold = 900,
            Poison = 1000,
            Blood = 1100,
            Occult = 1200
        }

        public readonly List<ItemInfo> items; // string is record if of item from MW. int is the row id of the item in Elden Ring, type is type of item in ER

        private Paramanager paramanager;
        private SpeffManager speffManager;
        private IconManager iconManager;
        private TextManager textManager;

        private int nextWeaponId, nextArmorId, nextAccessoryId, nextGoodsId, nextCustomWeaponId;

        public ItemManager(ESM esm, Paramanager paramanager, SpeffManager speffManager, IconManager iconManager, TextManager textManager)
        {
            this.paramanager = paramanager;
            this.speffManager = speffManager;
            this.iconManager = iconManager;
            this.textManager = textManager;

            items = new();

            nextWeaponId = 70000000;
            nextArmorId = 6000000;
            nextGoodsId = 30000;
            nextAccessoryId = 8500;
            nextCustomWeaponId = 15000;

            /* Search all scripts for references to items. Items used by scripts are critical and must be created */
            List<Papyrus.Call> itemCalls = new();
            List<string> scriptItems = new();  // list of record ids for items used in scripts
            foreach (Papyrus papyrus in esm.scripts)
            {
                itemCalls.AddRange(papyrus.GetCalls(Papyrus.Call.Type.AddItem));
                itemCalls.AddRange(papyrus.GetCalls(Papyrus.Call.Type.RemoveItem));
                itemCalls.AddRange(papyrus.GetCalls(Papyrus.Call.Type.GetItemCount));
            }

            /* Search all dialog papyrus calls and filters as well */
            foreach(Dialog.DialogRecord dialog in esm.dialog)
            {
                foreach (Dialog.DialogInfoRecord info in dialog.infos)
                {
                    if(info.script != null)
                    {
                        foreach (Papyrus.Call call in info.script.calls)
                        {
                            switch(call.type)
                            {
                                case Papyrus.Call.Type.RemoveItem:
                                case Papyrus.Call.Type.AddItem:
                                    itemCalls.Add(call);
                                    break;
                            }
                        }
                    }

                    foreach(Dialog.DialogFilter filter in info.filters)
                    {
                        if (filter.type == Dialog.DialogFilter.Type.Item)
                        {
                            if (!scriptItems.Contains(filter.id.ToLower())) { scriptItems.Add(filter.id.ToLower()); }
                        }
                    }
                }
            }

            /* Grab item record ids from calls */
            foreach(Papyrus.Call itemCall in itemCalls)
            {
                switch(itemCall.type)
                {
                    case Papyrus.Call.Type.GetItemCount:
                    case Papyrus.Call.Type.RemoveItem:
                    case Papyrus.Call.Type.AddItem:
                        if (!scriptItems.Contains(itemCall.parameters[0].ToLower())) { scriptItems.Add(itemCall.parameters[0].ToLower()); }
                        break;
                }
            }

            /* Generate params for items */
            /* If the item has an override mapping we use that */
            /* If the item is used in a script we generate a placeholder item of whatever type seems relevant */
            /* If an item is not mapped and not used in a script we discard it */

            /* Misc Items */ // This category contains keys, unique items like the bittercup, souls gems, and random clutter such as bottles and plates
            /* Weapons */
            /* Armor */
            /* Clothing */   // Contains both clothes AND jewelrey 
            /* Ingredient */
            /* Alchemy */    // Potions
            /* Light */      // Torches, lanterns, candles... We are actually already processing these as statics in the world space. Hopefully we will not need to change that.
            /* Apparatus */  // Alchemy tools. Probably won't use these at all in this mod
            /* Book */
            /* Lockpick */   // These last three are tools as well
            /* Probe */
            /* Repair Item */
            void HandleItemsByRecord(ESM.Type recordType)
            {
                foreach (JsonNode json in esm.GetAllRecordsByType(recordType))
                {
                    string id = json["id"].GetValue<string>().ToLower();
                    int value = json["data"]["value"].GetValue<int>();

                    bool scriptItem = scriptItems.Contains(id);
                    Override.ItemDefinition def = Override.GetItemDefinition(id);
                    Override.ItemRemap remap = Override.GetItemRemap(id);

                    /* Item id has a corresponding definition json file from overrides/items */   // this creates a new item for this id that is defined by data in the json file
                    if (def != null)
                    {
                        switch (def.type)
                        {
                            case Type.Weapon:
                                /* First check if this weapon has infusion rows, and if we need to copy them */
                                FsParam.Row sourceRow = paramanager.GetRow(paramanager.param[Paramanager.ParamType.EquipParamWeapon], def.row);
                                bool hasInfusionRows = (short)sourceRow["reinforceTypeId"].Value.Value == (short)0;
                                bool needsInfusionRows;
                                if (def.data.ContainsKey("reinforceTypeId"))
                                {
                                    needsInfusionRows = (int.Parse(def.data["reinforceTypeId"]) == 0);
                                }
                                else { needsInfusionRows = hasInfusionRows; }

                                if (!hasInfusionRows && needsInfusionRows) { throw new Exception($"Item Def '{def.id}' is copying an uninfusable weapon but trying to give it infusion rows. This is impossible!"); }

                                /* Make a list of what rows we are creating and the infusion type if applicable */
                                List<(Infusion infusion, int row)> rowsToCopy = new();
                                if (needsInfusionRows)
                                {
                                    foreach (Infusion infusion in Enum.GetValues(typeof(Infusion)))
                                    {
                                        rowsToCopy.Add((infusion, def.row + ((int)infusion)));
                                    }
                                }
                                else
                                {
                                    rowsToCopy.Add((Infusion.None, def.row));
                                }

                                /* Auto generate some originEquipWep fields and plonk them in the def.data before we copy and modify rows */
                                for (int i = 0; i <= 25; i++)
                                {
                                    string fieldName = $"originEquipWep{(i == 0 ? "" : i)}";
                                    int fieldValue = (int)sourceRow[fieldName].Value.Value;
                                    if (fieldValue == -1 || def.data.ContainsKey(fieldName)) { continue; }
                                    else
                                    {
                                        def.data.Add(fieldName, nextWeaponId.ToString());
                                    }
                                }

                                /* Copy rows and apply modifications */
                                foreach ((Infusion infusion, int row) in rowsToCopy)
                                {
                                    ItemInfo weapon = new(def.id, Type.Weapon, nextWeaponId + (int)infusion, value, scriptItem);
                                    SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamWeapon, def.id, row, weapon.row, def.data);
                                    textManager.AddWeapon(weapon.row, def.text.name, def.text.description, infusion);
                                    if (def.text.enchant != null)
                                    {
                                        FsParam.Row infusedSourceRow = paramanager.GetRow(paramanager.param[Paramanager.ParamType.EquipParamWeapon], row);
                                        int j = 0;
                                        for (int i = 0; i < 3 && j < def.text.enchant.Length; i++)
                                        {
                                            string fieldName = $"spEffectMsgId{i}";
                                            int fieldValue = (int)infusedSourceRow[fieldName].Value.Value;

                                            if (fieldValue != -1) { continue; }

                                            string enchant = def.text.enchant[j++];
                                            int txtId = textManager.AddWeaponEffect(enchant);
                                            SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamWeapon, weapon.row, fieldName, txtId);
                                        }
                                    }
                                    if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamWeapon, weapon.row, "iconId", iconManager.GetIconByRecord(id).id); }
                                    items.Add(weapon);
                                }
                                nextWeaponId += 10000;
                                break;
                            case Type.Armor:
                                ItemInfo armor = new(def.id, Type.Armor, nextArmorId, value, scriptItem);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamProtector, def.id, def.row, nextArmorId, def.data);
                                textManager.AddArmor(armor.row, def.text.name, def.text.summary, def.text.description);
                                if (def.useIcon) {
                                    SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamProtector, nextArmorId, "iconIdM", iconManager.GetIconByRecord(id).id);
                                    SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamProtector, nextArmorId, "iconIdF", iconManager.GetIconByRecord(id).id);
                                }
                                nextArmorId += 10000;
                                items.Add(armor);
                                break;
                            case Type.Accessory:
                                ItemInfo accessory = new(def.id, Type.Accessory, nextAccessoryId, value, scriptItem);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamAccessory, def.id, def.row, nextAccessoryId, def.data);
                                textManager.AddAccessory(accessory.row, def.text.name, def.text.summary, def.text.description);
                                if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamAccessory, nextAccessoryId, "iconId", iconManager.GetIconByRecord(id).id); }
                                nextAccessoryId += 10;
                                items.Add(accessory);
                                break;
                            case Type.Goods:
                                ItemInfo goods = new(def.id, Type.Goods, nextGoodsId, value, scriptItem);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamGoods, def.id, def.row, nextGoodsId, def.data);
                                textManager.AddGoods(goods.row, def.text.name, def.text.summary, def.text.description, def.text.effect);
                                if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamGoods, nextGoodsId, "iconId", iconManager.GetIconByRecord(id).id); }
                                nextGoodsId += 10;
                                items.Add(goods);
                                break;
                            default:
                                throw new Exception($"Item definition for id {id} has invalid type {def.type}");
                        }
                    }
                    /* Item id has a corresponding entry in the item_remap.json file */   // this points the item id at an existing item row. Not a copy, more like a reference
                    else if (remap != null)
                    {
                        ItemInfo it;

                        /* If the remap is to a customweapon we need to generate a row for that. */
                        if (remap.type == Type.CustomWeapon)
                        {
                            int customWeaponRow = GenerateCustomWeapon(remap);
                            it = new(id, remap.type, customWeaponRow, value, scriptItem);
                        }
                        /* Otherwise it's chill */
                        else { it = new(id, remap.type, remap.row, value, scriptItem); }

                        items.Add(it);

                        // if the item remap has text changes (name/description) we apply those!
                        if (remap.HasTextChanges())
                        {
                            switch (remap.type)
                            {
                                case Type.Weapon:
                                    textManager.RenameWeapon(it.row, remap.text.name, remap.text.description);
                                    break;
                                case Type.Armor:
                                    textManager.RenameArmor(it.row, remap.text.name, remap.text.summary, remap.text.description);
                                    break;
                                case Type.Accessory:
                                    textManager.RenameAccessory(it.row, remap.text.name, remap.text.summary, remap.text.description);
                                    break;
                                case Type.Goods:
                                    textManager.RenameGoods(it.row, remap.text.name, remap.text.summary, remap.text.description, remap.text.effect);
                                    break;
                                case Type.CustomWeapon:
                                    throw new Exception("CustomWeapon remaps cannot have text changes!");
                                default:
                                    throw new Exception($"Item remap for id {id} has invalid type {def.type}");
                            }
                        }
                    }
                    /* Item id has no matches so we just generate an item from data in the ESM. This can be bad for some things but fine for others. Really just depends! */
                    else if (scriptItem)
                    {
                        if (json["has_script_reference"] == null) { json["has_script_reference"] = scriptItem; } // add this data to the json so i dont have to pass this var through parameters
                        GenerateItem(recordType, json);
                    }
                    /* Depending on debug settings, generate this non-essential item */
                    else if(!Const.DEBUG_SKIP_NON_ESSENTIAL_ITEMS && !Override.CheckSkipItem(id))
                    {
                        GenerateItem(recordType, json);
                    }
                }
            }

            HandleItemsByRecord(ESM.Type.Weapon);
            HandleItemsByRecord(ESM.Type.Armor);
            HandleItemsByRecord(ESM.Type.Clothing);
            HandleItemsByRecord(ESM.Type.Ingredient);
            HandleItemsByRecord(ESM.Type.Alchemy);
            HandleItemsByRecord(ESM.Type.Book);
            HandleItemsByRecord(ESM.Type.MiscItem);
            HandleItemsByRecord(ESM.Type.Apparatus);
            HandleItemsByRecord(ESM.Type.Probe);
            HandleItemsByRecord(ESM.Type.Lockpick);
            HandleItemsByRecord(ESM.Type.RepairItem);

        }

        private int GenerateCustomWeapon(Override.ItemRemap remap)
        {
            FsParam customWeaponParam = paramanager.param[Paramanager.ParamType.EquipParamCustomWeapon];
            FsParam.Row row = paramanager.CloneRow(customWeaponParam[10], remap.id, nextCustomWeaponId);

            row["baseWepId"].Value.SetValue(remap.row + ((int)remap.infusion));
            row["gemId"].Value.SetValue(remap.skill);
            row["reinforceLv"].Value.SetValue((byte)remap.upgrade);

            paramanager.AddRow(customWeaponParam, row);
            nextCustomWeaponId += 10000;

            return row.ID;
        }

        /* Generate an item from the morrowind record data. This is a "Best guess" situation. */
        /* For some item types this is fine. Books and keys for example can be generated like this without any issue. */
        /* But for things like weapons or armor we should make sure those get defined by hand eventually. */
        private void GenerateItem(ESM.Type recordType, JsonNode json)
        {
            switch(recordType)
            {
                case ESM.Type.Weapon:
                    string weaponType = json["data"]["weapon_type"].GetValue<string>().ToLower();
                    switch(weaponType)
                    {
                        case "marksmanthrown":
                            GenerateItemThrown(json);
                            break;
                        default:
                            GenerateItemWeapon(json);
                            break;
                    }
                    break;
                case ESM.Type.Armor:
                    string armorType = json["data"]["armor_type"].GetValue<string>().ToLower();
                    switch(armorType)
                    {
                        case "shield":
                            GenerateItemShield(json);
                            break;
                        default:
                            GenerateItemArmor(json);
                            break;
                    }

                    break;
                case ESM.Type.Clothing:
                    string clothingType = json["data"]["clothing_type"].GetValue<string>().ToLower();
                    switch(clothingType)
                    {
                        case "amulet":
                        case "ring":
                        case "belt":
                            GenerateItemAccessory(json);
                            break;
                        default:
                            GenerateItemClothing(json);
                            break;
                    }
                    break;
                case ESM.Type.Ingredient:
                    GenerateItemIngredient(json);
                    break;
                case ESM.Type.Alchemy:
                    GenerateItemPotion(json);
                    break;
                case ESM.Type.Book:
                    GenerateItemBook(json);
                    break;
                case ESM.Type.Apparatus:
                case ESM.Type.Probe:
                case ESM.Type.Lockpick:
                case ESM.Type.RepairItem:
                    GenerateItemPlaceholder(json);
                    break;
                case ESM.Type.MiscItem:
                    string id = json["id"].GetValue<string>().ToLower();
                    if (id.Contains("key_")) { GenerateItemKey(json); }
                    else { GenerateItemPlaceholder(json); }
                    break;
            }
        }

        /* In most cases I want to have paramamanger do param edits itself, but in this case we will let ItemManager do them to prevent excessive clutter in paramanager */
        private void GenerateItemWeapon(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            string weaponType = json["data"]["weapon_type"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            int rowToCopy;
            switch (weaponType)
            {
                case "arrow":
                    rowToCopy = 50000000; // standard arrow
                    break;
                case "bolt":
                    rowToCopy = 52000000; // standard bolt
                    break;
                case "marksmanbow":
                    rowToCopy = 40000000; // shortbow
                    break;
                case "marksmancrossbow":
                    rowToCopy = 43020000; // light crossbow
                    break;
                case "axeonehand":
                    rowToCopy = 14020000; // hand axe
                    break;
                case "axetwoclose":
                    rowToCopy = 15000000; // greataxe
                    break;
                case "bluntonehand":
                    rowToCopy = 11000000; // mace
                    break;
                case "blunttwowide":
                case "blunttwoclose":
                    rowToCopy = 12060000; // great mace
                    break;
                case "longbladeonehand":
                    rowToCopy = 2010000; // short sword
                    break;
                case "longbladetwoclose":
                    rowToCopy = 3180000; // claymore
                    break;
                case "shortbladeonehand":
                    rowToCopy = 1000000; // dagger
                    break;
                case "speartwowide":
                default:
                    rowToCopy = 16000000; // short spear
                    break;
            }

            FsParam weaponParam = paramanager.param[Paramanager.ParamType.EquipParamWeapon];
            FsParam.Row row = paramanager.CloneRow(weaponParam[rowToCopy], id, nextWeaponId);

            string enchantId = json["enchanting"] != null ? json["enchanting"].GetValue<string>().ToLower() : null;
            SpeffManager.SpeffEnchant speff = speffManager.GetEnchantingSpeff(enchantId);

            textManager.AddWeapon(row.ID, json["name"].GetValue<string>(), "Descriptions describe things!");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon!=null?icon.id:((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddRow(weaponParam, row);
            items.Add(new(id, Type.Weapon, row.ID, value, hasScriptReference));
            nextWeaponId += 10000;
        }

        private void GenerateItemShield(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam weaponParam = paramanager.param[Paramanager.ParamType.EquipParamWeapon];
            FsParam.Row row = paramanager.CloneRow(weaponParam[31330000], id, nextWeaponId); // 31330000 is a basic heater shield

            string enchantId = json["enchanting"] != null ? json["enchanting"].GetValue<string>().ToLower() : null;
            SpeffManager.SpeffEnchant speff = speffManager.GetEnchantingSpeff(enchantId);

            textManager.AddWeapon(row.ID, json["name"].GetValue<string>(), "Descriptions describe things!");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddRow(weaponParam, row);
            items.Add(new(id, Type.Weapon, row.ID, value, hasScriptReference));
            nextWeaponId += 10000;
        }

        private void GenerateItemArmor(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            string armorType = json["data"]["armor_type"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            int rowToCopy;
            switch(armorType)
            {
                case "helmet":
                    rowToCopy = 1100000; // chain coif
                    break;
                case "rightbracer":
                case "leftbracer":
                case "rightgauntlet":
                case "leftgauntlet":
                    rowToCopy = 1100200; // chain gloves
                    break;
                case "greaves":
                case "boots":
                    rowToCopy = 1100300; // chain legs
                    break;
                case "rightpauldron":
                case "leftpauldron":
                case "cuirass":
                default:
                    rowToCopy = 1100100; // chain armor
                    break;
            }

            FsParam armorParam = paramanager.param[Paramanager.ParamType.EquipParamProtector];
            FsParam.Row row = paramanager.CloneRow(armorParam[rowToCopy], id, nextArmorId);

            string enchantId = json["enchanting"] != null ? json["enchanting"].GetValue<string>().ToLower() : null;
            SpeffManager.SpeffEnchant speff = speffManager.GetEnchantingSpeff(enchantId);

            textManager.AddArmor(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconIdM"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["iconIdF"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddRow(armorParam, row);
            items.Add(new(id, Type.Armor, row.ID, value, hasScriptReference));
            nextArmorId += 10000;
        }

        private void GenerateItemClothing(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            string clothingType = json["data"]["clothing_type"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            int rowToCopy;
            switch (clothingType)
            {
                case "robe":
                    rowToCopy = 630100; // astrologers robe
                    break;
                case "leftglove":
                case "rightglove":
                    rowToCopy = 630200; // astrologers gloves
                    break;
                case "skirt":
                case "pants":
                case "shoes":
                    rowToCopy = 630300; // astrologers pants
                    break;
                case "shirt":
                default:
                    rowToCopy = 631100; // astrologers robe altered
                    break;
            }

            FsParam armorParam = paramanager.param[Paramanager.ParamType.EquipParamProtector];
            FsParam.Row row = paramanager.CloneRow(armorParam[rowToCopy], id, nextArmorId);

            string enchantId = json["enchanting"] != null ? json["enchanting"].GetValue<string>().ToLower() : null;
            SpeffManager.SpeffEnchant speff = speffManager.GetEnchantingSpeff(enchantId);

            textManager.AddArmor(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconIdM"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["iconIdF"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddRow(armorParam, row);
            items.Add(new(id, Type.Armor, row.ID, value, hasScriptReference));
            nextArmorId += 10000;
        }

        private void GenerateItemAccessory(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            string clothingType = json["data"]["clothing_type"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            int rowToCopy;
            switch (clothingType)
            {
                case "belt":
                    rowToCopy = 4000; // dragoncrest shield talisman
                    break;
                case "amulet":
                    rowToCopy = 1040; // erdtree favor
                    break;
                case "ring":
                default:
                    rowToCopy = 1010; // cerulean amber medaliion
                    break;
            }

            FsParam accessoryParam = paramanager.param[Paramanager.ParamType.EquipParamAccessory];
            FsParam.Row row = paramanager.CloneRow(accessoryParam[rowToCopy], id, nextAccessoryId);

            string enchantId = json["enchanting"] != null ? json["enchanting"].GetValue<string>().ToLower() : null;
            SpeffManager.SpeffEnchant speff = speffManager.GetEnchantingSpeff(enchantId);

            textManager.AddAccessory(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            if (speff != null) { row["refId"].Value.SetValue(speff.row); }

            paramanager.AddRow(accessoryParam, row);
            items.Add(new(id, Type.Accessory, row.ID, value, hasScriptReference));
            nextAccessoryId += 10;
        }

        private void GenerateItemThrown(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[1700], id, nextGoodsId); // 1700 is a throwing knife

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        private void GenerateItemPotion(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[2000900], id, nextGoodsId); // 2000900 is a divine blesing

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            SpeffManager.Speff speff = speffManager.GetAlchemySpeff(id);

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["sellValue"].Value.SetValue(json["data"]["value"].GetValue<int>());
            row["rarity"].Value.SetValue((byte)0);
            row["maxNum"].Value.SetValue((short)5);
            row["refId_default"].Value.SetValue(speff.row);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        private void GenerateItemIngredient(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[20690], id, nextGoodsId); // 20690 is a herba

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        public void GenerateItemBook(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[8859], id, nextGoodsId); // 8859 is the assasins prayerbook

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        private void GenerateItemPlaceholder(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[2120], id, nextGoodsId); // 2120 is soap

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        private void GenerateItemKey(JsonNode json)
        {
            string id = json["id"].GetValue<string>().ToLower();
            bool hasScriptReference = json["has_script_reference"] != null ? json["has_script_reference"].GetValue<bool>() : false;
            int value = json["data"]["value"].GetValue<int>();
            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
            FsParam.Row row = paramanager.CloneRow(goodsParam[8197], id, nextGoodsId); // 8197 is the sewer gaol key

            textManager.AddGoods(row.ID, json["name"].GetValue<string>(), "Information informs you.", "Descriptions describe things!", "More information.");

            IconManager.IconInfo icon = iconManager.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);

            paramanager.AddRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference));
            nextGoodsId += 10;
        }

        public ItemInfo GetItem(string id)
        {
            foreach(ItemInfo item in items)
            {
                if(item.id.ToLower() == id.ToLower()) { return item; }
            }
            return null;
        }

        public List<ItemInfo> GetInventory(NpcContent npc)
        {
            List<ItemInfo> inv = new();
            foreach ((string id, int quantity) invItem in npc.inventory)
            {
                ItemInfo item = GetItem(invItem.id);
                if (item != null) { inv.Add(item); }
            }
            return inv;
        }

        public List<ItemInfo> GetInventory(ContainerContent container)
        {
            List<ItemInfo> inv = new();
            foreach ((string id, int quantity) invItem in container.inventory)
            {
                ItemInfo item = GetItem(invItem.id);
                if (item != null) { inv.Add(item); }
            }
            return inv;
        }

        /* Stores info on an item */
        [DebuggerDisplay("Item :: {id} :: {type}->{row}")]
        public class ItemInfo
        {
            public readonly Type type;  // param type
            public readonly int row;    // param row
            public readonly string id;  // morrowind record id
            public readonly int value;  // value for shops to use
            public readonly bool quest; // item is referenced in a script. this doesn't 100% mean its a quest item but it does mean it's needed to compile scripts

            public ItemInfo(string id, Type type, int row, int value, bool quest)
            {
                this.id = id;
                this.type = type;
                this.row = row;
                this.value = value;
                this.quest = quest;
            }
        }
    }
}
