﻿using HKLib.hk2018.hkaiCollisionAvoidance;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob.Common
{
    public class Bind
    {
        public static void BindMaterials(string outPath)
        {
            BND4 bnd = BND4.Read(Utility.ResourcePath($"matbins\\allmaterial.matbinbnd.dcx"));

            /* Grab all matbin files */
            string[] fileList = Directory.GetFiles($"{Const.CACHE_PATH}materials");
            int i = 15102; // appending our new file indexes after all the base game ones
            foreach (string file in fileList)
            {
                MATBIN matbin = MATBIN.Read(file);
                BinderFile bind = new();
                bind.CompressionType = SoulsFormats.DCX.Type.Zlib;
                bind.Flags = SoulsFormats.Binder.FileFlags.Flag1;
                bind.ID = ++i;
                bind.Name = $"N:\\GR\\data\\INTERROOT_win64\\material\\matbin\\Morrowind\\matxml\\{Utility.PathToFileName(file)}.matbin";
                bind.Bytes = matbin.Write();
                bnd.Files.Add(bind);
            }

            bnd.Write(outPath);
        }

        /* Binds all assets to correct asset directories */
        public static void BindAssets(Cache cache)
        {
            int partition = (int)Math.Ceiling(cache.assets.Count / (float)Const.THREAD_COUNT);
            List<BindWorker> workers = new();
            for (int i = 0; i < Const.THREAD_COUNT; i++)
            {
                int start = i * partition;
                int end = start + partition;
                BindWorker worker = new(cache, start, end);
                workers.Add(worker);
            }

            /* Wait for threads to finish */
            while (true)
            {
                bool done = true;
                foreach (BindWorker worker in workers)
                {
                    done &= worker.IsDone;
                }

                if (done)
                    break;
            }
        }

        public static void BindAsset(ModelInfo modelInfo, string outPath)
        {

            // Bind up asset flver
            {
                BND4 bnd = new();
                bnd.Compression = SoulsFormats.DCX.Type.DCX_DFLT_11000_44_9;
                bnd.Extended = 4;
                bnd.Format = SoulsFormats.Binder.Format.IDs | SoulsFormats.Binder.Format.Names1 | SoulsFormats.Binder.Format.Names2 | SoulsFormats.Binder.Format.Compression;
                bnd.Unicode = true;
                bnd.Version = "07D7R6";

                FLVER2 flver = FLVER2.Read($"{Const.CACHE_PATH}{modelInfo.path}");

                BinderFile file = new();
                file.CompressionType = SoulsFormats.DCX.Type.Zlib;
                file.Flags = SoulsFormats.Binder.FileFlags.Flag1;
                file.ID = 200;
                file.Name = $"N:\\GR\\data\\INTERROOT_win64\\asset\\aeg\\{modelInfo.AssetPath()}\\sib\\{modelInfo.AssetName()}.flver";
                file.Bytes = flver.Write();

                bnd.Files.Add(file);
                bnd.Write(outPath);
            }

            // If this asset has collision we bind that up as well
            if(modelInfo.collision != null)
            {
                BND4 bnd = new();
                bnd.Compression = SoulsFormats.DCX.Type.DCX_KRAK;
                bnd.Extended = 4;
                bnd.Format = SoulsFormats.Binder.Format.IDs | SoulsFormats.Binder.Format.Names1 | SoulsFormats.Binder.Format.Names2 | SoulsFormats.Binder.Format.Compression;
                bnd.Unicode = true;
                bnd.Version = "07D7R6";

                BinderFile file = new();
                file.CompressionType = SoulsFormats.DCX.Type.Zlib;
                file.Flags = SoulsFormats.Binder.FileFlags.Flag1;
                file.ID = 300;
                file.Name = $"N:\\GR\\data\\INTERROOT_win64\\asset\\aeg\\{modelInfo.AssetPath().ToUpper()}\\hkx_L\\{modelInfo.AssetName().ToUpper()}_L.hkx";
                file.Bytes = File.ReadAllBytes($"{Const.CACHE_PATH}{modelInfo.collision.path}");

                bnd.Files.Add(file);
                bnd.Write(outPath.Replace(".geombnd.dcx", "_l.geomhkxbnd.dcx"));
            }
        }

        public static void BindTPF(Cache cache, string outPath)
        {
            /* Collect all textures, kind of brute force, could optimize later */
            List<TextureInfo> textures = new();
            bool TextureExists(TextureInfo t)
            {
                foreach (TextureInfo tex in textures)
                {
                    if (t.name == tex.name) { return true; }
                }
                return false;
            }

            foreach (ModelInfo mod in cache.assets)
            {
                foreach(TextureInfo tex in mod.textures)
                {
                    if (TextureExists(tex)) { continue; }
                    textures.Add(tex);
                }
            }

            foreach(TerrainInfo terrain in cache.terrains)
            {
                foreach(TextureInfo tex in terrain.textures)
                {
                    if (TextureExists(tex)) { continue; }
                    textures.Add(tex);
                }
            }

            /* Bind all textures */
            BXF4 tpfbdt = new();
            tpfbdt.Extended = 4;
            tpfbdt.Format = SoulsFormats.Binder.Format.IDs | SoulsFormats.Binder.Format.Names1 | SoulsFormats.Binder.Format.Names2 | SoulsFormats.Binder.Format.Compression;
            tpfbdt.Unicode = true;
            tpfbdt.Version = "25E14X35";
            int index = 0;
            foreach (TextureInfo tex in textures)
            {
                TPF tpf = TPF.Read($"{Const.CACHE_PATH}{tex.path}");

                BinderFile bf = new();
                bf.CompressionType = DCX.Type.None;
                bf.Flags = SoulsFormats.Binder.FileFlags.Flag1;
                bf.ID = index++;
                bf.Name = $"{tex.name}.tpf.dcx";
                bf.Bytes = tpf.Write();
                tpfbdt.Files.Add(bf);
            }

            /* Write bind */
            tpfbdt.Write($"{outPath}.tpfbhd", $"{outPath}.tpfbdt");
        }
    }
}
