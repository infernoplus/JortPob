using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using WitchyFormats;
using static IronPython.Modules._ast;

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
        public readonly List<LeveledList> lists; // leveled lists for items
        private readonly Dictionary<string, ItemInfo> spellScrolls = new(); // spell id -> generated scroll item, populated by RegisterSpellScrolls()

        private Paramanager paramanager;
        private ScriptManager scriptManager;
        private SpellManager spellManager;
        public RecipeManager recipeManager;
        private SpeffManager speffManager;
        private MenuTextureManager textureManager;
        private TextManager textManager;

        private int nextWeaponId, nextArmorId, nextAccessoryId, nextGoodsId, nextCustomWeaponId, nextShopId;

        public ItemManager(ESM esm, Paramanager paramanager, ScriptManager scriptManager, SpeffManager speffManager, MenuTextureManager textureManager, TextManager textManager)
        {
            this.paramanager = paramanager;
            this.scriptManager = scriptManager;
            this.spellManager = new SpellManager(esm, paramanager, textManager);
            this.speffManager = speffManager;
            this.textureManager = textureManager;
            this.textManager = textManager;

            items = new();

            nextWeaponId = 70000000;
            nextArmorId = 6000000;
            nextGoodsId = 30000;
            nextAccessoryId = 8500;
            nextCustomWeaponId = 15000;
            nextShopId = 1700000;

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
            foreach (Dialog.DialogRecord dialog in esm.dialog)
            {
                foreach (Dialog.DialogInfoRecord info in dialog.infos)
                {
                    if (info.script != null)
                    {
                        foreach (Papyrus.Call call in info.script.calls)
                        {
                            switch (call.type)
                            {
                                case Papyrus.Call.Type.RemoveItem:
                                case Papyrus.Call.Type.AddItem:
                                    itemCalls.Add(call);
                                    break;
                            }
                        }
                    }

                    foreach (Dialog.DialogFilter filter in info.filters)
                    {
                        if (filter.type == Dialog.DialogFilter.Type.Item)
                        {
                            if (!scriptItems.Contains(filter.id.ToLower())) { scriptItems.Add(filter.id.ToLower()); }
                        }
                    }
                }
            }

            /* Grab item record ids from calls */
            foreach (Papyrus.Call itemCall in itemCalls)
            {
                switch (itemCall.type)
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
                        /* Check if we have the field for value set, if we don't then add the morrowind field for value */
                        if(!def.data.ContainsKey("sellValue"))
                        {
                            def.data.Add("sellValue", ((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE)).ToString());
                        }

                        switch (def.type)
                        {
                            case Type.CustomWeapon:
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
                                    int rowId = nextWeaponId + (int)infusion;
                                    SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamWeapon, def.id, row, rowId, def.data);
                                    textManager.AddWeapon(rowId, def.text.name, def.text.description, infusion);
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
                                            SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamWeapon, rowId, fieldName, txtId);
                                        }
                                    }
                                    if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamWeapon, rowId, "iconId", textureManager.icon.GetIconByRecord(id).id); }
                                }

                                /* Add iteminfo for weapon, or generate customweapon rows if needed */
                                if(def.type == Type.Weapon)
                                {
                                    ItemInfo weapon = new(def.id, Type.Weapon, nextWeaponId, value, scriptItem, json);
                                    items.Add(weapon);
                                    nextWeaponId += 10000;
                                }
                                else if (def.type == Type.CustomWeapon)
                                {
                                    FsParam customWeaponParam = paramanager.param[Paramanager.ParamType.EquipParamCustomWeapon];
                                    FsParam.Row row = paramanager.CloneRow(customWeaponParam[10], def.id, nextCustomWeaponId);
                                    row["baseWepId"].Value.SetValue(nextWeaponId + ((int)def.infusion));
                                    row["gemId"].Value.SetValue(def.skill);
                                    row["reinforceLv"].Value.SetValue((byte)def.upgrade);
                                    paramanager.AddOrReplaceRow(customWeaponParam, row);

                                    ItemInfo custom = new(def.id, Type.CustomWeapon, nextCustomWeaponId, value, scriptItem, json);
                                    items.Add(custom);
                                    nextCustomWeaponId += 10000;
                                }

                                break;
                            case Type.Armor:
                                ItemInfo armor = new(def.id, Type.Armor, nextArmorId, value, scriptItem, json);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamProtector, def.id, def.row, nextArmorId, def.data);
                                textManager.AddArmor(armor.row, def.text.name, def.text.summary, def.text.description);
                                if (def.useIcon)
                                {
                                    SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamProtector, nextArmorId, "iconIdM", textureManager.icon.GetIconByRecord(id).id);
                                    SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamProtector, nextArmorId, "iconIdF", textureManager.icon.GetIconByRecord(id).id);
                                }
                                nextArmorId += 10000;
                                items.Add(armor);
                                break;
                            case Type.Accessory:
                                ItemInfo accessory = new(def.id, Type.Accessory, nextAccessoryId, value, scriptItem, json);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamAccessory, def.id, def.row, nextAccessoryId, def.data);
                                textManager.AddAccessory(accessory.row, def.text.name, def.text.summary, def.text.description);
                                if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamAccessory, nextAccessoryId, "iconId", textureManager.icon.GetIconByRecord(id).id); }
                                nextAccessoryId += 10;
                                items.Add(accessory);
                                break;
                            case Type.Goods:
                                ItemInfo goods = new(def.id, Type.Goods, nextGoodsId, value, scriptItem, json);
                                SillyJsonUtils.CopyRowAndModify(paramanager, speffManager, Paramanager.ParamType.EquipParamGoods, def.id, def.row, nextGoodsId, def.data);
                                textManager.AddGoods(goods.row, def.text.name, def.text.summary, def.text.description, def.text.effect);
                                if (def.useIcon) { SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamGoods, nextGoodsId, "iconId", textureManager.icon.GetIconByRecord(id).id); }
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
                            it = new(id, remap.type, customWeaponRow, value, scriptItem, json);
                        }
                        /* Otherwise it's chill */
                        else { it = new(id, remap.type, remap.row, value, scriptItem, json); }

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

                        /* Adjust price of item froms shops */  // @TODO: this is a quick and dirty. should average value across all remaps pointing at same item
                        switch (remap.type)
                        {
                            case Type.Weapon:
                                SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamWeapon, it.row, "sellValue", (int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
                                break;
                            case Type.Armor:
                                SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamProtector, it.row, "sellValue", (int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
                                break;
                            case Type.Accessory:
                                SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamAccessory, it.row, "sellValue", (int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
                                break;
                            case Type.Goods:
                                SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamGoods, it.row, "sellValue", (int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
                                break;
                        }
                    }
                    /* Item id has no matches so we just generate an item from data in the ESM. This can be bad for some things but fine for others. Really just depends! */
                    else if (scriptItem)
                    {
                        if (json["has_script_reference"] == null) { json["has_script_reference"] = scriptItem; } // add this data to the json so i dont have to pass this var through parameters
                        GenerateItem(recordType, json);
                    }
                    /* Depending on debug settings, generate this non-essential item */
                    else if (!Const.DEBUG_SKIP_NON_ESSENTIAL_ITEMS && !Override.CheckSkipItem(id))
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


            /* Now that all items are generated, let's create leveled lists as well */
            lists = new();

            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.LeveledItem))
            {
                string id = json["id"].GetValue<string>().ToLower();
                int chance = json["chance_none"].GetValue<int>();
                LeveledList list = new(id, chance);

                foreach (JsonNode node in json["items"].AsArray())
                {
                    string itemId = node.AsArray()[0].GetValue<string>().ToLower();
                    int levelReq = node.AsArray()[1].GetValue<int>();
                    ItemInfo item = GetItem(itemId);

                    list.Add(item, levelReq); // null is a valid entry. the reason we add null is because if an item is not generated we dont wanna exclude it as it would modify odds of rolling other items
                }

                lists.Add(list);
            }

            /* Lastly we should parse our json info regarding ash of war items */
            foreach(Override.SkillInfo skillInfo in Override.GetSkills())
            {
                SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.EquipParamGem, skillInfo.row, "sellValue", (int)Math.Max(1, skillInfo.value * Const.MERCANTILE_SELL_SCALE));
                if(skillInfo.HasTextChanges())
                {
                    textManager.RenameGem(skillInfo.row, skillInfo.text.name, skillInfo.text.description);
                }
            }

            /* Okay I lied, for real lastly we create recipemanager which handles alchemy recipe stuff */
            recipeManager = new RecipeManager(paramanager, scriptManager, this, textManager);
        }

        /* Generates one reusable Goods item per Morrowind Spell/Power, linked to the SpEffect SpeffManager already produces.
         * The scroll's refId_default fires the existing SpEffect on use; isConsume=0 keeps it in inventory.
         * maxRepositoryNum=1 caps the inventory at one per spell so duplicate AddSpell calls silently no-op. */
        private void RegisterSpellScrolls()
        {
            // The set of MagicEffect cases that SpeffManager.GenerateSpeff actually maps onto SpEffect fields.
            // If a spell has no effect from this set, the resulting scroll is inert; we mark it [unimplemented]
            // in the description so the punch-list of unmapped effects is visible from an FMG dump.
            HashSet<SpeffManager.Speff.Effect.MagicEffect> mapped = new()
            {
                SpeffManager.Speff.Effect.MagicEffect.RestoreHealth,
                SpeffManager.Speff.Effect.MagicEffect.RestoreMagicka,
                SpeffManager.Speff.Effect.MagicEffect.RestoreFatigue,
                SpeffManager.Speff.Effect.MagicEffect.FortifyHealth,
                SpeffManager.Speff.Effect.MagicEffect.FortifyMagicka,
                SpeffManager.Speff.Effect.MagicEffect.FortifyFatigue,
                SpeffManager.Speff.Effect.MagicEffect.FortifyAttribute,
            };

            FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];

            List<SpeffManager.SpeffSpell> spells = new();
            spells.AddRange(speffManager.GetSpeffBySpellType(SpeffManager.SpeffSpell.SpellType.Spell));
            spells.AddRange(speffManager.GetSpeffBySpellType(SpeffManager.SpeffSpell.SpellType.Power));

            foreach (SpeffManager.SpeffSpell spell in spells)
            {
                // Clone Divine Blessing (2000900) as the goods template — already used as a goods template
                // elsewhere in this file. Meaningful fields are overridden below.
                FsParam.Row row = paramanager.CloneRow(goodsParam[2000900], $"Scroll :: {spell.id}", nextGoodsId);

                row["refId_default"].Value.SetValue(spell.row);              // fire the existing SpEffect on use
                row["isConsume"].Value.SetValue((byte)0);                    // reusable
                row["maxNum"].Value.SetValue((short)1);                      // cap inventory at one
                row["maxRepositoryNum"].Value.SetValue((short)1);            // cap repository at one too

                // Icon: pull from the same buff-icon system SpeffManager already uses (SpeffManager.cs:205).
                if (spell.effects.Count > 0)
                {
                    IconManager.BuffInfo buff = textureManager.icon.GetBuffByType(spell.effects[0].effect);
                    if (buff != null) { row["iconId"].Value.SetValue((int)buff.id); }
                    else { row["iconId"].Value.SetValue(-1); }
                }
                else
                {
                    row["iconId"].Value.SetValue(-1);
                }

                paramanager.AddOrReplaceRow(goodsParam, row);

                // FMG text. Title-case the spell id for display ("fire_bite" -> "Fire Bite").
                string displayName = $"Scroll of {ToTitleCase(spell.id)}";
                string description = BuildScrollDescription(spell, mapped);
                textManager.AddGoods(nextGoodsId, displayName, "", description, "");

                // Track in spellScrolls dictionary for AddSpell/RemoveSpell stubs to look up.
                ItemInfo info = new(spell.id, Type.Goods, nextGoodsId, 0, true, ItemInfo.OriginalType.MiscItem);
                spellScrolls[spell.id] = info;
                items.Add(info);

                nextGoodsId += 10;
            }
        }

        /* Title-cases a snake_case Morrowind id for display. "fire_bite" -> "Fire Bite". */
        private static string ToTitleCase(string id)
        {
            string spaced = id.Replace('_', ' ').Trim();
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
        }

        /* Builds a description from the spell's effect list. Suffixes [unimplemented]
         * if NO effect on the spell maps to a SpEffect field SpeffManager actually translates. */
        private static string BuildScrollDescription(
            SpeffManager.SpeffSpell spell,
            HashSet<SpeffManager.Speff.Effect.MagicEffect> mapped)
        {
            if (spell.effects.Count == 0) { return "[unimplemented]"; }

            System.Text.StringBuilder sb = new();
            bool anyMapped = false;
            foreach (SpeffManager.Speff.Effect e in spell.effects)
            {
                if (mapped.Contains(e.effect)) { anyMapped = true; }
                if (sb.Length > 0) { sb.Append(", "); }
                sb.Append(e.effect.ToString());
                if (e.duration > 0) { sb.Append($" {e.duration}s"); }
            }
            if (!anyMapped) { sb.Append(" [unimplemented]"); }
            return sb.ToString();
        }

        private int GenerateCustomWeapon(Override.ItemRemap remap)
        {
            FsParam customWeaponParam = paramanager.param[Paramanager.ParamType.EquipParamCustomWeapon];
            FsParam.Row row = paramanager.CloneRow(customWeaponParam[10], remap.id, nextCustomWeaponId);

            row["baseWepId"].Value.SetValue(remap.row + ((int)remap.infusion));
            row["gemId"].Value.SetValue(remap.skill);
            row["reinforceLv"].Value.SetValue((byte)remap.upgrade);

            paramanager.AddOrReplaceRow(customWeaponParam, row);
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon!=null?icon.id:((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddOrReplaceRow(weaponParam, row);
            items.Add(new(id, Type.Weapon, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddOrReplaceRow(weaponParam, row);
            items.Add(new(id, Type.Weapon, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconIdM"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["iconIdF"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddOrReplaceRow(armorParam, row);
            items.Add(new(id, Type.Armor, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconIdM"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["iconIdF"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            if (speff != null) { row["residentSpEffectId"].Value.SetValue(speff.row); }

            paramanager.AddOrReplaceRow(armorParam, row);
            items.Add(new(id, Type.Armor, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            if (speff != null) { row["refId"].Value.SetValue(speff.row); }

            paramanager.AddOrReplaceRow(accessoryParam, row);
            items.Add(new(id, Type.Accessory, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));
            row["maxNum"].Value.SetValue((short)5);
            row["refId_default"].Value.SetValue(speff.row);

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
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

            IconManager.IconInfo icon = textureManager.icon.GetIconByRecord(id);
            row["iconId"].Value.SetValue(icon != null ? icon.id : ((ushort)0));
            row["rarity"].Value.SetValue((byte)0);
            row["sellValue"].Value.SetValue((int)Math.Max(1, value * Const.MERCANTILE_SELL_SCALE));

            paramanager.AddOrReplaceRow(goodsParam, row);
            items.Add(new(id, Type.Goods, row.ID, value, hasScriptReference, json));
            nextGoodsId += 10;
        }

        /* Returns true if the given record id goes to a leveled list, otherwise false */
        public bool IsList(string id)
        {
            foreach (LeveledList list in lists)
            {
                if (list.id.ToLower() == id.ToLower()) { return true; }
            }
            return false;
        }

        public LeveledList GetList(string id)
        {
            foreach (LeveledList list in lists)
            {
                if (list.id.ToLower() == id.ToLower()) { return list; }
            }
            return null;
        }

        public List<ItemInfo> GetItems(List<string> ids)
        {
            List<ItemInfo> itemInfos = new();
            foreach(string id in ids) { itemInfos.Add(GetItem(id)); }
            return itemInfos;
        }

        public ItemInfo GetItem(string id)
        {
            /* First search for a regular item */
            foreach(ItemInfo item in items)
            {
                if(item.id.ToLower() == id.ToLower()) { return item; }
            }

            /* If no result, search for a leveled list, if we find one resolve it for an item */
            foreach(LeveledList list in lists)
            {
                if(list.id.ToLower() == id.ToLower()) { return list.Get(); }
            }

            /* No match! */
            return null;
        }

        /* Returns the scroll ItemInfo for a given Morrowind Spell or Power id, or null if no scroll exists. */
        public ItemInfo GetSpellScroll(string spellId)
        {
            if (spellId == null) { return null; }
            return spellScrolls.TryGetValue(spellId.Trim().ToLower(), out ItemInfo info) ? info : null;
        }

        /* Resolves a content objects inventory (record id and quanity) to actual ItemInfo objects */
        /* Also truncates inventory to 10 slots which is the max size for an ItemLot param chain! */
        public List<(ItemInfo item, int quantity)> ResolveInventory(Content content)
        {
            List<(string id, int quantity)> inv;
            switch (content)
            {
                case CharacterContent npc: inv = npc.inventory; break;
                case ContainerContent cnt: inv = cnt.inventory; break;
                default: throw new Exception($"Content type of '{content.type}' cannot have an inventory to resolve!");
            }

            const int MAX_INV = 10;

            if (inv == null || inv.Count() <= 0) { return new(); }

            List<(ItemInfo item, int quantity)> inventory = new();

            // A regular contains/map check won't cut it here. Need to check values. Multiple ItemInfo objects can point at the same param row */
            void AddOrIncrement(List<(ItemInfo item, int quantity)> list, ItemInfo itemInfo)
            {
                if (itemInfo == null) { return; } // skip blank entries
                for(int i=0;i<list.Count();i++)
                {
                    (ItemInfo item, int quantity) entry = list[i];
                    if (entry.item.type == itemInfo.type && entry.item.row == itemInfo.row)
                    {
                        list.RemoveAt(i);
                        list.Add((entry.item, entry.quantity + 1));  // can't increment value in a tuple because fuck
                        return;
                    }
                }
                list.Add((itemInfo, 1));
            }

            foreach ((string id, int quantity) entry in inv)
            {
                for (int i = 0; i < entry.quantity; i++)
                {
                    ItemInfo itemInfo = GetItem(entry.id); // getitem resolves leveled lists so a single record id CAN return multiple different items!
                    AddOrIncrement(inventory, itemInfo);
                }
            }

            /* Convert gold_001 to rune items, @TODO: create custom sized ones flavored as "coin pouch" later */
            for (int i = 0; i < inventory.Count(); i++)
            {
                (ItemInfo item, int quantity) entry = inventory[i];
                if (entry.item.id.ToLower() == "gold_001") {
                    inventory.RemoveAt(i--);
                    ItemInfo rune = new ItemInfo("TEMP_HACK_TODO_GOLDEN_RUNE_ONE", Type.Goods, 2900, 200, false, ItemInfo.OriginalType.MiscItem);
                    inventory.Add((rune, 1)); // @TODO: temporary. see above
                    break;
                }
            }

            /* If the content is an NPC we resolve their equipment BEFORE truncating their inventory. This prevents npcs with big inventories from losing their clothes */
            if (content is NpcContent) { ResolveEquipment(content as NpcContent, inventory); }

            /* Truncate inventory if it exceeds max size */
            /* We prioritize quest items (script referenced item records) first, then randomly choose from remaining pool what gets included */
            if (inventory.Count() > MAX_INV)
            {
                List<(ItemInfo item, int quantity)> truncated = new();

                for(int i=0;i<inventory.Count();i++)
                {
                    (ItemInfo item, int quantity) entry = inventory[i];
                    if (entry.item.quest) { truncated.Add(entry); inventory.RemoveAt(i--); }
                }

                while(truncated.Count() < MAX_INV && inventory.Count() > 0)
                {
                    int roll = Utility.RandomRange(0, inventory.Count());
                    (ItemInfo item, int quantity) entry = inventory[roll];
                    inventory.RemoveAt(roll);
                    truncated.Add(entry);
                }

                if(truncated.Count > 10)
                {
                    Lort.Log($"Inventory excceded max possible size [{truncated.Count}/10]! Truncating!", Lort.Type.Debug);
                    truncated = truncated.GetRange(0, 10);
                }

                inventory = truncated;
            }

            return inventory;
        }

        /* Resolve equipment slots for NpcContent */
        public void ResolveEquipment(NpcContent npc, List<(ItemInfo item, int quantity)> inventory)
        {
            List<ItemManager.ItemInfo> oneHand = new(), acc = new(), goods = new();
            ItemManager.ItemInfo twoHand = null, ranged = null, arrow = null, bolt = null, shield = null;
            ItemManager.ItemInfo head = null, body = null, hands = null, legs = null;

            foreach ((ItemInfo item, int quantity) entry in inventory)
            {
                switch (entry.item.info)
                {
                    case ItemInfo.OriginalType.AxeOneHand:
                    case ItemInfo.OriginalType.BluntOneHand:
                    case ItemInfo.OriginalType.LongBladeOneHand:
                    case ItemInfo.OriginalType.ShortBladeOneHand:
                        oneHand.Add(entry.item);
                        break;
                    case ItemInfo.OriginalType.AxeTwoHand:
                    case ItemInfo.OriginalType.BluntTwoClose:
                    case ItemInfo.OriginalType.BluntTwoWide:
                    case ItemInfo.OriginalType.LongBladeTwoClose:
                    case ItemInfo.OriginalType.SpearTwoWide:
                        if (twoHand != null && entry.item.value > twoHand.value) { twoHand = entry.item; }
                        else if (twoHand == null) { twoHand = entry.item; }
                        break;
                    case ItemInfo.OriginalType.MarksmanBow:
                    case ItemInfo.OriginalType.MarksmanCrossbow:
                        if (ranged != null && entry.item.value > ranged.value) { ranged = entry.item; }
                        else if (ranged == null) { ranged = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Arrow:
                        if (arrow != null && entry.item.value > arrow.value) { arrow = entry.item; }
                        else if (arrow == null) { arrow = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Bolt:
                        if (bolt != null && entry.item.value > bolt.value) { bolt = entry.item; }
                        else if (bolt == null) { bolt = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Shield:
                        if (shield != null && entry.item.value > shield.value) { shield = entry.item; }
                        else if (shield == null) { shield = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Helmet:
                        if (head != null && entry.item.value > head.value) { head = entry.item; }
                        else if (head == null) { head = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Cuirass:
                    case ItemInfo.OriginalType.Shirt:
                    case ItemInfo.OriginalType.Robe:
                        if (body != null && entry.item.value > body.value) { body = entry.item; }
                        else if (body == null) { body = entry.item; }
                        break;
                    case ItemInfo.OriginalType.LeftBracer:
                    case ItemInfo.OriginalType.RightBracer:
                    case ItemInfo.OriginalType.LeftGlove:
                    case ItemInfo.OriginalType.RightGlove:
                        if (hands != null && entry.item.value > hands.value) { hands = entry.item; }
                        else if (hands == null) { hands = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Greaves:
                    case ItemInfo.OriginalType.Boots:
                    case ItemInfo.OriginalType.Pants:
                    case ItemInfo.OriginalType.Skirt:
                    case ItemInfo.OriginalType.Shoes:
                        if (legs != null && entry.item.value > legs.value) { legs = entry.item; }
                        else if (legs == null) { legs = entry.item; }
                        break;
                    case ItemInfo.OriginalType.Amulet:
                    case ItemInfo.OriginalType.Ring:
                    case ItemInfo.OriginalType.Belt:
                        acc.Add(entry.item);
                        break;
                    case ItemInfo.OriginalType.Alchemy:
                    case ItemInfo.OriginalType.MarksmanThrown:
                        goods.Add(entry.item);
                        break;
                }
            }

            oneHand = oneHand.OrderByDescending(i => i.value).ToList();
            acc = acc.OrderByDescending(i => i.value).ToList();
            goods = goods.OrderByDescending(i => i.value).ToList();

            ItemInfo rightHand, leftHand;
            // Go twohanded big weapon
            if (twoHand != null && twoHand.value > (oneHand.FirstOrDefault()?.value ?? 0)) { rightHand = twoHand; leftHand = null; }
            // Go dual short blades
            else if (oneHand.Count() >= 2 && oneHand[0].info == ItemInfo.OriginalType.ShortBladeOneHand && oneHand[1].info == ItemInfo.OriginalType.ShortBladeOneHand) { rightHand = oneHand[0]; leftHand = oneHand[1]; }
            // One handed weapon and shield (if we have a shield)
            else if (oneHand.Count() >= 1) { rightHand = oneHand[0]; leftHand = shield; }
            // Bare hands I guess!
            else { rightHand = null; leftHand = null; }

            // Set npc equip slots now
            npc.equipWeaponRight = rightHand;
            npc.equipWeaponLeft = leftHand;
            npc.equipRange = ranged;
            npc.equipHead = head;
            npc.equipBody = body;
            npc.equipHands = hands;
            npc.equipLegs = legs;
            npc.equipArrow = arrow;
            npc.equipBolt = bolt;
            npc.equipAcc = acc[..Math.Min(acc.Count(), 4)].ToArray();
            npc.equipGood = goods[..Math.Min(goods.Count(), 6)].ToArray();
        }

        /* Creates params for a barter shop from a list of item record ids and returns the base row */
        public int CreateShop(List<(string id, int quantity)> inventory)
        {
            if (inventory == null || inventory.Count() < 0) { return -1; } // no shop!

            /* Resolve inventory to a list of ItemInfo objects. This has to be done before creating rows because of Leveled Lists of quanties > 1 resolving to an unknown number of rows */
            List<ItemInfo> itemsToSell = new();

            // A regular contains check won't cut it here. Need to check values. Multiple ItemInfo objects can point at the same param row */
            void AddExclusive(List<ItemInfo> list, ItemInfo entry)
            {
                if (entry == null) { return; } // skip blank entries
                if (entry.id.ToLower() == "gold_001") { return; } // blacklist gold from shops (lol)
                foreach (ItemInfo ii in list)
                {
                    if(ii.type == entry.type && ii.row == entry.row)
                    {
                        return;
                    }
                }
                list.Add(entry);
            }

            foreach((string id, int quantity) invItem in inventory)
            {
                for(int i=0;i<invItem.quantity;i++)
                {
                    ItemInfo itemInfo = GetItem(invItem.id);  // getitem resolves leveled lists so a single record id CAN return multiple different items!
                    AddExclusive(itemsToSell, itemInfo);
                }
            }

            if (itemsToSell.Count <= 0) { return -1; } // empty shop, discard!

            if (itemsToSell.Count > 99) // uh-oh!
            {
                Lort.Log($"ShopParam excceded max possible size [{itemsToSell.Count}/99]! Truncating!", Lort.Type.Debug);
                itemsToSell = itemsToSell.GetRange(0, 99); // @TODO: Better truncation method please! Discard less useful items instead of chopping!
            } 

            /* Create shop */
            int baseRow = nextShopId;
            FsParam shopParam = paramanager.param[Paramanager.ParamType.ShopLineupParam];
            int j = 0;
            foreach(ItemInfo item in itemsToSell)
            {
                FsParam.Row row = paramanager.CloneRow(shopParam[1], $"Shop::{item.type}::{item.id}", baseRow + j); // 1 is something default-ish idk. we just filling this out fully

                row["equipId"].Value.SetValue(item.row);
                row["equipType"].Value.SetValue((byte)item.EquipType());
                row["value"].Value.SetValue((int)Math.Max(1, item.value * Const.MERCANTILE_BUY_SCALE));

                paramanager.AddOrReplaceRow(shopParam, row);

                j++;
            }

            nextShopId += 100;
            return baseRow;
        }

        /* Creates params for a spell shop from a list of spell record ids and returns the base row */
        public int CreateShop(List<string> spells)
        {
            if (spells == null || spells.Count() < 0) { return -1; } // no shop!

            /* Resolve spell ids to actual spellinfo objects */
            List<SpellManager.SpellInfo> spellsToSell = new();
            foreach(string spell in spells)
            {
                SpellManager.SpellInfo spellInfo = spellManager.GetSpell(spell);
                if(spellInfo != null) { spellsToSell.Add(spellInfo); }
            }

            if (spellsToSell.Count <= 0) { return -1; } // empty shop, discard!

            if (spellsToSell.Count > 99) // uh-oh!
            {
                Lort.Log($"ShopParam excceded max possible size [{spellsToSell.Count}/99]! Truncating!", Lort.Type.Debug);
                spellsToSell = spellsToSell.GetRange(0, 99); // @TODO: Better truncation method please! Discard less useful items instead of chopping!
            }

            /* Create shop */
            int baseRow = nextShopId;
            FsParam shopParam = paramanager.param[Paramanager.ParamType.ShopLineupParam];
            int j = 0;
            foreach (SpellManager.SpellInfo spell in spellsToSell)
            {
                FsParam.Row row = paramanager.CloneRow(shopParam[1], $"Shop::Magic::{spell.id}", baseRow + j); // 1 is something default-ish idk. we just filling this out fully

                row["equipId"].Value.SetValue(spell.row);
                row["equipType"].Value.SetValue((byte)3);  // goods
                row["value"].Value.SetValue((int)Math.Max(1, spell.value));

                paramanager.AddOrReplaceRow(shopParam, row);

                j++;
            }

            nextShopId += 100;
            return baseRow;
        }

        /* Creates params for an enchant shop, parameter determines the max quality of enchantments provided */
        public int CreateShop(CharacterContent.Stats.Tier tier)
        {
            /* Randomly select skills to provide based on tier */
            List<Override.SkillInfo> skillPool = Override.GetSkills(tier); // this method creates a new list so we can modify it without issue
            int numItems;
            switch(tier)
            {
                case CharacterContent.Stats.Tier.Novice: numItems = Utility.RandomRange(1, 2); break;
                case CharacterContent.Stats.Tier.Apprentice: numItems = Utility.RandomRange(3, 4); break;
                case CharacterContent.Stats.Tier.Journeyman: numItems = Utility.RandomRange(5, 7); break;
                case CharacterContent.Stats.Tier.Expert: numItems = Utility.RandomRange(8, 11); break;
                case CharacterContent.Stats.Tier.Master: numItems = Utility.RandomRange(13, 18); break;
                default: throw new Exception($"Invalid skill tier: {tier}"); // can't happen
            }

            while(skillPool.Count() > numItems)
            {
                int roll = Utility.RandomRange(0, skillPool.Count());
                skillPool.RemoveAt(roll);
            }

            /* Create shop */
            int baseRow = nextShopId;
            FsParam shopParam = paramanager.param[Paramanager.ParamType.ShopLineupParam];
            int j = 0;
            foreach (Override.SkillInfo skill in skillPool)
            {
                FsParam.Row row = paramanager.CloneRow(shopParam[1], $"Shop::Gem::{skill.row}", baseRow + j); // 1 is something default-ish idk. we just filling this out fully

                row["equipId"].Value.SetValue(skill.row);
                row["equipType"].Value.SetValue((byte)4);  // gem
                row["value"].Value.SetValue((int)Math.Max(1, skill.value));

                paramanager.AddOrReplaceRow(shopParam, row);

                j++;
            }

            nextShopId += 100;
            return baseRow;
        }

        /* Item leveled list */
        [DebuggerDisplay("Leveled List :: {id}")]
        public class LeveledList
        {
            public readonly string id;  // morrowind record id
            public readonly int chance; // chance for no item at all

            private int weight; // total weight of all entries
            private readonly List<(ItemInfo item, int level)> list;  // item and level requirement to roll it

            public LeveledList(string id, int chance)
            {
                this.id = id;
                this.chance = chance;
                list = new();

                weight = 0;
            }

            public void Add(ItemInfo item, int level)
            {
                int corrected = Math.Min(Math.Max(1, level), 30);
                weight += corrected;
                list.Add((item, corrected));
            }

            /* Resolves the leveled list statically using level requirements as weighting for chance */
            public ItemInfo Get()
            {
                if (list.Count <= 0) { return null; }  // empty lists are a theoretical possibility
                if (Utility.RandomRange(0, 100) < chance) { return null; }  // chance is a chance for no item at all so resolve that first

                int w = 0;
                int roll = Utility.RandomRange(0, weight);
                foreach((ItemInfo item, int level) entry in list)
                {
                    w += entry.level;

                    if (roll < w) { return entry.item; }
                }

                return list[^1].item; // return last entry, i think this is unreachable but good fallback incase
            }

            public List<ItemInfo> Possibilites()
            {
                return list.Select(tuple => tuple.item).ToList();
            }
        }

        /* Stores info on an item */
        [DebuggerDisplay("Item :: {id} :: {type}->{row}")]
        public class ItemInfo
        {
            public enum OriginalType  // type from morrowind record
            {
                // Weapon types
                Arrow, AxeOneHand, AxeTwoHand, BluntOneHand, BluntTwoClose, BluntTwoWide, Bolt, LongBladeOneHand, LongBladeTwoClose, MarksmanBow, 
                MarksmanCrossbow, MarksmanThrown, ShortBladeOneHand, SpearTwoWide,
                // Armor types
                Boots, Cuirass, Greaves, Helmet, LeftBracer, LeftGauntlet, LeftPauldron, RightBracer, RightGauntlet, RightPauldron, Shield,
                // Clothing types
                Amulet, Belt, LeftGlove, Pants, RightGlove, Ring, Robe, Shirt, Shoes, Skirt,
                // Regular item types
                Ingredient, Alchemy, Book, MiscItem, Apparatus, Probe, Lockpick, RepairItem
            }

            public readonly Type type;  // param type
            public readonly int row;    // param row
            public readonly string id;  // morrowind record id
            public readonly int value;  // value for shops to use
            public readonly bool quest; // item is referenced in a script. this doesn't 100% mean its a quest item but it does mean it's needed to compile scripts
            public readonly OriginalType info;

            public ItemInfo(string id, Type type, int row, int value, bool quest, JsonNode json)
            {
                this.id = id;
                this.type = type;
                this.row = row;
                this.value = value;
                this.quest = quest;

                /* Extract original type from json */
                string recordType = json["type"].GetValue<string>().ToLower().Trim();
                string itemType;
                if (recordType == "weapon") { itemType = json["data"]["weapon_type"].GetValue<string>(); }
                else if (recordType == "armor") { itemType = json["data"]["armor_type"].GetValue<string>(); }
                else if (recordType == "clothing") { itemType = json["data"]["clothing_type"].GetValue<string>(); }
                else { itemType = json["type"].GetValue<string>(); }

                info = Enum.Parse<OriginalType>(itemType);
            }

            public ItemInfo(string id, Type type, int row, int value, bool quest, ItemInfo.OriginalType info)
            {
                this.id = id;
                this.type = type;
                this.row = row;
                this.value = value;
                this.quest = quest;
                this.info = info;
            }

            /* Returns the correct int value for an equipType param field. */
            public int EquipType()
            {
                switch (type)
                {
                    case ItemManager.Type.Weapon:
                        return 0;
                    case ItemManager.Type.Armor:
                        return 1;
                    case ItemManager.Type.Accessory:
                        return 2;
                    case ItemManager.Type.Goods:
                        return 3;
                    case ItemManager.Type.Enchant:
                        return 4;
                    case ItemManager.Type.CustomWeapon:
                        return 5;
                    default:
                        throw new Exception("Item had invalid type! This should NEVER happen!");
                }
            }

            public int ItemLotCategory()
            {
                switch (type)
                {
                    case ItemManager.Type.Weapon:
                        return 2;
                    case ItemManager.Type.Armor:
                        return 3;
                    case ItemManager.Type.Goods:
                        return 1;
                    case ItemManager.Type.Accessory:
                        return 4;
                    case ItemManager.Type.Enchant:
                        return 5;
                    case ItemManager.Type.CustomWeapon:
                        return 6;
                    default:
                        throw new Exception("Item had invalid type! This should NEVER happen!");
                }
            }
        }
    }
}
