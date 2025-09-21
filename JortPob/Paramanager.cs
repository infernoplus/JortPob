using JortPob.Common;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using WitchyFormats;
using static SoulsAssetPipeline.Audio.Wwise.WwiseBlock;

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

        public readonly Dictionary<ParamType, FsParam> param;

        public short terrainDrawParamID;
        private Dictionary<int, int> lodPartDrawParamIDs; // first int is the index of the array from Const.ASSET_LOD_VALUES, second int is the param row id

        public readonly Dictionary<int, int> talkRowData;

        public Paramanager()
        {
            SoulsFormats.BND4 paramBnd = SoulsFormats.SFUtil.DecryptERRegulation(Utility.ResourcePath(@"misc\regulation.bin"));
            string[] files = Directory.GetFiles(Utility.ResourcePath(@"misc\paramdefs"));

            Dictionary<ParamDefType, WitchyFormats.PARAMDEF> paramdefs = new();
            foreach (string file in files)
            {
                WitchyFormats.PARAMDEF p = WitchyFormats.PARAMDEF.XmlDeserialize(file);
                Lort.TaskIterate();
                try
                {
                    ParamDefType ty = (ParamDefType)System.Enum.Parse(typeof(ParamDefType), p.ParamType);
                    paramdefs.Add(ty, p);
                }
                catch (Exception ex)
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

            GC.Collect(); // maybe fixes a bug with fsparam. 80% sure

            talkRowData = new();
        }

        public void AddRow(FsParam param, FsParam.Row row)
        {
            FsParam.Row oldrow = param[row.ID];
            if (oldrow != null)
                param.RemoveRow(oldrow);
            param.AddRow(row);
        }

        public FsParam.Row CloneRow(FsParam.Row row, string name, int newId)
        {
            var clone = new FsParam.Row(row);
            clone.ID = newId;
            clone.Name = name;
            return clone;
        }

        public void Write()
        {
            Lort.Log($"Binding {param.Count()} PARAMs...", Lort.Type.Main);
            Lort.NewTask($"Binding PARAMs", param.Count());
            Lort.Log($"Total TalkParam rows: {param[Paramanager.ParamType.TalkParam].Rows.Count()} out of a max of {ushort.MaxValue}", Lort.Type.Debug);

            /* Create talkrow binary for nords thingy */
            string talkRowBinaryPath = $"{Const.OUTPUT_PATH}TalkRowBinary.bin";
            List<byte> rawData = new();
            foreach (KeyValuePair<int, int> kvp in talkRowData)
            {
                rawData.AddRange(BitConverter.GetBytes(kvp.Key));
                rawData.AddRange(BitConverter.GetBytes(kvp.Value));
            }
            System.IO.File.WriteAllBytes(talkRowBinaryPath, rawData.ToArray());

            /* Bnd and write params */
            BND4 bnd = new();
            bnd.Compression = SoulsFormats.DCX.Type.DCX_ZSTD;
            bnd.Version = "11601000";
            int i = 0;
            foreach (KeyValuePair<ParamType, FsParam> kvp in param)
            {
                FsParam param = kvp.Value;
              
                if (Const.DEBUG_ENABLE_FMG_PARAM_SORTING)
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
            SFUtil.EncryptERRegulation($"{Const.OUTPUT_PATH}regulation.bin", bnd);
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
                if (asset.IsDynamic())
                {
                    // Clone a specific row as our baseline
                    FsParam.Row row = CloneRow(electedStoneBuildingRow, asset.name, asset.AssetRow());   // 7077 is a big stone building part in the overworld

                    // Set some values and add
                    drawParamID.SetValue(row, AssetPartDrawParamBySize(asset));        // DrawParamID
                    hitType.SetValue(row, (sbyte)0);           // Hit type (LO ONLY)
                    behaviourType.SetValue(row, (byte)0);           // BehaviourType, affects HKX scaling and breakability
                    AddRow(assetParam, row);
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
                    AddRow(assetParam, row);
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
                    AddRow(assetParam, row);
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

                    AddRow(emitterParam, row);
                }
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
                AddRow(assetParam, row);
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
                AddRow(drawParam, row);
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
                AddRow(drawParam, row);
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
                for (int i = 0; i < match.Count(); i++) { match[i] = match[i].Trim().ToLower(); }
                this.MapInfoParamId = MapInfoParamId;
                this.MapRegionParamId = MapRegionParamId;
                this.rem = rem;
            }
        }

        // @TODO: maybe move weatherdata to layout or its own class since its used by multiple toher thingsthingso

        public static List<WeatherData> EXTERIOR_WEATHER_DATA_LIST = new()
        {
            new WeatherData("Limgrave", new(){ "West Gash Region", "Ascadian Isles Region", "Grazelands Region" }, 60423600, 99999901, EnvManager.Rem.Forest), // 0
            new WeatherData("Liurnia", new(){ "Bitter Coast Region", "Sheogorad", "Azura's Coast Region" }, 60374200, 60365000, EnvManager.Rem.Forest), // 10
            new WeatherData("Altus", new(){ }, 60395000, 60395000, EnvManager.Rem.Forest), // 20
            new WeatherData("Gelmir", new(){ "Ashlands Region", "Molag Mar Region" }, 60355200, 60355200, EnvManager.Rem.Mountain), // 21
            new WeatherData("Caelid", new(){ }, 60473700, 60473700, EnvManager.Rem.Mountain), // 30
            new WeatherData("Caelid Desert", new(){ "Red Mountain Region" }, 60533800, 60533800, EnvManager.Rem.Mountain), // 31
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
            foreach (Tile tile in layout.tiles)
            {
                if (tile.IsEmpty()) { continue; } // skip empty tiles

                string region = tile.GetRegion();
                WeatherData weatherData = null;
                foreach (WeatherData w in EXTERIOR_WEATHER_DATA_LIST)
                {
                    if (w.match.Contains(region))
                    {
                        weatherData = w; break;
                    }
                }

                int id = int.Parse($"60{tile.coordinate.x.ToString("D2")}{tile.coordinate.y.ToString("D2")}00");

                /* MapInfoParam */ // controls sky and weather
                FsParam.Row rowA = CloneRow(mapInfoParam[weatherData.MapInfoParamId], $"mw ext m{tile.map} {tile.coordinate.x} {tile.coordinate.y} {tile.block}", id);
                AddRow(mapInfoParam, rowA);

                /* MapRegionParam */ // controls gparam
                FsParam.Row rowB = CloneRow(mapRegionParam[weatherData.MapRegionParamId], $"mw ext m{tile.map} {tile.coordinate.x} {tile.coordinate.y} {tile.block}", id);
                AddRow(mapRegionParam, rowB);
            }

            // Interior msbs
            foreach (InteriorGroup group in layout.interiors)
            {
                if (group.IsEmpty()) { continue; } // skip empty tiles

                WeatherData weatherData = group.GetWeather();

                int id = int.Parse($"{group.map:D2}{group.area:D2}{group.unk:D2}{group.block:D2}");

                /* MapInfoParam */ // controls sky and weather
                FsParam.Row rowA = CloneRow(mapInfoParam[weatherData.MapInfoParamId], $"mw int m{group.map} {group.area} {group.unk} {group.block}", id);
                AddRow(mapInfoParam, rowA);

                /* MapRegionParam */ // controls gparam
                FsParam.Row rowB = CloneRow(mapRegionParam[weatherData.MapRegionParamId], $"mw int m{group.map} {group.area} {group.unk} {group.block}", id);
                AddRow(mapRegionParam, rowB);
            }
        }

        public void GenerateTalkParam(TextManager textManager, List<NpcManager.TopicData> topicData)
        {
            foreach (NpcManager.TopicData topic in topicData)
            {
                foreach (NpcManager.TopicData.TalkData talk in topic.talks)
                {
                    for (int i = 0; i < talk.talkRows.Count(); i++)
                    {
                        int id = talk.talkRows[i];
                        string text = talk.splitText[i];

                        // If exists skip, duplicates happen during gen of these params beacuse a single talkparam can be used by any number of npcs. Hundreds in some cases.
                        if (talkRowData.ContainsKey(id)) { continue; }
                        textManager.AddTalk(id * 10, text);
                        talkRowData.Add(id, id * 10);
                    }
                }
            }

            /*
            FsParam talkParam = param[ParamType.TalkParam];
            FsParam.Row templateTalkRow = talkParam[1400000]; // 1400000 is a line from opening cutscene

            FsParam.Column msgId = talkParam["msgId"];
            FsParam.Column voiceId = talkParam["voiceId"];

            FsParam.Column msgId_female = talkParam["msgId_female"];
            FsParam.Column voiceId_female = talkParam["voiceId_female"];

            foreach (NpcManager.TopicData topic in topicData)
            {
                foreach (NpcManager.TopicData.TalkData talk in topic.talks)
                {
                    for (int i = 0; i < talk.talkRows.Count(); i++)
                    {
                        int id = talk.talkRows[i];
                        string text = talk.splitText[i];

                        // If exists skip, duplicates happen during gen of these params beacuse a single talkparam can be used by any number of npcs. Hundreds in some cases.
                        if (talkParam[id] != null) { continue; }

                        // truncating the text in the row name because it can cause issues if it is too long
                        FsParam.Row row = CloneRow(templateTalkRow, text.Substring(0, Math.Min(32, text.Length)), id); // 1400000 is a line from opening cutscene

                        msgId.SetValue(row, id * 10); // message id (male)
                        voiceId.SetValue(row, id * 10); // message id (male)

                        msgId_female.SetValue(row, id * 10); // message id (female)
                        voiceId_female.SetValue(row, id * 10); // message id (female)

                        textManager.AddTalk(id * 10, text);
                        AddRow(talkParam, row);
                    }
                }
            }*/
        }

        public void GenerateNpcParam(TextManager textManager, int id, NpcContent npc)
        {
            FsParam npcParam = param[ParamType.NpcParam];
            FsParam.Row row = CloneRow(npcParam[523010000], npc.name, id); // 523010000 is white mask varre

            int textId = textManager.AddNpcName(npc.name);
            row.Cells[5].SetValue(textId); // nameId
            row.Cells[105].SetValue((byte)(npc.hostile ? 27 : 26)); // team type (hostile=27, friendly=26)

            AddRow(npcParam, row);
        }

        public void GenerateActionButtonParam(TextManager textManager, int id, string text)
        {
            FsParam actionParam = param[ParamType.ActionButtonParam];
            FsParam.Row row = CloneRow(actionParam[1000], text, id); // 1000 is the prompt to pickup runes

            int textId = textManager.AddActionButton(text);

            row.Cells[5].SetValue(2f);   // radius
            row.Cells[9].SetValue(1.5f); // hegiht high
            row.Cells[10].SetValue(-1.5f); // height low
            row.Cells[23].SetValue(textId);

            AddRow(actionParam, row);
        }

        /* Set all map placenames to "Morrowind" for now. Later we can edit the map mask and setup the regions properly */
        public void SetAllMapLocation(TextManager textManager)
        {
            int textId = textManager.AddLocation("Morrowind");

            FsParam.Column mapNameId = param[ParamType.MapNameTexParam]["mapNameId"];

            foreach(FsParam.Row row in param[ParamType.MapNameTexParam].Rows)
            {
               mapNameId.SetValue(row, textId);
            }

            textManager.SetLocation(10010, "Morrowind");  // it seems like chapel of anticiaption is the default when the game doesnt know where you are so making that a generic as well
        }

        /* Generate or get an already generated worldmappoint to be used as a placename. Not for actual map icons! */
        public int GenerateWorldMapPoint(TextManager textManager, BaseTile tile, Cell cell, Vector3 relative, int id)
        {
            FsParam worldMapPointParam = param[ParamType.WorldMapPointParam];
            FsParam.Row row = CloneRow(worldMapPointParam[61423600], $"{cell.name} placename", id); // 61423600 is limgrave church of elleh placename

            int textId = textManager.AddLocation(cell.name);

            row["eventFlagId"].Value.SetValue(60000u);  // idk if we need to genrate ids, this seems to just work
            row["dispMask00"].Value.SetValue((byte)0);

            row["areaNo"].Value.SetValue((byte)tile.map);
            row["gridXNo"].Value.SetValue((byte)tile.coordinate.x);
            row["gridZNo"].Value.SetValue((byte)tile.coordinate.y);

            row["posX"].Value.SetValue(relative.X);
            row["posY"].Value.SetValue(relative.Y);
            row["posZ"].Value.SetValue(relative.Z);

            row["textId1"].Value.SetValue(textId);

            AddRow(worldMapPointParam, row);

            return id;
        }

        /* Same as above but for interiors */
        public int GenerateWorldMapPoint(TextManager textManager, InteriorGroup group, Cell cell, Vector3 relative, int id)
        {
            FsParam worldMapPointParam = param[ParamType.WorldMapPointParam];
            FsParam.Row row = CloneRow(worldMapPointParam[61423600], $"{cell.name} placename", id); // 61423600 is limgrave church of elleh placename

            int textId = textManager.AddLocation(cell.name);

            row["eventFlagId"].Value.SetValue(60000u);  // idk if we need to genrate ids, this seems to just work
            row["dispMask00"].Value.SetValue((byte)0);

            row["areaNo"].Value.SetValue((byte)group.map);
            row["gridXNo"].Value.SetValue((byte)group.area);
            row["gridZNo"].Value.SetValue((byte)group.unk);

            row["posX"].Value.SetValue(relative.X);
            row["posY"].Value.SetValue(relative.Y);
            row["posZ"].Value.SetValue(relative.Z);

            row["textId1"].Value.SetValue(textId);

            AddRow(worldMapPointParam, row);

            return id;
        }

        /* Setup both the class and race params. These are refered to as BaseChrParam (origin) FaceParam (base template) */
        /* also edits CharMakeMenuListItemParam and CharMakeMenuTopParam for text on the char creation screen */
        public void GenerateCustomCharacterCreation(TextManager textManager)
        {
            // Race stuff
            int[] charMakeMenuListItemParam_Races = new int[] { 240, 241, 242, 243, 244, 245, 246, 247, 248, 249 };
            int charMakeMenuListItemParam_Race_Gender_Offset = 20; // add this to above value to get the female version

            JsonNode jsonRaces = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_race.json")));

            FsParam charMakeMenuListItemParam = param[ParamType.CharMakeMenuListItemParam];
            FsParam faceParam = param[ParamType.FaceParam];
            for (int i = 0; i < charMakeMenuListItemParam_Races.Count(); i++)
            {
                JsonNode jsonRace = jsonRaces.AsArray()[i];

                // Texy menu entry rows
                int raceRowIdMale = charMakeMenuListItemParam_Races[i];
                int raceRowIdFemale = raceRowIdMale + charMakeMenuListItemParam_Race_Gender_Offset;

                FsParam.Row charMakemMenuListMaleRow = charMakeMenuListItemParam[raceRowIdMale];
                FsParam.Row charMakemMenuListFemaleRow = charMakeMenuListItemParam[raceRowIdFemale];

                int charMakemMenuListRowText = textManager.AddMenuText(jsonRace["race"].ToString(), jsonRace["description"].ToString());

                charMakemMenuListMaleRow.Name = $"Male {jsonRace["race"].GetValue<string>()}";   // row names for helpful debugging
                charMakemMenuListFemaleRow.Name = $"Female {jsonRace["race"].GetValue<string>()}";

                charMakemMenuListMaleRow["captionId"].Value.SetValue(charMakemMenuListRowText);
                charMakemMenuListFemaleRow["captionId"].Value.SetValue(charMakemMenuListRowText);

                // Face param rows
                FsParam.Row faceMaleRow = faceParam[(int)(charMakemMenuListMaleRow["value"].Value.Value)];
                FsParam.Row faceFemaleRow = faceParam[(int)(charMakemMenuListFemaleRow["value"].Value.Value)];

                byte raceId = byte.Parse(jsonRace["id"].ToString());  // these values match the enums in the NpcContent class

                faceMaleRow.Name = $"Male {jsonRace["race"].GetValue<string>()}";   // row names for helpful debugging
                faceFemaleRow.Name = $"Female {jsonRace["race"].GetValue<string>()}";

                faceMaleRow["burn_scar"].Value.SetValue(raceId);  // the burn scars value is used as a race indentifier. this is picked up by scripts and reset to 0 on first game load
                faceFemaleRow["burn_scar"].Value.SetValue(raceId);
            }

            // Class stuff
            JsonNode jsonClasses = JsonNode.Parse(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_class.json")));
            FsParam baseChrSelectMenuParam = param[ParamType.BaseChrSelectMenuParam];
            FsParam charInitParam = param[ParamType.CharaInitParam];

            int[] baseChrSelectMenuParam_Classes = new int[] { 2000, 2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008, 2009 };
            int[] charMakeMenuListItemParam_Classes = new int[] { 100200, 100201, 100202, 100203, 100204, 100205, 100206, 100207, 100208, 100209 };
            for (int i = 0; i < baseChrSelectMenuParam_Classes.Count(); i++)
            {
                JsonNode jsonClass = jsonClasses.AsArray()[i];
                FsParam.Row baseClassRow = baseChrSelectMenuParam[baseChrSelectMenuParam_Classes[i]];

                // Base class rows
                int classTextId = textManager.AddMenuText(jsonClass["class"].GetValue<string>(), jsonClass["description"].GetValue<string>());
                baseClassRow.Name = jsonClass["class"].GetValue<string>();   // row names for helpful debugging
                baseClassRow["textId"].Value.SetValue(classTextId);

                // Texty menu entry rows
                FsParam.Row charMakeMenuListClassRow = charMakeMenuListItemParam[charMakeMenuListItemParam_Classes[i]];
                charMakeMenuListClassRow.Name = jsonClass["class"].GetValue<string>();   // row names for helpful debugging
                charMakeMenuListClassRow["captionId"].Value.SetValue(classTextId);

                // Char init rows
                FsParam.Row classRow = charInitParam[(int)(uint)baseClassRow["chrInitParam"].Value.Value];
                FsParam.Row originRow = charInitParam[(int)(uint)baseClassRow["originChrInitParam"].Value.Value];

                classRow.Name = jsonClass["class"].GetValue<string>();   // row names for helpful debugging
                originRow.Name = jsonClass["class"].GetValue<string>();
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

        /* Die */ // @TODO: maybe move all row removal into the constructor since it can just *happen*
        public void KillMapHeightParams()
        {
            /* Delete most of these */
            FsParam mapGridHeightParam = param[ParamType.MapGridCreateHeightLimitInfoParam];
            for (int i = 0; i < mapGridHeightParam.Rows.Count(); i++)
            {
                FsParam.Row row = mapGridHeightParam.Rows[i];
                if (row.ID >= 99999901) { continue; } // keep some base params
                mapGridHeightParam.RemoveRow(row);
                i--;
            }

            /* Delete most of these */
            FsParam mapGridHeightDetailParam = param[ParamType.MapGridCreateHeightDetailLimitInfo];
            for (int i = 0; i < mapGridHeightDetailParam.Rows.Count(); i++)
            {
                FsParam.Row row = mapGridHeightDetailParam.Rows[i];
                if (row.ID <= 2) { continue; } // keep some base params
                mapGridHeightDetailParam.RemoveRow(row);
                i--;
            }
        }
    }
}
