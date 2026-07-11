using FSParam;
using JortPob.Common;
using JortPob.Scripts;
using JortPob.Worker;
using SoulsFormats;
using SoulsFormats.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using WitchyFormats;

namespace JortPob
{
    public class Paramanager
    {
        public enum ParamType
        {
            ActionButtonParam, AiSoundParam, AssetEnvironmentGeometryParam, AssetMaterialSfxParam, AssetModelSfxParam, AtkParam_Npc, AtkParam_Pc, AttackElementCorrectParam,
            AutoCreateEnvSoundParam, BaseChrSelectMenuParam, BehaviorParam, BehaviorParam_PC, BonfireWarpParam, BonfireWarpSubCategoryParam, BonfireWarpTabParam, BuddyParam,
            BuddyStoneParam, BudgetParam, Bullet, BulletCreateLimitParam, CalcCorrectGraph, Ceremony, CharaInitParam, CharMakeMenuListItemParam, CharMakeMenuTopParam,
            ChrActivateConditionParam, ChrEquipModelParam, ChrModelParam, ClearCountCorrectParam, CoolTimeParam, CutsceneGparamTimeParam, CutsceneGparamWeatherParam, CutsceneMapIdParam,
            CutSceneTextureLoadParam, CutsceneTimezoneConvertParam, CutsceneWeatherOverrideGparamConvertParam, DecalParam, DirectionCameraParam, EnemyCommonParam, EnvObjLotParam,
            EquipMtrlSetParam, EquipParamAccessory, EquipParamCustomWeapon, EquipParamGem, EquipParamGoods, EquipParamProtector, EquipParamWeapon, FaceParam, FaceRangeParam, FeTextEffectParam,
            FinalDamageRateParam, FootSfxParam, GameAreaParam, GameSystemCommonParam, GestureParam, GparamRefSettings, GraphicsCommonParam, GraphicsConfig, GrassLodRangeParam, GrassTypeParam,
            GrassTypeParam_Lv1, GrassTypeParam_Lv2, HitEffectSeParam, HitEffectSfxConceptParam, HitEffectSfxParam, HitMtrlParam, HPEstusFlaskRecoveryParam, ItemLotParam_enemy,
            ItemLotParam_map, KeyAssignMenuItemParam, KeyAssignParam_TypeA, KeyAssignParam_TypeB, KeyAssignParam_TypeC, KnockBackParam, KnowledgeLoadScreenItemParam,
            LegacyDistantViewPartsReplaceParam, LoadBalancerDrawDistScaleParam, LoadBalancerDrawDistScaleParam_ps4, LoadBalancerDrawDistScaleParam_ps5, LoadBalancerDrawDistScaleParam_xb1,
            LoadBalancerDrawDistScaleParam_xb1x, LoadBalancerDrawDistScaleParam_xss, LoadBalancerDrawDistScaleParam_xsx, LoadBalancerNewDrawDistScaleParam_ps4,
            LoadBalancerNewDrawDistScaleParam_ps5, LoadBalancerNewDrawDistScaleParam_win64, LoadBalancerNewDrawDistScaleParam_xb1, LoadBalancerNewDrawDistScaleParam_xb1x,
            LoadBalancerNewDrawDistScaleParam_xss, LoadBalancerNewDrawDistScaleParam_xsx, LoadBalancerParam, LockCamParam, Magic, MapDefaultInfoParam, MapGdRegionDrawParam,
            MapGdRegionInfoParam, MapGridCreateHeightDetailLimitInfo, MapGridCreateHeightLimitInfoParam, MapMimicryEstablishmentParam, MapNameTexParam, MapNameTexParam_m61,
            MapPieceTexParam, MapPieceTexParam_m61, MaterialExParam, MenuColorTableParam, MenuCommonParam, MenuOffscrRendParam, MenuPropertyLayoutParam, MenuPropertySpecParam,
            MenuValueTableParam, MimicryEstablishmentTexParam, MimicryEstablishmentTexParam_m61, MoveParam, MPEstusFlaskRecoveryParam, MultiHPEstusFlaskBonusParam,
            MultiMPEstusFlaskBonusParam, MultiPlayCorrectionParam, MultiSoulBonusRateParam, NetworkAreaParam, NetworkMsgParam, NetworkParam, NpcAiActionParam, NpcAiBehaviorProbability,
            NpcParam, NpcThinkParam, ObjActParam, PartsDrawParam, PhantomParam, PlayerCommonParam, PlayRegionParam, PostureControlParam_Gender, PostureControlParam_Pro,
            PostureControlParam_WepLeft, PostureControlParam_WepRight, RandomAppearParam, ReinforceParamProtector, ReinforceParamWeapon, ResistCorrectParam, RideParam, RoleParam,
            RollingObjLotParam, RuntimeBoneControlParam, SeActivationRangeParam, SeMaterialConvertParam, SfxBlockResShareParam, ShopLineupParam, ShopLineupParam_Recipe, SignPuddleParam,
            SignPuddleSubCategoryParam, SignPuddleTabParam, SoundAssetSoundObjEnableDistParam, SoundAutoEnvSoundGroupParam, SoundAutoReverbEvaluationDistParam, SoundAutoReverbSelectParam,
            SoundChrPhysicsSeParam, SoundCommonIngameParam, SoundCutsceneParam, SpeedtreeParam, SpEffectParam, SpEffectSetParam, SpEffectVfxParam, SwordArtsParam, TalkParam,
            ThrowDirectionSfxParam, ThrowParam, ToughnessParam, TutorialParam, WaypointParam, WeatherAssetCreateParam, WeatherAssetReplaceParam, WeatherLotParam, WeatherLotTexParam,
            WeatherLotTexParam_m61, WeatherParam, WepAbsorpPosParam, WetAspectParam, WhiteSignCoolTimeParam, WorldMapLegacyConvParam, WorldMapPieceParam, WorldMapPlaceNameParam,
            WorldMapPointParam, WwiseValueToStrParam_BgmBossChrIdConv, WwiseValueToStrParam_EnvPlaceType, WwiseValueToStrParam_Switch_AttackStrength, WwiseValueToStrParam_Switch_AttackType,
            WwiseValueToStrParam_Switch_DamageAmount, WwiseValueToStrParam_Switch_DeffensiveMaterial, WwiseValueToStrParam_Switch_GrassHitType, WwiseValueToStrParam_Switch_HitStop,
            WwiseValueToStrParam_Switch_OffensiveMaterial, WwiseValueToStrParam_Switch_PlayerEquipmentBottoms, WwiseValueToStrParam_Switch_PlayerEquipmentTops,
            WwiseValueToStrParam_Switch_PlayerShoes, WwiseValueToStrParam_Switch_PlayerVoiceType
        }

        public enum ParamDefType
        {
            ACTIONBUTTON_PARAM_ST, AI_SOUND_PARAM_ST, ASSET_GEOMETORY_PARAM_ST, ASSET_MATERIAL_SFX_PARAM_ST, ASSET_MODEL_SFX_PARAM_ST, ATK_PARAM_ST,
            ATTACK_ELEMENT_CORRECT_PARAM_ST, AUTO_CREATE_ENV_SOUND_PARAM_ST, BASECHR_SELECT_MENU_PARAM_ST, BEHAVIOR_PARAM_ST, BONFIRE_WARP_PARAM_ST,
            BONFIRE_WARP_SUB_CATEGORY_PARAM_ST, BONFIRE_WARP_TAB_PARAM_ST, BUDDY_PARAM_ST, BUDDY_STONE_PARAM_ST, BUDGET_PARAM_ST, BULLET_PARAM_ST,
            BULLET_CREATE_LIMIT_PARAM_ST, CACL_CORRECT_GRAPH_ST, CEREMONY_PARAM_ST, CHARACTER_INIT_PARAM, CHARMAKEMENU_LISTITEM_PARAM_ST, CHARMAKEMENUTOP_PARAM_ST,
            CHR_ACTIVATE_CONDITION_PARAM_ST, CHR_EQUIP_MODEL_PARAM_ST, CHR_MODEL_PARAM_ST, CLEAR_COUNT_CORRECT_PARAM_ST, COOL_TIME_PARAM_ST,
            CUTSCENE_GPARAM_TIME_PARAM_ST, CUTSCENE_GPARAM_WEATHER_PARAM_ST, CUTSCENE_MAP_ID_PARAM_ST, CUTSCENE_TEXTURE_LOAD_PARAM_ST,
            CUTSCENE_TIMEZONE_CONVERT_PARAM_ST, CUTSCENE_WEATHER_OVERRIDE_GPARAM_ID_CONVERT_PARAM_ST, DECAL_PARAM_ST, DIRECTION_CAMERA_PARAM_ST,
            ENEMY_COMMON_PARAM_ST, ENV_OBJ_LOT_PARAM_ST, EQUIP_MTRL_SET_PARAM_ST, EQUIP_PARAM_ACCESSORY_ST, EQUIP_PARAM_CUSTOM_WEAPON_ST,
            EQUIP_PARAM_GEM_ST, EQUIP_PARAM_GOODS_ST, EQUIP_PARAM_PROTECTOR_ST, EQUIP_PARAM_WEAPON_ST, FACE_PARAM_ST, FACE_RANGE_PARAM_ST,
            FE_TEXT_EFFECT_PARAM_ST, FINAL_DAMAGE_RATE_PARAM_ST, FOOT_SFX_PARAM_ST, GAME_AREA_PARAM_ST, GAME_SYSTEM_COMMON_PARAM_ST, GESTURE_PARAM_ST,
            GPARAM_REF_SETTINGS_PARAM_ST, GRAPHICS_COMMON_PARAM_ST, CS_GRAPHICS_CONFIG_PARAM_ST, GRASS_LOD_RANGE_PARAM_ST, GRASS_TYPE_PARAM_ST,
            HIT_EFFECT_SE_PARAM_ST, HIT_EFFECT_SFX_CONCEPT_PARAM_ST, HIT_EFFECT_SFX_PARAM_ST, HIT_MTRL_PARAM_ST, ESTUS_FLASK_RECOVERY_PARAM_ST,
            ITEMLOT_PARAM_ST, CS_KEY_ASSIGN_MENUITEM_PARAM, KEY_ASSIGN_PARAM_ST, KNOCKBACK_PARAM_ST, KNOWLEDGE_LOADSCREEN_ITEM_PARAM_ST,
            LEGACY_DISTANT_VIEW_PARTS_REPLACE_PARAM, LOAD_BALANCER_DRAW_DIST_SCALE_PARAM_ST, LOAD_BALANCER_NEW_DRAW_DIST_SCALE_PARAM_ST,
            LOAD_BALANCER_PARAM_ST, LOCK_CAM_PARAM_ST, MAGIC_PARAM_ST, MAP_DEFAULT_INFO_PARAM_ST, MAP_GD_REGION_DRAW_PARAM, MAP_GD_REGION_ID_PARAM_ST,
            MAP_GRID_CREATE_HEIGHT_LIMIT_DETAIL_INFO_PARAM_ST, MAP_GRID_CREATE_HEIGHT_LIMIT_INFO_PARAM_ST, MAP_MIMICRY_ESTABLISHMENT_PARAM_ST,
            MAP_NAME_TEX_PARAM_ST, MAP_NAME_TEX_PARAM_ST_DLC02, MAP_PIECE_TEX_PARAM_ST, MAP_PIECE_TEX_PARAM_ST_DLC02, MATERIAL_EX_PARAM_ST,
            MENU_PARAM_COLOR_TABLE_ST, MENU_COMMON_PARAM_ST, MENU_OFFSCR_REND_PARAM_ST, MENUPROPERTY_LAYOUT, MENUPROPERTY_SPEC, MENU_VALUE_TABLE_SPEC,
            MIMICRY_ESTABLISHMENT_TEX_PARAM_ST, MIMICRY_ESTABLISHMENT_TEX_PARAM_ST_DLC02, MOVE_PARAM_ST, MULTI_ESTUS_FLASK_BONUS_PARAM_ST,
            MULTI_PLAY_CORRECTION_PARAM_ST, MULTI_SOUL_BONUS_RATE_PARAM_ST, NETWORK_AREA_PARAM_ST, NETWORK_MSG_PARAM_ST, NETWORK_PARAM_ST,
            NPC_AI_ACTION_PARAM_ST, NPC_AI_BEHAVIOR_PROBABILITY_PARAM_ST, NPC_PARAM_ST, NPC_THINK_PARAM_ST, OBJ_ACT_PARAM_ST, PARTS_DRAW_PARAM_ST,
            PHANTOM_PARAM_ST, PLAYER_COMMON_PARAM_ST, PLAY_REGION_PARAM_ST, POSTURE_CONTROL_PARAM_GENDER_ST, POSTURE_CONTROL_PARAM_PRO_ST,
            POSTURE_CONTROL_PARAM_WEP_LEFT_ST, POSTURE_CONTROL_PARAM_WEP_RIGHT_ST, RANDOM_APPEAR_PARAM_ST, REINFORCE_PARAM_PROTECTOR_ST,
            REINFORCE_PARAM_WEAPON_ST, RESIST_CORRECT_PARAM_ST, RIDE_PARAM_ST, ROLE_PARAM_ST, ROLLING_OBJ_LOT_PARAM_ST, RUNTIME_BONE_CONTROL_PARAM_ST,
            SE_ACTIVATION_RANGE_PARAM_ST, SE_MATERIAL_CONVERT_PARAM_ST, SFX_BLOCK_RES_SHARE_PARAM, SHOP_LINEUP_PARAM, SIGN_PUDDLE_PARAM_ST,
            SIGN_PUDDLE_SUB_CATEGORY_PARAM_ST, SIGN_PUDDLE_TAB_PARAM_ST, SOUND_ASSET_SOUND_OBJ_ENABLE_DIST_PARAM_ST, SOUND_AUTO_ENV_SOUND_GROUP_PARAM_ST,
            SOUND_AUTO_REVERB_EVALUATION_DIST_PARAM_ST, SOUND_AUTO_REVERB_SELECT_PARAM_ST, SOUND_CHR_PHYSICS_SE_PARAM_ST, SOUND_COMMON_INGAME_PARAM_ST,
            SOUND_CUTSCENE_PARAM_ST, SPEEDTREE_MODEL_PARAM_ST, SP_EFFECT_PARAM_ST, SP_EFFECT_SET_PARAM_ST, SP_EFFECT_VFX_PARAM_ST, SWORD_ARTS_PARAM_ST,
            TALK_PARAM_ST, THROW_DIRECTION_SFX_PARAM_ST, THROW_PARAM_ST, TOUGHNESS_PARAM_ST, TUTORIAL_PARAM_ST, WAYPOINT_PARAM_ST, WEATHER_ASSET_CREATE_PARAM_ST,
            WEATHER_ASSET_REPLACE_PARAM_ST, WEATHER_LOT_PARAM_ST, WEATHER_LOT_TEX_PARAM_ST, WEATHER_LOT_TEX_PARAM_ST_DLC02, WEATHER_PARAM_ST,
            WEP_ABSORP_POS_PARAM_ST, WET_ASPECT_PARAM_ST, WHITE_SIGN_COOL_TIME_PARAM_ST, WORLD_MAP_LEGACY_CONV_PARAM_ST, WORLD_MAP_PIECE_PARAM_ST,
            WORLD_MAP_PLACE_NAME_PARAM_ST, WORLD_MAP_POINT_PARAM_ST, WWISE_VALUE_TO_STR_CONVERT_PARAM_ST
        }

        public readonly Cache cache;
        public readonly TextManager textManager;

        public readonly Dictionary<ParamType, FsParam> param;
        public readonly LiveParam extendedTalkParam;

        public Dictionary<string, int> itemActionButtons; // string is the text of the button prompt, int is the row id

        public short terrainDrawParamID;
        private Dictionary<int, int> lodPartDrawParamIDs; // first int is the index of the array from Const.ASSET_LOD_VALUES, second int is the param row id
        private int nextMessageParam, nextMapItemLotId, nextEnemyItemLotId, nextActionButtonId, nextWeatherLotParamId;

        public Paramanager(Cache cache, TextManager textManager)
        {
            this.cache = cache;
            this.textManager = textManager;

            nextMessageParam = 3000;
            nextMapItemLotId = 120000;
            nextEnemyItemLotId = 720000000;
            nextActionButtonId = 300000;
            nextWeatherLotParamId = 500000000;

            itemActionButtons = new();

            SoulsFormats.BND4 paramBnd = RegulationDecryptor.DecryptERRegulation(Utility.ResourcePath(@"misc\regulation.bin"));
            string[] files = Directory.GetFiles(Utility.ResourcePath(@"misc\paramdefs"));

            Dictionary<ParamDefType, WitchyFormats.PARAMDEF> paramdefs = new();
            foreach (string file in files)
            {
                WitchyFormats.PARAMDEF p = WitchyFormats.PARAMDEF.XmlDeserialize(file);
                Lort.TaskIterate();
                try
                {
                    ParamDefType ty = Enum.Parse<ParamDefType>(p.ParamType);
                    paramdefs.Add(ty, p);
                }
                catch (Exception)
                {
                    Lort.Log($"Skipped unknown paramdef: {p.ParamType}", Lort.Type.Debug);
                    continue;
                }
            }

            param = ParamWorker.Go(paramBnd, paramdefs);

            /* Clear out most of the talk params to make room for our custom ones */
            /* Just keeping some important ones for opening cutscene */
            FsParam talkParam = param[ParamType.TalkParam];
            List<FsParam.Row> openingcustcenestuff = new();
            foreach (FsParam.Row row in talkParam.Rows)
            {
                if (row.ID > 1500080)
                    break;
                openingcustcenestuff.Add(row);
            }
            talkParam.ClearRows();
            foreach (FsParam.Row row in openingcustcenestuff) {
                talkParam.AddRow(row);
            }

            /* Load extenedTalkParam and do the same thing as above */
            foreach(BinderFile bf in paramBnd.Files) { if (Path.GetFileNameWithoutExtension(bf.Name) == ParamType.TalkParam.ToString()) { extendedTalkParam = LiveParam.Read(bf.Bytes); break; } }
            extendedTalkParam.ApplyParamdef(SoulsFormats.PARAMDEF.XmlDeserialize(Utility.ResourcePath(@"misc\paramdefs\TalkParam.xml")));

            List<LiveParam.Row> openingcustcenestuff2 = new();
            foreach (LiveParam.Row row in extendedTalkParam.Rows)
            {
                if (row.ID > 1500080)
                    break;
                openingcustcenestuff2.Add(row);
            }
            extendedTalkParam.ClearRows();
            foreach (LiveParam.Row row in openingcustcenestuff2)
            {
                extendedTalkParam.AddRow(row);
            }

            /* Clear out recipe and recipe material params */
            FsParam recipeParam = param[Paramanager.ParamType.ShopLineupParam_Recipe];
            FsParam materialParam = param[Paramanager.ParamType.EquipMtrlSetParam];
            List<FsParam.Row> keepRecipes = new();
            List<FsParam.Row> keepMaterials = new();
            keepRecipes.Add(recipeParam.Rows[0]); // just the one template row
            foreach(FsParam.Row row in materialParam.Rows)
            {
                if(row.ID >= 300000 && row.ID < 400000)
                {
                    continue;
                }
                keepMaterials.Add(row);
            }
            recipeParam.ClearRows();
            materialParam.ClearRows();
            foreach(FsParam.Row row in keepRecipes) { recipeParam.AddRow(row); }
            foreach(FsParam.Row row in keepMaterials) { materialParam.AddRow(row); }

            /* Clear out some itemlots to make space because idk dumb param limits. Also make a template row */
            FsParam itemLotParamMap = param[Paramanager.ParamType.ItemLotParam_map];
            List<FsParam.Row> keepMapLots = new();
            foreach (FsParam.Row row in itemLotParamMap.Rows)
            {
                if (row.ID >= 18000000 && row.ID < 19000000)
                {
                    keepMapLots.Add(row);
                }

                if(row.ID == 0)
                {
                    // Create blank template
                    row["lotItem_Rarity"].Value.SetValue((sbyte)-1);
                    row["GameClearOffset"].Value.SetValue((sbyte)-1);
                    row["getItemFlagId"].Value.SetValue((uint)0);
                    for(int i=1;i<=8;i++)
                    {
                        row[$"lotItemId{i:D2}"].Value.SetValue(0);
                        row[$"lotItemCategory{i:D2}"].Value.SetValue(0);
                        row[$"lotItemNum{i:D2}"].Value.SetValue((byte)0);
                        row[$"lotItemBasePoint{i:D2}"].Value.SetValue((ushort)0);
                        row[$"enableLuck{i:D2}"].Value.SetValue((ushort)0);
                    }
                    keepMapLots.Add(row);
                }
            }
            itemLotParamMap.ClearRows();
            foreach (FsParam.Row row in keepMapLots) { itemLotParamMap.AddRow(row); }

            /* Clear out mapheight params, these are 1000% not needed for the morrowind map. Just trust me */
            FsParam mapGridHeightParam = param[ParamType.MapGridCreateHeightLimitInfoParam];
            for (int i = 0; i < mapGridHeightParam.Rows.Count(); i++)
            {
                FsParam.Row row = mapGridHeightParam.Rows[i];
                if (row.ID >= 99999901) { continue; } // keep some base params
                mapGridHeightParam.RemoveRow(row);
                i--;
            }

            FsParam mapGridHeightDetailParam = param[ParamType.MapGridCreateHeightDetailLimitInfo];
            for (int i = 0; i < mapGridHeightDetailParam.Rows.Count(); i++)
            {
                FsParam.Row row = mapGridHeightDetailParam.Rows[i];
                if (row.ID <= 2) { continue; } // keep some base params
                mapGridHeightDetailParam.RemoveRow(row);
                i--;
            }

            /* Clear out WorldMapPointParam. We don't need any of these from the base game. */
            FsParam worldMapPointParam = param[ParamType.WorldMapPointParam];
            FsParam.Row worldMapPointParamTemplate = GetRow(worldMapPointParam, 61413800);   // grab stormhill shack as our template
            worldMapPointParamTemplate.ID = 1;
            worldMapPointParamTemplate["eventFlagId"].Value.SetValue(6000u); // always off
            worldMapPointParam.ClearRows();
            AddOrReplaceRow(worldMapPointParam, worldMapPointParamTemplate);

            /* Clear out WorldMapPlaceNaemParam. We don't need any of these from the base game. */
            FsParam worldMapPlaecNameParam = param[ParamType.WorldMapPlaceNameParam];
            FsParam.Row worldMapPlaecNameParamTemplate = GetRow(worldMapPlaecNameParam, 1);   // grab the blank one as a template
            worldMapPlaecNameParam.ClearRows();
            AddOrReplaceRow(worldMapPlaecNameParam, worldMapPlaecNameParamTemplate);

            /* Claer out all but 255 from the 3 MapTexParams */
            FsParam mapNameTexParam = param[ParamType.MapNameTexParam];
            FsParam mapPieceTexParam = param[ParamType.MapPieceTexParam];
            FsParam weatherTexParam = param[ParamType.WeatherLotTexParam];
            void KillAllBut255(FsParam param)
            {
                for(int i = 0; i < param.Rows.Count(); i++)
                {
                    FsParam.Row row = param.Rows[i];
                    if (row.ID != 255)
                    {
                        param.RemoveRow(row);
                        i--;
                    }
                }
            }
            KillAllBut255(mapNameTexParam);
            KillAllBut255(mapPieceTexParam);
            KillAllBut255(weatherTexParam);

            GC.Collect(); // maybe fixes a bug with fsparam. 80% sure
        }

        /* Adds row or replaces an existing row with a matching ID */
        public void AddOrReplaceRow(FsParam param, FsParam.Row row)
        {
            if (param.Rows.Count() >= ushort.MaxValue - 3) { throw new Exception($"{param.ParamType.ToString()} exceeded {ushort.MaxValue} rows!"); }

            FsParam.Row oldrow = param[row.ID];
            if (oldrow != null)
                param.RemoveRow(oldrow);
            param.AddRow(row);
        }

        public FsParam.Row CloneRow(FsParam.Row row, string name, int newId)
        {
            FsParam.Row clone = new FsParam.Row(row);
            clone.ID = newId;
            clone.Name = name;
            return clone;
        }

        public FsParam.Row GetRow(FsParam param, int row)
        {
            return param[row];
        }

        public void Write()
        {
            Lort.Log($"Binding {param.Count()} PARAMs...", Lort.Type.Main);
            Lort.NewTask($"Binding PARAMs", param.Count());
            Lort.Log($"Total TalkParam rows: {param[Paramanager.ParamType.TalkParam].Rows.Count()} out of a max of {ushort.MaxValue}", Lort.Type.Debug);
            
            /* Write params */
            BND4 bnd = new();
            bnd.Compression = Compression.ZSTD();
            bnd.Version = "11601000";
            int i = 0;
            foreach (KeyValuePair<ParamType, FsParam> kvp in param)
            {
                FsParam param = kvp.Value;
              
                if (!Const.DEBUG_SKIP_FMG_PARAM_SORTING)
                {
                    Utility.SortFsParam(param);  // sort rows, debug flag to turn this off for speed. fmg sorting isn't required but in order to mimick FS standards it should be done in prod
                }

                BinderFile file = new();
                file.Bytes = param.Write();
                file.Name = $"N:\\GR\\data\\Param\\param\\GameParam\\merged\\DLC02\\{kvp.Key.ToString()}.param";
                file.ID = i++;
                bnd.Files.Add(file);

                Lort.TaskIterate();
            }
            RegulationDecryptor.EncryptERRegulation(Path.Combine(Const.OUTPUT_PATH, "regulation.bin"), bnd);

            /* Write LiveParam extended talk rows */
            extendedTalkParam.Write(Path.Combine(Const.OUTPUT_PATH, "ExtendedTalkParam.param"));
        }

        /* picks the partdrawparam for an asset based on its size. smaller assets have shorter render distance etc */
        private int AssetPartDrawParamBySize(ModelInfo asset)
        {
            for (int i = 0; i < Const.ASSET_LOD_VALUES.Count(); i++)
            {
                // we do a little cheating here. dynamics can be scaled so im just giving them a huge size mult to compensate.
                // realstically an optimization should be made to calculate this but its a minor concern so very low priority @TODO:
                float[] values = Const.ASSET_LOD_VALUES[i];
                if ((asset.IsDynamic() ? asset.size * 10f : asset.size) < values[0]) { return lodPartDrawParamIDs[i]; }
            }
            return lodPartDrawParamIDs.Last().Value;
        }

        /* These 3 methods generate the params for assets and assetsfx for emitters */
        public void GenerateAssetRows(List<ModelInfo> assets)
        {
            FsParam assetParam = param[ParamType.AssetEnvironmentGeometryParam];
            FsParam.Row electedStoneBuildingRow = assetParam[7077];
            FsParam.Column drawParamID = assetParam["refDrawParamId"];
            FsParam.Column hitType = assetParam["hitCreateType"];
            FsParam.Column behaviourType = assetParam["behaviorType"];
            foreach (ModelInfo asset in assets)
            {
                /* Dynamic */
                if (asset.IsDynamic() || !asset.HasCollision())
                {
                    // Clone a specific row as our baseline
                    FsParam.Row row = CloneRow(electedStoneBuildingRow, asset.name, asset.AssetRow());   // 7077 is a big stone building part in the overworld

                    // Set some values and add
                    drawParamID.SetValue(row, AssetPartDrawParamBySize(asset));        // DrawParamID
                    hitType.SetValue(row, (sbyte)0);           // Hit type (LO ONLY)
                    behaviourType.SetValue(row, (byte)0);           // BehaviourType, affects HKX scaling and breakability
                    AddOrReplaceRow(assetParam, row);
                }
                /* Static */
                else
                {
                    // Clone a specific row as our baseline
                    FsParam.Row row = CloneRow(electedStoneBuildingRow, asset.name, asset.AssetRow());   // 7077 is a big stone building part in the overworld

                    // Set some values and add
                    drawParamID.SetValue(row, AssetPartDrawParamBySize(asset));        // DrawParamID
                    hitType.SetValue(row, (sbyte)0);           // Hit type (LO ONLY)
                    behaviourType.SetValue(row, (byte)1);           // BehaviourType, affects HKX scaling and breakability
                    AddOrReplaceRow(assetParam, row);
                }
            }
        }

        public void GenerateAssetRows(List<EmitterInfo> assets)
        {
            FsParam assetParam = param[ParamType.AssetEnvironmentGeometryParam];
            FsParam.Row blessedStoneBuildingRow = assetParam[7077];
            FsParam.Column drawParamID = assetParam["refDrawParamId"];
            FsParam.Column hitType = assetParam["hitCreateType"];
            FsParam.Column behaviourType = assetParam["behaviorType"];
            
            FsParam emitterParam = param[ParamType.AssetModelSfxParam];
            FsParam.Row blessedCandleRow = emitterParam[228039000];
            List<FsParam.Column> emitterParamCols = [.. emitterParam.Columns];

            foreach (EmitterInfo asset in assets)
            {
                /* We just make all emitters dynamic assets because I can't be asked to sort out baked scaling for them rn */
                /* There aren't that many of them and most will be no-collide so its fine prolly */
                // Clone a specific row as our baseline
                {
                    FsParam.Row row = CloneRow(blessedStoneBuildingRow, asset.record, asset.AssetRow());   // 7077 is a big stone building part in the overworld

                    // Set some values and add
                    drawParamID.SetValue(row, AssetPartDrawParamBySize(asset.model));        // DrawParamID
                    hitType.SetValue(row, (sbyte)0);           // Hit type (LO ONLY)
                    behaviourType.SetValue(row, (byte)0);           // BehaviourType, affects HKX scaling and breakability
                    AddOrReplaceRow(assetParam, row);
                }

                /* If the asset has some emitter or attachlight nodes we create an sfx param for it */
                {
                    if (!asset.HasEmitter() && !asset.HasLight()) { continue; }  // really shouldnt happen but...

                    int offset = 0;
                    FsParam.Row row = CloneRow(blessedCandleRow, asset.record, asset.AssetRow() * 1000); // 228039000 is a candle in the round table hold
                    emitterParamCols[0 + (offset * 3)].SetValue(row, -1); //sfxId_X
                    emitterParamCols[1 + (offset * 3)].SetValue(row, -1); //dmypolyId_X

                    /* Quick optimization */
                    /* In Morrowind they comibne multiple effects for some emitter things. Most notably a campfire is like 5 emitters */
                    /* In Elden Ring they jus thave a single simple campfire FXR. So uhhh let's just look and see if a MW emitter has the fire part and then delete the rest to make things easier. */
                    if (asset.model.dummies.ContainsKey("superspray01 emitter"))
                    {
                        asset.model.dummies.Remove("smoke emitter");
                        asset.model.dummies.Remove("sparks emitter");
                    }
                    if (asset.model.dummies.ContainsKey("fire emitter"))
                    {
                        asset.model.dummies.Remove("smoke emitter");
                        asset.model.dummies.Remove("sparks emitter");
                    }

                    foreach (KeyValuePair<string, short> kvp in asset.model.dummies)
                    {
                        string name = kvp.Key;
                        short refid = kvp.Value;
                        int fxrid = FxrManager.GetFXR(name);

                        if (fxrid != -1)
                        {
                            emitterParamCols[0 + (offset * 3)].SetValue(row, fxrid); //sfxId_X
                            emitterParamCols[1 + (offset * 3)].SetValue(row, (int)refid); //dmypolyId_X
                            offset++;
                        }
                    }

                    if (asset.HasLight())
                    {
                        emitterParamCols[0 + (offset * 3)].SetValue(row, FxrManager.GetLightFXR(asset)); //sfxId_X
                        emitterParamCols[1 + (offset * 3)].SetValue(row, (int)asset.GetAttachLight()); //dmypolyId_X
                    }

                    AddOrReplaceRow(emitterParam, row);
                }
            }
        }

        /* Generates assetparam rows for pickable (harvestable) plants */
        public void GeneratePickableAssetRows(ItemManager itemManager, List<PickableInfo> pickables)
        {
            FsParam assetParam = param[ParamType.AssetEnvironmentGeometryParam];
            FsParam lotParam = param[ParamType.ItemLotParam_map];
            FsParam actionParam = param[ParamType.ActionButtonParam];

            foreach (PickableInfo pickable in pickables)
            {
                // Resolve inventory to something we can actually use here
                List<(ItemManager.ItemInfo item, int max)> possibleItems = new();
                foreach((string id, int quantity) tuple in pickable.inventory)
                {
                    ItemManager.LeveledList list = itemManager.GetList(tuple.id);
                    ItemManager.ItemInfo item = itemManager.GetItem(tuple.id);
                    if(list != null)
                    {
                        foreach(ItemManager.ItemInfo entry in list.Possibilites())
                        {
                            possibleItems.Add((entry, Math.Min(8, tuple.quantity + 1)));
                        }
                    }
                    else if(item != null)
                    {
                        possibleItems.Add((item, Math.Min(8, tuple.quantity + 1)));
                    }
                }

                // Setup itemlot param
                for (int i = 0; i < possibleItems.Count; i++)
                {
                    (ItemManager.ItemInfo item, int max) tuple = possibleItems[i];

                    FsParam.Row lotRow = CloneRow(lotParam[0], $"Pickable->{pickable.name}::{tuple.item.id}", nextMapItemLotId + i);
                    lotRow["getItemFlagId"].Value.SetValue((uint)0);

                    int k = tuple.max;
                    for (int j=0;j<8&&k>0;j++)
                    {
                        (ItemManager.ItemInfo item, int max) possibility = possibleItems[i];

                        lotRow[$"lotItemCategory{j+1:D2}"].Value.SetValue(possibility.item.ItemLotCategory());
                        lotRow[$"lotItemId{j+1:D2}"].Value.SetValue(possibility.item.row);
                        lotRow[$"enableLuck{j+1:D2}"].Value.SetValue((ushort)(k==tuple.max?1:0));
                        lotRow[$"lotItemNum{j+1:D2}"].Value.SetValue((byte)k--);
                        lotRow[$"lotItemBasePoint{j+1:D2}"].Value.SetValue((ushort)(1000/tuple.max));
                    }

                    AddOrReplaceRow(lotParam, lotRow);
                }

                // Setup action param
                FsParam.Row actionRow = CloneRow(actionParam[7817], pickable.ActionText(), nextActionButtonId++); // 7817 is rowa berry harvest prompt
                int textId = textManager.AddActionButton(pickable.ActionText());

                actionRow["radius"].Value.SetValue(1.45f); // radius
                actionRow["angle"].Value.SetValue(180); // angle from dmy
                actionRow["depth"].Value.SetValue(0f);
                actionRow["width"].Value.SetValue(0f);
                actionRow["height"].Value.SetValue(2.25f);
                actionRow["baseHeightOffset"].Value.SetValue(-1.75f);
                actionRow["angleCheckType"].Value.SetValue((byte)0);
                actionRow["allowAngle"].Value.SetValue(180);  // player look angle
                actionRow["textId"].Value.SetValue(textId);
                actionRow["isGrayoutForRide"].Value.SetValue((byte)1); // don't allow while riding torrent
                actionRow["execInvalidTime"].Value.SetValue(0f); // cooldown

                AddOrReplaceRow(actionParam, actionRow);

                // Setup asset param
                FsParam.Row assetRow = CloneRow(assetParam[99680], $"Pickable->{pickable.name}", pickable.AssetRow()); // 99680 is an erdleaf flower

                assetRow["refDrawParamId"].Value.SetValue(AssetPartDrawParamBySize(pickable.model));        // DrawParamID
                assetRow["pickUpActionButtonParamId"].Value.SetValue(actionRow.ID);
                assetRow["pickUpItemLotParamId"].Value.SetValue(nextMapItemLotId);

                AddOrReplaceRow(assetParam, assetRow);

                nextMapItemLotId += 10;
            }
        }

        public void GenerateAssetRows(List<LiquidInfo> assets)
        {
            FsParam assetParam = param[ParamType.AssetEnvironmentGeometryParam];
            FsParam.Row oceanwaterrow = assetParam[97000];
            foreach (LiquidInfo asset in assets)
            {
                // Clone a specific row as our baseline
                FsParam.Row row = CloneRow(oceanwaterrow, $"water{asset.id}", asset.AssetRow()); // 097000 is the ocean water around limgrave
                AddOrReplaceRow(assetParam, row);
            }
        }

        /* Make some parts draw params for us to use on different types of assets */
        public void GeneratePartDrawParams()
        {
            FsParam drawParam = param[ParamType.PartsDrawParam];
            float NONE = 99999f;
            short drawParamId = Const.PART_DRAW_PARAM;
            lodPartDrawParamIDs = new();

            FsParam.Row genericlongdistanceloddrawparam = drawParam[1001];

            FsParam.Column lv01_BorderDist = drawParam["lv01_BorderDist"];
            FsParam.Column lv01_PlayDist = drawParam["lv01_PlayDist"];

            FsParam.Column drawDist = drawParam["drawDist"];
            FsParam.Column drawFadeRange = drawParam["drawFadeRange"];

            FsParam.Column tex_lv01_BorderDist = drawParam["tex_lv01_BorderDist"];
            FsParam.Column tex_lv01_PlayDist = drawParam["tex_lv01_PlayDist"];
            FsParam.Column IncludeLodMapLv = drawParam["IncludeLodMapLv"];
            FsParam.Column lodType = drawParam["lodType"];

            FsParam.Column DistantViewModel_BorderDist = drawParam["DistantViewModel_BorderDist"];
            FsParam.Column DistantViewModel_PlayDist = drawParam["DistantViewModel_PlayDist"];

            // Clone a specific row as our baseline
            for (int i = 0; i < Const.ASSET_LOD_VALUES.Count(); i++)
            {
                float[] values = Const.ASSET_LOD_VALUES[i];

                FsParam.Row row = CloneRow(genericlongdistanceloddrawparam, $"mw | generic | 0lod | size_{values[0]} | static", drawParamId); // generic long distance lod drawparam

                // set some values
                lv01_BorderDist.SetValue(row, NONE);  // border 0
                lv01_PlayDist.SetValue(row, 0f);

                drawDist.SetValue(row, values[1]); // drawdist
                drawFadeRange.SetValue(row, values[2]); // fadeoff

                tex_lv01_BorderDist.SetValue(row, 256f); // tex_lv1_borderdist [512]
                tex_lv01_PlayDist.SetValue(row, 32f);    // tex_lv1_playdist [10]
                IncludeLodMapLv.SetValue(row, (sbyte)0);    // include lod map level [2]
                lodType.SetValue(row, (byte)1);    // lodtype [1]

                DistantViewModel_BorderDist.SetValue(row, NONE); // distant view model border dist [30]
                DistantViewModel_PlayDist.SetValue(row, 0f);    // distant view model play dist [5]
                lodPartDrawParamIDs.Add(i, drawParamId++);
                AddOrReplaceRow(drawParam, row);
            }

            // Clone a specific row as our baseline
            {
                FsParam.Row row = CloneRow(drawParam[1001], $"mw | terrain | 2lod | static", drawParamId); // generic long distance lod drawparam

                // set some values and add
                row.Cells[0].SetValue(Const.TERRAIN_LOD_VALUES[0].DISTANCE); // border 0
                row.Cells[1].SetValue(16f);
                row.Cells[2].SetValue(Const.TERRAIN_LOD_VALUES[1].DISTANCE); // border 1
                row.Cells[3].SetValue(32f);
                row.Cells[4].SetValue(Const.TERRAIN_LOD_VALUES[2].DISTANCE); // border 2
                row.Cells[5].SetValue(64f);

                row.Cells[13].SetValue(NONE); // drawdist
                row.Cells[14].SetValue(0f); //fadeoff

                row.Cells[10].SetValue(256f); // tex_lv1_borderdist [512]
                row.Cells[11].SetValue(32f);    // tex_lv1_playdist [10]
                row.Cells[24].SetValue((sbyte)0);    // include lod map level [2]
                row.Cells[26].SetValue((byte)1);    // lodtype [1]

                row.Cells[30].SetValue(NONE); // distant view model border dist [30]
                row.Cells[31].SetValue(0f);    // distant view model play dist [5]
                AddOrReplaceRow(drawParam, row);
                terrainDrawParamID = drawParamId++;
            }
        }

        public class WeatherData
        {
            public readonly string name;
            public readonly List<string> match;
            public readonly int MapInfoParamId, MapRegionParamId;
            public readonly EnvManager.Rem rem;

            public WeatherData(string name, List<string> match, int MapInfoParamId, int MapRegionParamId, EnvManager.Rem rem)
            {
                this.name = name;
                this.match = match;
                this.MapInfoParamId = MapInfoParamId;
                this.MapRegionParamId = MapRegionParamId;
                this.rem = rem;
            }
        }

        // @TODO: maybe move weatherdata to layout or its own class since its used by multiple toher thingsthingso
        public static WeatherData GetWeatherData(string region)
        {
            string regionLower = region.ToLower().Trim();
            foreach (WeatherData wd in EXTERIOR_WEATHER_DATA_LIST)
            {
                foreach(string m in wd.match)
                {
                    if (m.ToLower().Trim() == regionLower) { return wd; }
                }
            }
            return null;
        }

        public static List<WeatherData> EXTERIOR_WEATHER_DATA_LIST = new()
        {       // default region is the result when an area has literally no region value set. just a big fat null
            new WeatherData("Limgrave", new(){ "Default Region", "West Gash Region", "Ascadian Isles Region", "Grazelands Region" }, 60423600, 99999901, EnvManager.Rem.Forest), // 0
            new WeatherData("Liurnia", new(){ "Bitter Coast Region", "Sheogorad", "Azura's Coast Region" }, 60374200, 60365000, EnvManager.Rem.Forest), // 10
            new WeatherData("Altus", new(){ }, 60395000, 60395000, EnvManager.Rem.Forest), // 20
            new WeatherData("Gelmir", new(){ "Ashlands Region", "Molag Mar Region" }, 60355200, 60355200, EnvManager.Rem.Mountain), // 21
            new WeatherData("Caelid", new(){ }, 60473700, 60473700, EnvManager.Rem.Mountain), // 30
            new WeatherData("Caelid Desert", new(){ "Red Mountain Region" }, 60533800, 60533800, EnvManager.Rem.Mountain), // 31
            new WeatherData("Mountaintop of Giants", new() { "Brodir Grove Region", "Felsaad Coast Region", "Hirstaang Forest Region", "Isinfier Plains Region", "Thirsk Region" }, 60505700, 60505700, EnvManager.Rem.Snowfield),
            new WeatherData("Consecrated Snowfield", new() { "Moesring Mountains Region" }, 60495500, 60495500, EnvManager.Rem.Snowfield)
        };

        public static List<WeatherData> INTERIOR_WEATHER_DATA_LIST = new()
        {
            new WeatherData("Cave", new(){ "cave", "mine" }, 31030000, 31030000, EnvManager.Rem.Cave), // 3103
            new WeatherData("Home", new(){ "house", "home" }, 11100000, 11100000, EnvManager.Rem.Home), // 1110
            new WeatherData("Tomb", new(){ "tomb" }, 30000000, 30000000, EnvManager.Rem.Tomb), // 3000
        };

        public void GenerateMapInfoParam(Layout layout)
        {
            FsParam mapInfoParam = param[ParamType.MapDefaultInfoParam];
            FsParam mapRegionParam = param[ParamType.MapGdRegionInfoParam];

            // Exterior msbs
            foreach (Tile tile in layout.Tiles)
            {
                if (tile.IsEmpty) { continue; } // skip empty tiles

                string region = tile.GetRegion();
                WeatherData weatherData = GetWeatherData(region);

                int id = int.Parse($"60{tile.coordinate.x:D2}{tile.coordinate.y:D2}00");


                /* MapInfoParam */ // controls sky and weather and bgm and other stuff
                FsParam.Row rowA = CloneRow(mapInfoParam[weatherData.MapInfoParamId], $"mw ext m{tile.map} {tile.coordinate.x} {tile.coordinate.y} {tile.block}", id);
                rowA["BgmPlaceInfo"].Value.SetValue((short)0); // set bgm to limgrave
                rowA["EnvPlaceInfo"].Value.SetValue((short)0); // set env to limgrave as well
                rowA["MapAdditionalSoundBankId"].Value.SetValue(60000); // default (?)
                AddOrReplaceRow(mapInfoParam, rowA);

                /* MapRegionParam */ // controls gparam
                FsParam.Row rowB = CloneRow(mapRegionParam[weatherData.MapRegionParamId], $"mw ext m{tile.map} {tile.coordinate.x} {tile.coordinate.y} {tile.block}", id);
                AddOrReplaceRow(mapRegionParam, rowB);
            }

            // Interior msbs
            foreach (InteriorGroup group in layout.Interiors)
            {
                if (group.IsEmpty) { continue; } // skip empty group

                WeatherData weatherData = group.GetWeather();

                int id = int.Parse($"{group.map:D2}{group.area:D2}{group.unk:D2}{group.block:D2}");

                /* MapInfoParam */ // controls sky and weather
                FsParam.Row rowA = CloneRow(mapInfoParam[weatherData.MapInfoParamId], $"mw int m{group.map} {group.area} {group.unk} {group.block}", id);
                rowA["BgmPlaceInfo"].Value.SetValue((short)0); // set bgm to limgrave
                rowA["EnvPlaceInfo"].Value.SetValue((short)0); // set env to limgrave as well
                AddOrReplaceRow(mapInfoParam, rowA);

                /* MapRegionParam */ // controls gparam
                FsParam.Row rowB = CloneRow(mapRegionParam[weatherData.MapRegionParamId], $"mw int m{group.map} {group.area} {group.unk} {group.block}", id);
                AddOrReplaceRow(mapRegionParam, rowB);
            }
        }

        public void GenerateTalkParam(List<NpcManager.TopicData> topicData)
        { 
            /*
             * Since FsParam[ID] is actually a linear search for a matching row, we build a Dictionary up-front
             * to speed up checks. Because of this, however, we need to keep it up-to-date in the loop below.
             */
            Dictionary<int, LiveParam.Row> rowsById = extendedTalkParam.Rows.ToDictionary(r => r.ID);

            LiveParam.Row templateTalkRow = rowsById[1400000]; // 1400000 is a line from opening cutscene

            LiveParam.Column msgId = extendedTalkParam["msgId"];
            LiveParam.Column voiceId = extendedTalkParam["voiceId"];

            LiveParam.Column msgId_female = extendedTalkParam["msgId_female"];
            LiveParam.Column voiceId_female = extendedTalkParam["voiceId_female"];

            foreach (NpcManager.TopicData topic in topicData)
            {
                foreach (NpcManager.TopicData.TalkData talk in topic.talks)
                {
                    for (int i = 0; i < talk.talkRows.Count(); i++)
                    {
                        int id = talk.talkRows[i];
                        string text = talk.splitText[i];

                        // If exists skip, duplicates happen during gen of these params beacuse a single talkparam can be used by any number of npcs. Hundreds in some cases.
                        if (rowsById.ContainsKey(id)) { continue; }

                        // truncating the text in the row name because it can cause issues if it is too long
                        LiveParam.Row row = new LiveParam.Row(templateTalkRow);
                        row.ID = id;
                        row.Name = text.Substring(0, Math.Min(32, text.Length));

                        msgId.SetValue(row, id * 10); // message id (male)
                        voiceId.SetValue(row, id * 10); // message id (male)

                        msgId_female.SetValue(row, id * 10); // message id (female)
                        voiceId_female.SetValue(row, id * 10); // message id (female)

                        textManager.AddTalk(id * 10, text);
                        LiveParam.Row oldrow = extendedTalkParam[row.ID];
                        if (oldrow != null)
                            extendedTalkParam.RemoveRow(oldrow);
                        extendedTalkParam.AddRow(row);
                        rowsById[id] = row; // this is where we keep the lookup up-to-date
                    }
                }
            }
        }

        public void GenerateFaceParam(NpcContent npc, int id)
        {
            Override.FaceData face = Override.GetFace(npc.head);
            Override.Hair hair = Override.GetHair(npc.hair);

            int rowToCopy = 23150; // default girl women as our template

            FsParam faceParam = param[ParamType.FaceParam];
            FsParam.Row row = CloneRow(faceParam[rowToCopy], npc.id, id);

            // set values for face
            foreach(var kvp in face.data)
            {
                row[kvp.Key].Value.SetValue(kvp.Value);
            }

            // set values for hair
            byte[] color = hair.GetColor();
            row["hair_partsId"].Value.SetValue(hair.part);
            row["hair_color_R"].Value.SetValue(color[0]);
            row["hair_color_G"].Value.SetValue(color[1]);
            row["hair_color_B"].Value.SetValue(color[2]);
            row["beard_color_R"].Value.SetValue(color[0]);
            row["beard_color_G"].Value.SetValue(color[1]);
            row["beard_color_B"].Value.SetValue(color[2]);
            row["body_hairColor_R"].Value.SetValue(color[0]);
            row["body_hairColor_G"].Value.SetValue(color[1]);
            row["body_hairColor_B"].Value.SetValue(color[2]);

            AddOrReplaceRow(faceParam, row);
        }

        public void GenerateCharInitParam(ItemManager itemManager, NpcContent npc, int id)
        {
            Override.FaceData face = Override.GetFace("");
            Override.Hair hair = Override.GetHair("");

            int rowToCopy = 23150; // default girl women as our template

            FsParam charInitParam = param[ParamType.CharaInitParam];
            FsParam.Row row = CloneRow(charInitParam[rowToCopy], npc.id, id);

            /* Face and sex */
            GenerateFaceParam(npc, id); // create faceparam to pair with this charainit
            row["npcPlayerFaceGenId"].Value.SetValue(id);  // set faceparam
            row["npcPlayerSex"].Value.SetValue(npc.sex == CharacterContent.Sex.Male ? (byte)1 : (byte)0); // set sex

            /* Equipment */
            row["equip_Wep_Right"].Value.SetValue(npc.equipWeaponRight?.row ?? -1);
            row["wepParamType_Right1"].Value.SetValue(npc.equipWeaponRight?.type == ItemManager.Type.CustomWeapon ? (byte)1 : (byte)0);
            row["equip_Subwep_Right"].Value.SetValue(npc.equipRange?.row ?? -1);
            row["wepParamType_Right2"].Value.SetValue(npc.equipRange?.type == ItemManager.Type.CustomWeapon ? (byte)1 : (byte)0);
            row["equip_Wep_Left"].Value.SetValue(npc.equipWeaponLeft?.row ?? -1);
            row["wepParamType_Left1"].Value.SetValue(npc.equipWeaponLeft?.type == ItemManager.Type.CustomWeapon ? (byte)1 : (byte)0);
            row["equip_Helm"].Value.SetValue(npc.equipHead?.row ?? -1);
            row["equip_Armer"].Value.SetValue(npc.equipBody?.row ?? -1);   // SIC
            row["equip_Gaunt"].Value.SetValue(npc.equipHands?.row ?? -1);
            row["equip_Leg"].Value.SetValue(npc.equipLegs?.row ?? -1);
            row["equip_Arrow"].Value.SetValue(npc.equipArrow?.row ?? -1);
            row["equip_Bolt"].Value.SetValue(npc.equipBolt?.row ?? -1);

            for (int i = 0; i < npc.equipAcc.Length; i++)
            {
                ItemManager.ItemInfo acc = npc.equipAcc[i];
                row[$"equip_Accessory{i + 1:D2}"].Value.SetValue(acc.row);
            }

            for (int i = 0; i < npc.equipGood.Length; i++)
            {
                ItemManager.ItemInfo good = npc.equipGood[i];
                row[$"item_{i + 1:D2}"].Value.SetValue(good.row);
                row[$"itemNum_{i + 1:D2}"].Value.SetValue((byte)1);   // @TODO: npc use item counts?
            }

            /* Stats */  // @TODO: placeholder calc for stats. directly translation with a little scaling. will comeback to this when we get more into gameplay design
            int vigr = (int)(npc.level * 2.9f) + 7;
            int endr = (int)(npc.stats.Get(CharacterContent.Stats.Attribute.Endurance) * .75f);
            int strg = (int)(npc.stats.Get(CharacterContent.Stats.Attribute.Strength) * .75f);
            int dext = (int)(((npc.stats.Get(CharacterContent.Stats.Attribute.Agility) + npc.stats.Get(CharacterContent.Stats.Attribute.Speed)) * .5f) * .75f);
            int mind = (int)(npc.stats.Get(CharacterContent.Stats.Attribute.Willpower) * .75f);
            int intl = (int)(npc.stats.Get(CharacterContent.Stats.Attribute.Intelligence) * .75f);
            int fath = (int)(((npc.stats.Get(CharacterContent.Stats.Attribute.Luck) + npc.stats.Get(CharacterContent.Stats.Attribute.Willpower)) * .5f) * .75f);
            int arcn = (int)(npc.stats.Get(CharacterContent.Stats.Attribute.Personality) * .75f);
            int total = Math.Max(1, vigr + endr + strg + dext + mind + intl + fath + arcn - 79);

            row["baseHp"].Value.SetValue((ushort)0);
            row["baseMp"].Value.SetValue((ushort)0);
            row["baseSp"].Value.SetValue((ushort)0);

            row["HpEstMax"].Value.SetValue((sbyte)0);
            row["MpEstMax"].Value.SetValue((sbyte)0);

            row["soulLv"].Value.SetValue((short)total);
            row["baseVit"].Value.SetValue((byte)vigr);
            row["baseWil"].Value.SetValue((byte)mind);
            row["baseEnd"].Value.SetValue((byte)endr);
            row["baseStr"].Value.SetValue((byte)strg);
            row["baseDex"].Value.SetValue((byte)dext);
            row["baseMag"].Value.SetValue((byte)intl);
            row["baseFai"].Value.SetValue((byte)fath);
            row["baseLuc"].Value.SetValue((byte)arcn);

            AddOrReplaceRow(charInitParam, row);
        }

        public void GenerateNpcParam(ItemManager itemManager, BaseScript script, NpcContent npc, int id)
        {
            // It seems like special poses are tied to npcparam in some way so i need to copy lanya to get the 'dead body' pose
            int rowToCopy;
            if (npc.dead) { rowToCopy = 523150020; } // lanya dead on the ground in the peter griffin pose
            else { rowToCopy = 523010000; }          // white mask varre

            FsParam npcParam = param[ParamType.NpcParam];
            FsParam.Row row = CloneRow(npcParam[rowToCopy], npc.id, id);

            int itemLotRow;
            List<(ItemManager.ItemInfo item, int quantity)> inventory = itemManager.ResolveInventory(npc);
            if (!npc.dead && inventory.Count() > 0) {
                itemLotRow = GenerateInventoryItemLot(script, npc, inventory);
            }
            else { itemLotRow = -1; }

            int textId = textManager.AddNpcName(npc.name);
            row.Cells[5].SetValue(textId); // nameId
            row.Cells[105].SetValue((byte)26); // team type [friendlynpc=26]
            row["itemLotId_enemy"].Value.SetValue(itemLotRow);

            AddOrReplaceRow(npcParam, row);
        }

        public void GenerateNpcParam(ItemManager itemManager, BaseScript script, CreatureContent creature, int id, Override.EnemyRemap remap)
        {
            int rowToCopy = remap.npc.row;

            FsParam npcParam = param[ParamType.NpcParam];
            FsParam.Row row = CloneRow(npcParam[rowToCopy], creature.id, id);

            int itemLotRow;
            List<(ItemManager.ItemInfo item, int quantity)> inventory = itemManager.ResolveInventory(creature);
            if (inventory.Count() > 0) { itemLotRow = GenerateInventoryItemLot(script, creature, inventory); }    // @TODO: rework item lot generation for creatures to be non-fixed
            else { itemLotRow = -1; }

            SillyJsonUtils.ModifyRow(row, remap.npc.data);

            int textId = textManager.AddNpcName(creature.name);
            row.Cells[5].SetValue(textId); // nameId
            row.Cells[105].SetValue((byte)(creature.IsHostile() ? 6 : 26)); // team type (enemy=6, hostilenpc=27, friendlynpc=26)
            row["itemLotId_enemy"].Value.SetValue(itemLotRow);

            AddOrReplaceRow(npcParam, row);
        }

        public void GenerateThinkParam(ItemManager itemManager, BaseScript script, NpcContent npc, int id)
        {
            FsParam thinkParam = param[ParamType.NpcThinkParam];
            FsParam.Row row = CloneRow(thinkParam[533250000], npc.id, id); // 533250000 is rogier followy

            // STUB:: do stuff to this param lol

            if (npc.follower) // remove back home distance if follower
            {
                row["maxBackhomeDist"].Value.SetValue(ushort.MaxValue);
                row["backhomeDist"].Value.SetValue(ushort.MaxValue);
                row["backhomeBattleDist"].Value.SetValue(ushort.MaxValue);
            }

            AddOrReplaceRow(thinkParam, row);
        }

        public void GenerateThinkParam(ItemManager itemManager, BaseScript script, CreatureContent creature, int id, Override.EnemyRemap remap)
        {
            FsParam thinkParam = param[ParamType.NpcThinkParam];
            FsParam.Row row = CloneRow(thinkParam[remap.think.row], creature.id, id);

            SillyJsonUtils.ModifyRow(row, remap.think.data);

            AddOrReplaceRow(thinkParam, row);
        }

        public int GenerateActionButtonItemParam(string text)
        {
            if (itemActionButtons.ContainsKey(text)) { return itemActionButtons[text]; } // already exists, return row to it

            int rowId = nextActionButtonId++;

            FsParam actionParam = param[ParamType.ActionButtonParam];
            FsParam.Row row = CloneRow(actionParam[4000], text, rowId); // 4000 is default pickup item prompt

            int textId = textManager.AddActionButton(text);

            row["radius"].Value.SetValue(1f); // radius
            row["angle"].Value.SetValue(180); // angle from dmy
            row["depth"].Value.SetValue(0f);
            row["width"].Value.SetValue(0f);
            row["height"].Value.SetValue(1.75f);
            row["baseHeightOffset"].Value.SetValue(-1.25f);
            row["angleCheckType"].Value.SetValue((byte)0);
            row["allowAngle"].Value.SetValue(180);  // player look angle
            row["textId"].Value.SetValue(textId);
            row["isGrayoutForRide"].Value.SetValue((byte)1); // don't allow while riding torrent
            row["execInvalidTime"].Value.SetValue(0f); // cooldown

            AddOrReplaceRow(actionParam, row);
            itemActionButtons.Add(text, rowId);

            return rowId;
        }

        // Refactor this @TODO: guh
        public int GenerateActionButtonInteractParam(string text) { return GenerateActionButtonInteractParam(null, text); }

        // Might be worth seperating this out into multiple smaller functions instead. idk just guh
        public int GenerateActionButtonInteractParam(Content content, string text)
        {
            FsParam actionParam = param[ParamType.ActionButtonParam];
            int rowId = nextActionButtonId++;
            int textId = textManager.AddActionButton(text);

            // Generating action button prompts for differetn types of objects can require very differnt parameters so usign a switch
            switch (content)
            {
                case DoorContent:
                    {
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);
                        FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, modelInfo.path)); // load flver of this door so we can look at its bounding box
                        float x = flver.Nodes[0].BoundingBoxMax.X - flver.Nodes[0].BoundingBoxMin.X;
                        float z = flver.Nodes[0].BoundingBoxMax.Z - flver.Nodes[0].BoundingBoxMin.Z;
                        float width = x > z ? x : z;
                        float top = Math.Max(0, flver.Nodes[0].BoundingBoxMax.Y);
                        float bottom = Math.Abs(flver.Nodes[0].BoundingBoxMin.Y);
                        float height = top + bottom;
                        float offset = -bottom - 0.25f;

                        height = Math.Max(Const.DOOR_MINIMUM_HEIGHT, height); // makes sure that doors are at least about as tall as a player, mainly fixes trapdoors which are very short

                        // If a door really appears to be a trapdoor extend the offset more so its reachable
                        string lname = modelInfo.name.ToLower();
                        if (lname.Contains("trapdoor") || lname.Contains("shipdoor"))
                        {
                            height += Const.TRAPDOOR_HEIGHT_CORRECTION;
                            offset -= Const.TRAPDOOR_HEIGHT_CORRECTION;
                        }

                        FsParam.Row row = CloneRow(actionParam[6000], text, rowId); // 6000 is talk prompt

                        row["regionType"].Value.SetValue((byte)0); // cylinder
                        row["dummyPoly1"].Value.SetValue((int)Const.FLVER_DMY_BOTTOM); // area for entering door is at the bottom since the check is at the feet of the player character
                        row["dummyPoly2"].Value.SetValue(-1);
                        row["radius"].Value.SetValue(width); // radius
                        row["angle"].Value.SetValue(180); // angle from dmy
                        row["depth"].Value.SetValue(0f);
                        row["width"].Value.SetValue(0f);
                        row["height"].Value.SetValue(height);
                        row["baseHeightOffset"].Value.SetValue(offset);
                        row["angleCheckType"].Value.SetValue((byte)0);
                        row["allowAngle"].Value.SetValue(90);  // player look angle
                        row["textId"].Value.SetValue(textId);
                        row["isGrayoutForRide"].Value.SetValue((byte)1); // don't allow while riding torrent
                        row["execInvalidTime"].Value.SetValue(3f); // cooldown

                        AddOrReplaceRow(actionParam, row);
                        return rowId;
                    }
                case null:
                default:
                    {
                        FsParam.Row row = CloneRow(actionParam[6000], text, rowId); // 6000 is talk prompt
                        row["textId"].Value.SetValue(textId);
                        row["isGrayoutForRide"].Value.SetValue((byte)1); // don't allow while riding torrent
                        row["execInvalidTime"].Value.SetValue(3f); // cooldown
                        AddOrReplaceRow(actionParam, row);
                        return rowId;
                    }
            }
        }

        public int GenerateActionButtonDoorParam(DoorContent door)
        {
            int rowId = nextActionButtonId++;

            ModelInfo modelInfo = cache.GetModel(door.mesh, door.scale);
            FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, modelInfo.path)); // load flver of this door so we can look at its bounding box
            float x = flver.Nodes[0].BoundingBoxMax.X - flver.Nodes[0].BoundingBoxMin.X;
            float z = flver.Nodes[0].BoundingBoxMax.Z - flver.Nodes[0].BoundingBoxMin.Z;
            float width = x > z ? x : z;
            float top = Math.Max(0, flver.Nodes[0].BoundingBoxMax.Y);
            float bottom = Math.Abs(flver.Nodes[0].BoundingBoxMin.Y);
            float height = top + bottom;
            float offset = -bottom - 0.25f;

            height = Math.Max(Const.DOOR_MINIMUM_HEIGHT, height); // makes sure that doors are at least about as tall as a player, mainly fixes trapdoors which are very short

            // If a door really appears to be a trapdoor extend the offset more so its reachable
            string lname = modelInfo.name.ToLower();
            if (lname.Contains("trapdoor") || lname.Contains("shipdoor"))
            {
                height += Const.TRAPDOOR_HEIGHT_CORRECTION;
                offset -= Const.TRAPDOOR_HEIGHT_CORRECTION;
            }

            FsParam actionParam = param[ParamType.ActionButtonParam];
            FsParam.Row row = CloneRow(actionParam[1000], door.warp.prompt, rowId); // 1000 is pick up runes prompt

            int textId = textManager.AddActionButton(door.warp.prompt);

            row["regionType"].Value.SetValue((byte)0); // cylinder
            row["dummyPoly1"].Value.SetValue((int)Const.FLVER_DMY_BOTTOM); // area for entering door is at the bottom since the check is at the feet of the player character
            row["dummyPoly2"].Value.SetValue(-1);
            row["radius"].Value.SetValue(width); // radius
            row["angle"].Value.SetValue(180); // angle from dmy
            row["depth"].Value.SetValue(0f);
            row["width"].Value.SetValue(0f);
            row["height"].Value.SetValue(height);
            row["baseHeightOffset"].Value.SetValue(offset);
            row["angleCheckType"].Value.SetValue((byte)0);
            row["allowAngle"].Value.SetValue(90);  // player look angle
            row["textId"].Value.SetValue(textId);
            row["isGrayoutForRide"].Value.SetValue((byte)1); // don't allow while riding torrent
            row["execInvalidTime"].Value.SetValue(3f); // cooldown

            AddOrReplaceRow(actionParam, row);
            return rowId;
        }

        /* Generate TexInfo stuff for the 'maptexinfo.bmp' file */
        public void GenerateTexInfoParam(List<RegionInfo> regions)
        {
            // EMEVD enum values converted to param row ids
            Dictionary<ScriptCommon.WeatherEMEVD, short> weatherIds = new()
            {
                { ScriptCommon.WeatherEMEVD.None, 0},
                { ScriptCommon.WeatherEMEVD.Default, 0},
                { ScriptCommon.WeatherEMEVD.Rain, 20},
                { ScriptCommon.WeatherEMEVD.Snow, 40},
                { ScriptCommon.WeatherEMEVD.WindyRain, 30},
                { ScriptCommon.WeatherEMEVD.Fog, 50},
                { ScriptCommon.WeatherEMEVD.Cloudless, 1},
                { ScriptCommon.WeatherEMEVD.FlatClouds, 10},
                { ScriptCommon.WeatherEMEVD.PuffyClouds, 11},
                { ScriptCommon.WeatherEMEVD.RainyClouds, 21},
                { ScriptCommon.WeatherEMEVD.WindyFog, 31},
                { ScriptCommon.WeatherEMEVD.HeavySnow, 41},
                { ScriptCommon.WeatherEMEVD.HeavyFog, 51},
                { ScriptCommon.WeatherEMEVD.WindyPuffyClouds, 60},
                { ScriptCommon.WeatherEMEVD.Default2, 0},
                { ScriptCommon.WeatherEMEVD.Default3, 0},
                { ScriptCommon.WeatherEMEVD.RainyHeavyFog, 52},
                { ScriptCommon.WeatherEMEVD.SnowyHeavyFog, 81},
                { ScriptCommon.WeatherEMEVD.ScatteredRain, 82},
                { ScriptCommon.WeatherEMEVD.Unknown18, 83},
                { ScriptCommon.WeatherEMEVD.Unknown19, 84},
                { ScriptCommon.WeatherEMEVD.Unknown20, 85},
                { ScriptCommon.WeatherEMEVD.Unknown21, 86},
                { ScriptCommon.WeatherEMEVD.Unknown22, 97},
                { ScriptCommon.WeatherEMEVD.Unknown23, 88}
            };

            FsParam mapNameTexParam = param[ParamType.MapNameTexParam];
            FsParam mapPieceTexParam = param[ParamType.MapPieceTexParam];
            FsParam lotTexParam = param[ParamType.WeatherLotTexParam];
            FsParam weatherLotParam = param[ParamType.WeatherLotParam];

            foreach (RegionInfo region in regions)
            {
                byte texByte = Override.GetRegionByte(region.id);
                if (texByte == 255) { continue; } // skip if default byte is returned

                /* Create map tex rows */
                FsParam.Row nameRow = CloneRow(mapNameTexParam[255], region.name, texByte);
                FsParam.Row pieceRow = CloneRow(mapPieceTexParam[255], region.name, texByte);
                FsParam.Row lotRow = CloneRow(lotTexParam[255], region.name, texByte);

                /* Create and setup weather lot row */
                int weatherLotId = nextWeatherLotParamId;
                nextWeatherLotParamId += 1000;
                FsParam.Row weatherRow = CloneRow(weatherLotParam[600000000], region.name, weatherLotId); // 600000000 is just a regular overworld weather lot. nothing special
                for(int i=0;i<16;i++)
                {
                    // If we don't have 16 possible weathers just fill out rest of rows with nothing
                    if(i >= region.weathers.Count())
                    {
                        weatherRow[$"weatherType{i}"].Value.SetValue((short)-1);
                        weatherRow[$"lotteryWeight{i}"].Value.SetValue((ushort)0);
                        continue;
                    }

                    // Fill out param rows for weather and chances
                    (ScriptCommon.WeatherEMEVD weather, float chance) entry = region.weathers[i];
                    ushort toPerThou = (ushort)(entry.chance * (1000f / region.ChanceTotal()));
                    weatherRow[$"weatherType{i}"].Value.SetValue(weatherIds[entry.weather]);
                    weatherRow[$"lotteryWeight{i}"].Value.SetValue(toPerThou);
                }

                /* Setup map tex rows */
                int textId = textManager.AddLocation(region.name);
                nameRow["mapNameId"].Value.SetValue(textId);
                lotRow["weatherLogId"].Value.SetValue(weatherLotId);  // SIC

                /* Add em all */
                AddOrReplaceRow(mapNameTexParam, nameRow);
                AddOrReplaceRow(mapPieceTexParam, pieceRow);
                AddOrReplaceRow(lotTexParam, lotRow);
                AddOrReplaceRow(weatherLotParam, weatherRow);
            }
        }

        /* Generate or get an already generated worldmappoint to be used as a placename. Not for actual map icons! */
        public int GenerateWorldMapPoint(IMSBCompilableGroup group, Layout.MapPoint point, int id)
        {
            FsParam worldMapPointParam = param[ParamType.WorldMapPointParam];
            FsParam.Row row = CloneRow(worldMapPointParam[1], $"{point.name} placename", id); // 1 is our template

            int textId = textManager.AddLocation(point.name);

                        
            if (group.IsInterior)
            {
                row["eventFlagId"].Value.SetValue(6001u);    // always on event flag
                row["dispMask00"].Value.SetValue((byte)0);   // never show on map
                row["dispMask01"].Value.SetValue((byte)0);   // never show on map
                row["dispMask02"].Value.SetValue((byte)0);   // never show on map
            }
            else
            {
                row["eventFlagId"].Value.SetValue(point.discovered.id);
                row["dispMask00"].Value.SetValue((byte)1);
                row["dispMinZoomStep"].Value.SetValue((byte)(point.important ? 0 : 1));
                row["selectMinZoomStep"].Value.SetValue((byte)(point.important ? 0 : 1));
            }

            row["iconId"].Value.SetValue((ushort)point.icon);
            row["altIconId"].Value.SetValue((ushort)point.icon);

            row["areaNo"].Value.SetValue((byte)group.map);
            row["gridXNo"].Value.SetValue((byte)group.coordinate.x);
            row["gridZNo"].Value.SetValue((byte)group.coordinate.y);

            row["posX"].Value.SetValue(point.relative.X);
            row["posY"].Value.SetValue(point.relative.Y);
            row["posZ"].Value.SetValue(point.relative.Z);

            row["textId1"].Value.SetValue(textId);

            row["textEnableFlag2Id3"].Value.SetValue(0);
            row["textDisableFlag2Id3"].Value.SetValue(0);

            row["textId3"].Value.SetValue(-1);
            row["textEnableFlagId3"].Value.SetValue((uint)0);
            row["textDisableFlagId3"].Value.SetValue((uint)0);
            row["textType3"].Value.SetValue((byte)0);

            row["entryFEType"].Value.SetValue((byte)(point.important?0:2));  // 0 shows a area title when you walk into it, 2 does not

            AddOrReplaceRow(worldMapPointParam, row);

            return id;
        }

        public int GenerateMessage(string title, string text)
        {
            FsParam messageParam = param[ParamType.TutorialParam];
            FsParam.Row row = CloneRow(messageParam[1010], text[..Math.Min(32, text.Length)], nextMessageParam); // 1010 is the tutorial row for using items
            int textId = textManager.AddTutorial(title, text);

            row["menuType"].Value.SetValue((byte)100);
            row["triggerType"].Value.SetValue((byte)0);
            row["repeatType"].Value.SetValue((byte)1);
            row["imageId"].Value.SetValue((ushort)0);
            row["unlockEventFlagId"].Value.SetValue((uint)0);
            row["textId"].Value.SetValue(textId);

            AddOrReplaceRow(messageParam, row);

            nextMessageParam += 10;
            return row.ID;
        }

        public int GenerateNotification(string text)
        {
            FsParam messageParam = param[ParamType.TutorialParam];
            FsParam.Row row = CloneRow(messageParam[1010], text[..Math.Min(32, text.Length)], nextMessageParam); // 1010 is the tutorial row for using items
            int textId = textManager.AddTutorial(string.Empty, text);

            row["menuType"].Value.SetValue((byte)0);
            row["triggerType"].Value.SetValue((byte)0);
            row["repeatType"].Value.SetValue((byte)1);
            row["imageId"].Value.SetValue((ushort)0);
            row["unlockEventFlagId"].Value.SetValue((uint)0);
            row["textId"].Value.SetValue(textId);

            AddOrReplaceRow(messageParam, row);

            nextMessageParam += 10;
            return row.ID;
        }

        /* Setup both the class and race params. These are refered to as BaseChrParam (origin) FaceParam (base template) */
        /* also edits CharMakeMenuListItemParam and CharMakeMenuTopParam for text on the char creation screen */
        public void GenerateCustomCharacterCreation()
        {
            // Race stuff
            List<Override.PlayerRace> playerRaces = Override.GetCharacterCreationRaces();
            int[] charMakeMenuListItemParam_Races = new int[] { 240, 241, 242, 243, 244, 245, 246, 247, 248, 249 };
            int charMakeMenuListItemParam_Race_Gender_Offset = 20; // add this to above value to get the female version

            FsParam.Row imperialMaleFaceDefault = null;  // setting all class choices to default to imperial male

            FsParam charMakeMenuListItemParam = param[ParamType.CharMakeMenuListItemParam];
            FsParam faceParam = param[ParamType.FaceParam];
            for (int i = 0; i < charMakeMenuListItemParam_Races.Count(); i++)
            {
                Override.PlayerRace playerRace = playerRaces[i];

                // Texy menu entry rows
                int raceRowIdMale = charMakeMenuListItemParam_Races[i];
                int raceRowIdFemale = raceRowIdMale + charMakeMenuListItemParam_Race_Gender_Offset;

                FsParam.Row charMakemMenuListMaleRow = charMakeMenuListItemParam[raceRowIdMale];
                FsParam.Row charMakemMenuListFemaleRow = charMakeMenuListItemParam[raceRowIdFemale];

                int charMakemMenuListRowText = textManager.AddMenuText(playerRace.name, playerRace.description);

                charMakemMenuListMaleRow.Name = $"Male {playerRace.name}";   // row names for helpful debugging
                charMakemMenuListFemaleRow.Name = $"Female {playerRace.name}";

                charMakemMenuListMaleRow["captionId"].Value.SetValue(charMakemMenuListRowText);
                charMakemMenuListFemaleRow["captionId"].Value.SetValue(charMakemMenuListRowText);

                // Face param rows
                FsParam.Row faceMaleRow = faceParam[(int)(charMakemMenuListMaleRow["value"].Value.Value)];
                FsParam.Row faceFemaleRow = faceParam[(int)(charMakemMenuListFemaleRow["value"].Value.Value)];

                faceMaleRow.Name = $"Male {playerRace.name}";   // row names for helpful debugging
                faceFemaleRow.Name = $"Female {playerRace.name}";

                /* Apply facedata from json */
                Override.FaceData maleFaceData = Override.GetFace($"cc_m_{playerRace.name.ToLower().Replace(" ", "")}");
                Override.FaceData femaleFaceData = Override.GetFace($"cc_f_{playerRace.name.ToLower().Replace(" ", "")}");
                foreach (var kvp in maleFaceData.data) { faceMaleRow[kvp.Key].Value.SetValue(kvp.Value); }
                foreach (var kvp in femaleFaceData.data) { faceFemaleRow[kvp.Key].Value.SetValue(kvp.Value); }

                // the burn scars value is used as a race indentifier. this is picked up by scripts and reset to 0 on first game load
                faceMaleRow["burn_scar"].Value.SetValue(playerRace.id);
                faceFemaleRow["burn_scar"].Value.SetValue(playerRace.id);

                if(playerRace.name.ToLower() == "imperial") { imperialMaleFaceDefault = faceMaleRow; }
            }

            // Class stuff
            List<Override.PlayerClass> playerClasses = Override.GetCharacterCreationClasses();
            FsParam baseChrSelectMenuParam = param[ParamType.BaseChrSelectMenuParam];
            FsParam charInitParam = param[ParamType.CharaInitParam];

            int[] baseChrSelectMenuParam_Classes = new int[] { 2000, 2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009 };
            int[] charMakeMenuListItemParam_Classes = new int[] { 100200, 100201, 100202, 100203, 100204, 100205, 100206, 100207, 100208, 100209 };
            for (int i = 0; i < baseChrSelectMenuParam_Classes.Count(); i++)
            {
                Override.PlayerClass playerClass = playerClasses[i];
                FsParam.Row baseClassRow = baseChrSelectMenuParam[baseChrSelectMenuParam_Classes[i]];

                // Base class rows
                int classTextId = textManager.AddMenuText(playerClass.name, playerClass.description);
                baseClassRow.Name = playerClass.name;   // row names for helpful debugging
                baseClassRow["textId"].Value.SetValue(classTextId);

                // Texty menu entry rows
                FsParam.Row charMakeMenuListClassRow = charMakeMenuListItemParam[charMakeMenuListItemParam_Classes[i]];
                charMakeMenuListClassRow.Name = playerClass.name;   // row names for helpful debugging
                charMakeMenuListClassRow["captionId"].Value.SetValue(classTextId);

                // Char init rows
                FsParam.Row classFaceRow = charInitParam[(int)(uint)baseClassRow["chrInitParam"].Value.Value];     // face default for a class
                FsParam.Row originRow = charInitParam[(int)(uint)baseClassRow["originChrInitParam"].Value.Value];  // actual class

                classFaceRow.Name = playerClass.name;   // row names for helpful debugging
                originRow.Name = playerClass.name;

                classFaceRow["npcPlayerFaceGenId"].Value.SetValue(imperialMaleFaceDefault.ID); // make all class choices have the imperial male a their default chr choice

                foreach(var (key, value) in playerClass.data)
                {
                    FsParam.Cell cell = (FsParam.Cell)originRow[key];
                    switch (cell.Value)
                    {
                        case short:
                            cell.SetValue((short)value);
                            break;
                        case byte:
                            cell.SetValue((byte)value);
                            break;
                        default:
                            throw new NotImplementedException($"Type {cell.Value.GetType()} not implemented in GenerateCustomCharacterCreation()");
                    }
                }

                originRow["equip_Wep_Right"].Value.SetValue(-1);
                originRow["equip_Subwep_Right"].Value.SetValue(-1);
                originRow["equip_Wep_Left"].Value.SetValue(-1);
                originRow["equip_Subwep_Left"].Value.SetValue(-1);

                originRow["equip_Helm"].Value.SetValue(-1);
                originRow["equip_Armer"].Value.SetValue(160100);  // SIC
                originRow["equip_Gaunt"].Value.SetValue(-1);
                originRow["equip_Leg"].Value.SetValue(160300);

                originRow["equip_Arrow"].Value.SetValue(-1);
                originRow["equip_SubArrow"].Value.SetValue(-1);
            }

            // Gift stuff
            List<Override.Gift> gifts = Override.GetCharacterCreationGifts();
            int[] charInitParam_Gifts = new int[] { 2400, 2401, 2402, 2403, 2404, 2405, 2406, 2407, 2408, 2409 };
            int[] charMakeMenuListItemParam_Gifts = new int[] { 100300, 100301, 100302, 100303, 100304, 100305, 100306, 100307, 100308, 100309 };
            for (int i = 0; i < baseChrSelectMenuParam_Classes.Count(); i++)
            {
                Override.Gift gift = gifts[i];
                FsParam.Row charMakeMenuListGiftRow = charMakeMenuListItemParam[charMakeMenuListItemParam_Gifts[i]];
                FsParam.Row charInitGiftRow = charInitParam[charInitParam_Gifts[i]];

                // Text in menu
                int classTextId = textManager.AddMenuText(gift.name, gift.description);
                charMakeMenuListGiftRow.Name = gift.name;   // row names for helpful debugging
                charMakeMenuListGiftRow["captionId"].Value.SetValue(classTextId);

                // Change item in CharInit
                charInitGiftRow.Name = gift.name;
                charInitGiftRow["item_03"].Value.SetValue(-1);
                charInitGiftRow["itemNum_03"].Value.SetValue((byte)0);
                charInitGiftRow["equip_Accessory01"].Value.SetValue(-1);

                foreach (var (key, value) in gift.data)
                {
                    FsParam.Cell cell = (FsParam.Cell)charInitGiftRow[key];
                    switch (cell.Value)
                    {
                        case int:
                            cell.SetValue((int)value);
                            break;
                        case byte:
                            cell.SetValue((byte)value);
                            break;
                        default:
                            throw new NotImplementedException($"Type {cell.Value.GetType()} not implemented in GenerateCustomCharacterCreation()");
                    }
                }
            }

            // Minor text tweaks
            {
                FsParam charMakemneuTopParam = param[ParamType.CharMakeMenuTopParam];
                FsParam.Row originRow = charMakemneuTopParam[6];
                FsParam.Row keepsakeRow = charMakemneuTopParam[7];
                FsParam.Row baseRow = charMakemneuTopParam[9];

                originRow["captionId"].Value.SetValue(textManager.AddMenuText("Class", "Select class"));
                //keepsakeRow["captionId"].Value.SetValue(textManager.AddMenuText("Sexy Item", "Select sexy item")); // dont actually wanna change this rn so guh
                baseRow["captionId"].Value.SetValue(textManager.AddMenuText("Race", "Select race"));
            }
        }

        /* Generates an itemlot with a single item and no flag */
        public int GenerateAddItemLot(ItemManager.ItemInfo itemInfo, int quantity)
        {
            FsParam itemLotParam = param[Paramanager.ParamType.ItemLotParam_map];
            FsParam.Row row = CloneRow(itemLotParam[0], $"single, repeatable, scripted, {itemInfo.type}", nextMapItemLotId); // 0 is a default template we created in the constructor

            row["getItemFlagId"].Value.SetValue((uint)0);
            row["lotItemCategory01"].Value.SetValue(itemInfo.ItemLotCategory());
            row["lotItemId01"].Value.SetValue(itemInfo.row);
            row["lotItemNum01"].Value.SetValue((byte)quantity);
            row[$"lotItemBasePoint01"].Value.SetValue((ushort)1000);

            AddOrReplaceRow(itemLotParam, row);
            nextMapItemLotId += 10;
            return row.ID;
        }

        /* Generates an itemlot with a single item with a flag. For item objects placed in the overworld that you can pick up as treasure. */
        public int GenerateContentItemLot(BaseScript script, ItemContent itemContent, ItemManager.ItemInfo itemInfo)
        {
            FsParam itemLotParam = param[Paramanager.ParamType.ItemLotParam_map];
            FsParam.Row row = CloneRow(itemLotParam[0], $"single, not repeatable, map treasure, {itemInfo.type}", nextMapItemLotId); // 0 is a default template we created in the constructor
            Script.Flag itemLotFlag = script.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Item, $"TreasureItem::{itemInfo.type}:{itemInfo.row}");
            itemContent.treasure = itemLotFlag;

            row["getItemFlagId"].Value.SetValue(itemLotFlag.id);
            row["lotItemCategory01"].Value.SetValue(itemInfo.ItemLotCategory());
            row["lotItemId01"].Value.SetValue(itemInfo.row);
            row["lotItemNum01"].Value.SetValue((byte)1);
            row[$"lotItemBasePoint01"].Value.SetValue((ushort)1000);

            script.RegisterItemAsset(this, itemContent);

            AddOrReplaceRow(itemLotParam, row);
            nextMapItemLotId += 10;
            return row.ID;
        }

        /* Generates a map item lot from the inventory of an npccontent with a flag. This is for dead npcs that are just bodies that you loot NOT LIVING ONES! */
        public int GenerateDeadBodyItemLot(BaseScript script, NpcContent npc, List<(ItemManager.ItemInfo item, int quantity)> inventory)
        {
            FsParam itemLotParam = param[Paramanager.ParamType.ItemLotParam_map];
            if (inventory.Count() <= 0) { return -1; } // skip empty inv
            if (inventory.Count() > 10) { throw new Exception($" Inventory itemlot exceeded max entries!"); }

            int i = 0;
            int baseRow = nextMapItemLotId;
            foreach ((ItemManager.ItemInfo item, int quantity) entry in inventory)
            {
                Script.Flag itemLotFlag = script.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Item, $"DeadBody::{npc.id}:{0}");
                if (i == 0) { npc.treasure = itemLotFlag; }
                FsParam.Row row = CloneRow(itemLotParam[0], $"deadbody, not repeatable, {npc.id}:{entry.item.id}:{i}", baseRow+i); // 0 is a default template we created in the constructor

                row["getItemFlagId"].Value.SetValue(itemLotFlag.id);
                row[$"lotItemCategory01"].Value.SetValue(entry.item.ItemLotCategory());
                row[$"lotItemId01"].Value.SetValue(entry.item.row);
                row[$"lotItemNum01"].Value.SetValue((byte)entry.quantity);
                row[$"lotItemBasePoint01"].Value.SetValue((ushort)1000);

                i++;
                AddOrReplaceRow(itemLotParam, row);
            }

            nextMapItemLotId += 10;
            return baseRow;
        }

        /* Generates a map item lot from the inventory of a container with a flag. Barrels, chests, boxes, etc... */
        public int GenerateContainerItemLot(BaseScript script, ContainerContent container, List<(ItemManager.ItemInfo item, int quantity)> inventory)
        {
            FsParam itemLotParam = param[Paramanager.ParamType.ItemLotParam_map];
            if (inventory.Count() <= 0) { return -1; } // skip empty inv
            if (inventory.Count() > 10) { throw new Exception($" Inventory itemlot exceeded max entries!"); }

            int i = 0;
            int baseRow = nextMapItemLotId;
            int totalValue = 0;
            foreach ((ItemManager.ItemInfo item, int quantity) entry in inventory)
            {
                Script.Flag itemLotFlag = script.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Item, $"Container::{container.id}:{0}");
                if(i==0) { container.treasure = itemLotFlag; } 
                FsParam.Row row = CloneRow(itemLotParam[0], $"container, not repeatable, {container.id}:{i}:{entry.item.id}", baseRow + i); // 0 is a default template we created in the constructor

                row["getItemFlagId"].Value.SetValue(itemLotFlag.id);
                row[$"lotItemCategory01"].Value.SetValue(entry.item.ItemLotCategory());
                row[$"lotItemId01"].Value.SetValue(entry.item.row);
                row[$"lotItemNum01"].Value.SetValue((byte)entry.quantity);
                row[$"lotItemBasePoint01"].Value.SetValue((ushort)1000);

                i++;
                totalValue += entry.item.value;
                AddOrReplaceRow(itemLotParam, row);
            }

            script.RegisterContainerAsset(this, container, totalValue);

            nextMapItemLotId += 10;
            return baseRow;
        }

        /* Generates an enemy item lot from the inventory of an npccontent with no flag. This is for LIVING npcs when the player kills them */
        public int GenerateInventoryItemLot(BaseScript script, Content content, List<(ItemManager.ItemInfo item, int quantity)> inventory)
        {
            FsParam itemLotParam = param[Paramanager.ParamType.ItemLotParam_enemy];
            if (inventory.Count() <= 0) { return -1; } // skip empty inv
            if (inventory.Count() > 10) { throw new Exception($" Inventory itemlot exceeded max entries!"); }

            int i = 0;
            int baseRow = nextEnemyItemLotId;
            foreach ((ItemManager.ItemInfo item, int quantity) entry in inventory)
            {
                FsParam.Row row = CloneRow(itemLotParam[584000500], $"npc inventory, repeatable, {content.id}:{i}:{entry.item.id}", baseRow + i); // 584000500 is a blankish one i found that looked good as a base
                
                row["getItemFlagId"].Value.SetValue((uint)0);
                row[$"lotItemCategory01"].Value.SetValue(entry.item.ItemLotCategory());
                row[$"lotItemId01"].Value.SetValue(entry.item.row);
                row[$"lotItemNum01"].Value.SetValue((byte)entry.quantity);
                row[$"lotItemBasePoint01"].Value.SetValue((ushort)1000);

                i++;
                AddOrReplaceRow(itemLotParam, row);
            }

            nextEnemyItemLotId += 10;
            return baseRow;
        }

        public void GenerateLoadingMenuRows(TextManager text)
        {
            // all custom loading menu items are stored in "resources/msg/loadingMenuItems.json"
            // quantity testing hasn't been done yet, but there can be more loading titles than the base game
            // just don't go crazy without telling one of the mods

            if (Const.DEBUG_SKIP_MENU_TEXTURES) return;

            // Grab loading menu text override
            List<Override.LoadingTip> loadingTips = Override.GetLoadingTips();

            // Wipe out loading text param
            FsParam loadingMenuParam = param[ParamType.KnowledgeLoadScreenItemParam];
            loadingMenuParam.ClearRows();

            // Add our new ones in
            foreach (Override.LoadingTip loadingTip in loadingTips)
            {
                FsParam.Row row = new FsParam.Row(loadingMenuParam.Rows.Count()+1, loadingTip.title, loadingMenuParam);
                int textId = textManager.AddLoadingTip(loadingTip.title, loadingTip.text);

                row["disableParam_NT"].Value.SetValue((byte)0);
                row["unlockFlagId"].Value.SetValue((UInt32)0);
                row["invalidFlagId"].Value.SetValue((UInt32)0);
                row["msgId"].Value.SetValue(textId);

                loadingMenuParam.AddRow(row);
            }
        }
    }
}
