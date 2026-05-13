using JortPob.Common;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JortPob
{
    public class SpellManager
    {
        private List<SpellInfo> spells;

        public SpellManager(ESM esm, Paramanager paramanager, TextManager textManager)
        {
            spells = new();

            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.Spell))
            {
                string id = json["id"].GetValue<string>().ToLower();
                int cost = json["data"]["cost"].GetValue<int>(); // mana cost. used to calculate value to buy spell

                Override.SpellRemap remap = Override.GetSpellRemap(id);
                if (remap != null)
                {
                    SpellInfo spellInfo = new(id, remap.row, (int)(cost * Const.MERCANTILE_SPELL_VALUE_SCALE));

                    if (remap.HasTextChanges())
                    {
                        textManager.RenameGoods(spellInfo.row, remap.text.name, remap.text.summary, remap.text.description, remap.text.effect);
                    }

                    spells.Add(spellInfo);
                }
            }
        }

        public SpellInfo GetSpell(string id)
        {
            foreach (SpellInfo spell in spells)
            {
                if (spell.id == id.ToLower()) { return spell; }
            }
            return null;
        }

        public class SpellInfo
        {
            public readonly string id;
            public readonly int row;

            public readonly int value; // when buying from a shop

            public SpellInfo(string id, int row, int value)
            {
                this.id = id.ToLower();
                this.row = row;

                this.value = value;
            }
        }
    }
}
