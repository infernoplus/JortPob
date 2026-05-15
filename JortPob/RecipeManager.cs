using JortPob.Scripts;
using System;
using System.Collections.Generic;
using WitchyFormats;

namespace JortPob
{
    public class RecipeManager
    {
        private Dictionary<CharacterContent.Stats.Tier, RecipeBookInfo> books;

        private int nextGoodsId, nextShopId;

        private int recipeBookShopId, nextRecipeId;

        public RecipeManager(Paramanager paramanager, ScriptManager scriptManager, ItemManager itemManager, TextManager textManager)
        {
            nextGoodsId = 60000; // item manager uses 30000 to 50000 as its id range
            nextShopId = 1900000; // item manager uses 1700000 to 1900000 as its id range
            nextRecipeId = 30000;

            /* Create recipe books for each tier of alchemy recipes */
            books = new();
            List<(CharacterContent.Stats.Tier tier, int value)> tiers = new()
            {
                (CharacterContent.Stats.Tier.Novice, 35),
                (CharacterContent.Stats.Tier.Apprentice, 155),
                (CharacterContent.Stats.Tier.Journeyman, 475),
                (CharacterContent.Stats.Tier.Expert, 1975),
                (CharacterContent.Stats.Tier.Master, 87235)
            };

            foreach((CharacterContent.Stats.Tier tier, int value) tuple in tiers)
            {
                FsParam goodsParam = paramanager.param[Paramanager.ParamType.EquipParamGoods];
                FsParam.Row row = paramanager.CloneRow(goodsParam[8859], $"Alchemy Cookbook [{tuple.tier.ToString()}]", nextGoodsId); // 9384 is the perfumers cookbook 1

                textManager.AddGoods(row.ID, $"Alchemy Recipebook [{tuple.tier.ToString()}]", "Summaries summarize things!", "Descriptions describe things!", "More information!");

                row["rarity"].Value.SetValue((byte)0);

                paramanager.AddOrReplaceRow(goodsParam, row);
                RecipeBookInfo recipeBook = new(scriptManager, row.ID, tuple.tier, tuple.value);
                books.Add(tuple.tier, recipeBook);

                nextGoodsId += 10;
            }

            /* Create recipebook shop */
            int baseRow = nextShopId;
            FsParam shopParam = paramanager.param[Paramanager.ParamType.ShopLineupParam];
            int j = 0;
            foreach (var kvp in books)
            {
                RecipeBookInfo book = kvp.Value;
                FsParam.Row row = paramanager.CloneRow(shopParam[1], $"Shop::RecipeBook::{book.row}", baseRow + j); // 1 is something default-ish idk. we just filling this out fully

                row["equipId"].Value.SetValue(book.row);
                row["equipType"].Value.SetValue((byte)3);  // goods
                row["value"].Value.SetValue((int)Math.Max(1, book.value));
                row["sellQuantity"].Value.SetValue((short)1);
                row["eventFlag_forRelease"].Value.SetValue(book.visible.id);
                row["eventFlag_forStock"].Value.SetValue(book.purchased.id);

                paramanager.AddOrReplaceRow(shopParam, row);

                j++;
            }

            recipeBookShopId = baseRow;
            nextShopId += 100;

            /* Create recipe params */
            FsParam recipeParam = paramanager.param[Paramanager.ParamType.ShopLineupParam_Recipe];
            FsParam materialParam = paramanager.param[Paramanager.ParamType.EquipMtrlSetParam];
            foreach (Override.AlchemyInfo recipe in Override.GetAlchemy())
            {
                baseRow = nextRecipeId;
                ItemManager.ItemInfo output = itemManager.GetItem(recipe.id);
                List<ItemManager.ItemInfo> inputs = itemManager.GetItems(recipe.ingredients);

                /* Material set param */
                FsParam.Row materialRow = paramanager.CloneRow(materialParam[0], $"{recipe.tier}::{recipe.id}", baseRow * 10); // 0 is some blankish row

                /* Default values */
                for(int i = 0; i < 5; i++)
                {
                    materialRow[$"materialId{i + 1:D2}"].Value.SetValue(-1);
                    materialRow[$"itemNum{i + 1:D2}"].Value.SetValue((sbyte)-1);
                    materialRow[$"materialCate{i + 1:D2}"].Value.SetValue((byte)0); // none
                }

                /* Actual values */
                for (int i = 0; i < inputs.Count; i++)
                {
                    ItemManager.ItemInfo input = inputs[i];
                    materialRow[$"materialId{i+1:D2}"].Value.SetValue(input.row);
                    materialRow[$"itemNum{i+1:D2}"].Value.SetValue((sbyte)1);
                    materialRow[$"materialCate{i+1:D2}"].Value.SetValue((byte)4); // good
                }

                paramanager.AddOrReplaceRow(materialParam, materialRow);

                /* Recipe param */
                FsParam.Row recipeRow = paramanager.CloneRow(recipeParam[1], $"{recipe.tier}::{recipe.id}", baseRow); // 1 is another blankish row

                recipeRow["equipId"].Value.SetValue(output.row);
                recipeRow["equipType"].Value.SetValue((byte)3); // goods
                recipeRow["mtrlId"].Value.SetValue(materialRow.ID);
                recipeRow["value"].Value.SetValue(0);
                recipeRow["eventFlag_forRelease"].Value.SetValue(books[recipe.tier].visible.id);

                paramanager.AddOrReplaceRow(recipeParam, recipeRow);

                nextRecipeId += 10;
            }

            /* Final thing, generate a script in common for managing when the player can or cannot craft */
            List<ItemManager.ItemInfo> alchemyTools = itemManager.GetItems([
                "apparatus_a_mortar_01",
                "apparatus_j_mortar_01",
                "apparatus_m_mortar_01",
                "apparatus_g_mortar_01",
                "apparatus_sm_mortar_01"
            ]);
            scriptManager.common.CreateAlchemyHandler(alchemyTools);
            textManager.EditMenuText(101009, "Alchemy");
        }

        /* Get book */
        public RecipeBookInfo GetBook(CharacterContent.Stats.Tier tier) {
            return books[tier];
        }

        /* Returns id to alchemy recipebook shop */
        public int GetShop()
        {
            return recipeBookShopId;
        }

        /* Cookbook-ish object that unlocks a tier of recipes */
        public class RecipeBookInfo
        {
            public readonly int row;
            public readonly CharacterContent.Stats.Tier tier;
            public readonly Script.Flag visible, purchased;

            public readonly int value; // when buying from a shop

            public RecipeBookInfo(ScriptManager scriptManager, int row, CharacterContent.Stats.Tier tier, int value)
            {
                this.row = row;
                this.tier = tier;

                this.value = value;

                visible = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Item, $"Alchemy Cookbook [{tier.ToString()}]");
                purchased = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.ItemVisibility, $"Alchemy Cookbook [{tier.ToString()}]");
            }
        }
    }
}
