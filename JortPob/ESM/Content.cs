﻿using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Transactions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace JortPob
{
    /* Content is effectively any physical object in the game world. Anything that has a physical position in a cell */
    public abstract class Content
    {
        public readonly string id;

        public readonly ESM.Type type;

        public Vector3 relative;
        public readonly Vector3 position, rotation;
        public readonly int scale;  // scale in converted to a int where 100 = 1.0f scale. IE:clamp to nearest 1%. this is to group scale for asset generation.

        public string mesh;  // can be null!

        public Content(JsonNode json, Record record)
        {
            id = json["id"].ToString();

            type = record.type;

            float x = float.Parse(json["translation"][0].ToString());
            float z = float.Parse(json["translation"][1].ToString());
            float y = float.Parse(json["translation"][2].ToString());

            float i = float.Parse(json["rotation"][0].ToString());
            float j = float.Parse(json["rotation"][1].ToString());
            float k = float.Parse(json["rotation"][2].ToString());

            /* The following unholy code converts morrowind (Z up) euler rotations into dark souls (Y up) euler rotations */
            /* Big thanks to katalash, dropoff, and the TESUnity dudes for helping me sort this out */

            /* Katalashes code from MapStudio */
            Vector3 MatrixToEulerXZY(Matrix4x4 m)
            {
                const float Pi = (float)Math.PI;
                const float Deg2Rad = Pi / 180.0f;
                Vector3 ret;
                ret.Z = MathF.Asin(-Math.Clamp(-m.M12, -1, 1));

                if (Math.Abs(m.M12) < 0.9999999)
                {
                    ret.X = MathF.Atan2(-m.M32, m.M22);
                    ret.Y = MathF.Atan2(-m.M13, m.M11);
                }
                else
                {
                    ret.X = MathF.Atan2(m.M23, m.M33);
                    ret.Y = 0;
                }
                ret.X = ret.X <= -180.0f * Deg2Rad ? ret.X + 360.0f * Deg2Rad : ret.X;
                ret.Y = ret.Y <= -180.0f * Deg2Rad ? ret.Y + 360.0f * Deg2Rad : ret.Y;
                ret.Z = ret.Z <= -180.0f * Deg2Rad ? ret.Z + 360.0f * Deg2Rad : ret.Z;
                return ret;
            }

            /* Adapted code from https://github.com/ColeDeanShepherd/TESUnity */
            Quaternion xRot = Quaternion.CreateFromAxisAngle(new Vector3(1.0f, 0.0f, 0.0f), i);
            Quaternion yRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 1.0f, 0.0f), k);
            Quaternion zRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 0.0f, 1.0f), j);
            Quaternion q = xRot * zRot * yRot;

            Vector3 eu = MatrixToEulerXZY(Matrix4x4.CreateFromQuaternion(q));

            relative = new();
            position = new Vector3(x, y, z) * Const.GLOBAL_SCALE;
            rotation = eu * (float)(180 / Math.PI);
            scale = (int)((json["scale"] != null ? float.Parse(json["scale"].ToString()) : 1f) * 100);
        }
    }

    /* npcs, humanoid only */
    public class NpcContent : Content
    {
        public NpcContent(JsonNode json, Record record) : base(json, record)
        {
            // Kinda stubby for now
        }
    }

    /* creatures, both leveled and non-leveled */
    public class CreatureContent : Content
    {
        public CreatureContent(JsonNode json, Record record) : base(json, record)
        {
            // Kinda stubby for now
        }
    }

    /* static meshes to be converted to assets */
    public class AssetContent : Content
    {
        public AssetContent(JsonNode json, Record record) : base(json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
        }
    }

    /* static meshes that have emitters/lights EX: candles/campfires -- converted to assets but also generates ffx files and params to make them work */
    public class EmitterContent : Content
    {
        public readonly string mesh;

        public EmitterContent(JsonNode json, Record record) : base(json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
        }
    }

    /* invisible lights with no static mesh associated */
    public class LightContent : Content 
    {
        public LightContent(JsonNode json, Record record) : base(json, record)
        {

        }
    }
}
