using System;
using JortPob.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using SoulsFormats;
using SoulsIds;

namespace JortPob.Worker
{
    public class EsdWorker(List<NpcManager.EsdInfo> esds) : IWorker<Unit>
    {
        /**
         * Naive implementation of using ESDLang as a library. Since it processes everything
         * linearly in a single thread we still get slowed down by IO. Ideally, we would break up
         * the list of ESDs into reasonably-large chunks, and we'd defer writing them to disk until we've accumulated
         * a few.
         */
        private Unit Run()
        {
            Predicate<string> allowAnyFilter = delegate { return true; };
            ESDLang.EzSemble.EzSembleContext context = LoadEsdDocumentationContext();
            List<EsdDescriptor> esdDescriptors = LoadEsdDescriptors();

            EsdDescriptor templateEsd = esdDescriptors.Last();
            NpcManager.EsdInfo templateEsdInfo = esds.First();

            ESDLang.Script.ESDOptions compilerOptions = ESDLang.Script.ESDOptions.Parse(
                ["-er", "-i", templateEsdInfo.py, "-writeloose", templateEsdInfo.esd]
            );
            ESDLang.Script.Compiler compiler = new ESDLang.Script.Compiler(context, compilerOptions);
            
            foreach(var esdInfo in esds)
            {
                Dictionary<string, ESD> result = compiler.Compile(esdInfo.py, templateEsd.Esd, allowAnyFilter);

                if (result == null)
                {
                    throw new Exception($"Failed to compile {esdInfo.py}");
                }

                foreach (KeyValuePair<string, ESD> entry in result)
                {
                    ESD outEsd = entry.Value;
                    // Filtered out
                    if (outEsd == null) continue;
                    string esdName = ESDLang.Script.ESDName.FromFunctionPrefix(entry.Key);
                    string outPath = esdInfo.esd.Replace("%e", esdName);
                    outEsd.Write(outPath); // this is where a lot of time is spent due to I/O
                }

                Lort.TaskIterate();
            }
            
            return Unit.Default;
        }

        public Unit Go()
        {
            if (esds.Count == 0)
            {
                return Unit.Default;
            }

            Lort.Log($"Compiling {esds.Count} ESDs...", Lort.Type.Main);
            Lort.NewTask("Compiling ESDs", esds.Count);

            return Run();
        }

        private struct EsdDescriptor
        {
            public ESD Esd = null;
            public SortedSet<string> Chr = [];

            public EsdDescriptor()
            {
            }
        }

        /**
         * Loads some static documentation used by the compiler.
         */
        private static ESDLang.EzSemble.EzSembleContext LoadEsdDocumentationContext()
        {
            ESDLang.Script.ESDOptions.CmdType talkDocType = ESDLang.Script.ESDOptions.CmdType.Talk;
            ESDLang.EzSemble.EzSembleContext context = ESDLang.EzSemble.EzSembleContext.LoadFromXml(
                Utility.ResourcePath($"esd\\ESDScriptingDocumentation_{talkDocType}.xml")
            );
            context.Doc = ESDLang.Doc.ESDDocumentation.DeserializeFromFile(
                Utility.ResourcePath($"esd\\ESDScriptingDocumentation_{talkDocType}.json"),
                new ESDLang.Doc.ESDDocumentation.DocOptions { Game = GameSpec.FromGame.ER.ToString().ToLowerInvariant() }
            );
            return context;
        }

        /**
         * Scrapes/loads ESD information from the game directory. We only really *need* one file to use a template,
         * but this has been (somewhat) faithfully ported from esdtool such that it reads everything it can.
         */
        private static List<EsdDescriptor> LoadEsdDescriptors()
        {
            // If no data, and loads are called for, path will be used instead.
            EsdDescriptor buildDescriptor(string path, byte[] data = null)
            {
                EsdDescriptor desc = new();

                if (data == null)
                {
                    data = File.ReadAllBytes(path);
                    data = DCX.Is(data) ? DCX.Decompress(data) : data;
                }

                desc.Esd = ESD.Read(data);

                return desc;
            }

            // Create the spec for EldenRing
            string gameDir = WindowsifyPath(Path.Combine(Const.ELDEN_PATH, "Game"));
            SoulsIds.GameSpec spec = SoulsIds.GameSpec.ForGame(SoulsIds.GameSpec.FromGame.ER);
            spec.GameDir = gameDir;

            // Define universe/editor
            SoulsIds.Universe universe = new();
            SoulsIds.Scraper scraper = new(spec, Path.Combine(gameDir, "empty"));
            SoulsIds.GameEditor editor = new GameEditor(spec);

            // Back-fill map info into the universe
            scraper.ScrapeMaps(universe);

            // Glob for all files we want to read
            string absDir = GameEditor.AbsolutePath(spec.GameDir, spec.EsdDir);
            IEnumerable<string> paths = Directory.GetFiles(absDir, "*.esd")
                .Concat(Directory.GetFiles(absDir, "*.esd.dcx"))
                .Concat(Directory.GetFiles(absDir, "*esdbnd.dcx"))
                .Concat(Directory.GetFiles(absDir, "*esdbnd"))
                .Select(WindowsifyPath);

            List<EsdDescriptor> descriptors = [];

             foreach (string path in paths)
            {
                if (path.EndsWith(".esd") || path.EndsWith(".esd.dcx"))
                {
                    descriptors.Add(buildDescriptor(path));
                }
                // Not an extension exactly - the real extensions are chresdbnd and talkesdbnd.
                else if (path.EndsWith("esdbnd.dcx") || path.EndsWith("esdbnd"))
                {
                    string map = GameEditor.BaseName(path);
                    foreach (KeyValuePair<string, byte[]> esds in editor.LoadBnd(path, (data, name) =>data))
                    {
                        string esdName = esds.Key;
                        // Special processing, because multiple ESDs are named dummy. They are parsed out correctly again later.
                        if (esdName.Equals("dummy"))
                        {
                            esdName = $"{esdName}_{map}";
                        }

                        EsdDescriptor desc = new();

                        // This block is just to have behavioral compatibility with the ESDTool CLI.
                        // We don't actually use the Chr field.
                        if (esdName.StartsWith("t") && int.TryParse(esdName.Substring(1), out int esdId))
                        {
                            SoulsIds.Universe.Obj obj = SoulsIds.Universe.Obj.Esd(esdId);
                            foreach (SoulsIds.Universe.Obj part in universe.Prev(obj, SoulsIds.Universe.Verb.CONTAINS, SoulsIds.Universe.Namespace.Part))
                            {
                                foreach (SoulsIds.Universe.Obj chr in universe.Next(part, SoulsIds.Universe.Verb.CONTAINS, SoulsIds.Universe.Namespace.ChrModel))
                                {
                                    desc.Chr.Add(chr.ID);
                                }

                                foreach (SoulsIds.Universe.Obj npc in universe.Next(part, SoulsIds.Universe.Verb.CONTAINS, SoulsIds.Universe.Namespace.Human))
                                {
                                    if (int.TryParse(npc.ID, out int id))
                                    {
                                        desc.Chr.Add("h" + npc.ID);
                                    }
                                }
                            }
                        }

                        descriptors.Add(buildDescriptor(path, esds.Value));
                    }
                }
            }

            return descriptors;
        }

        protected static string WindowsifyPath(string path)
        {
            if (path == null) return null;
            if (path.StartsWith("/mnt/"))
            {
                string drive = path[5].ToString();
                return $"{drive.ToUpper()}:{path.Substring(6)}";
            }
            return path;
        }
    }
}
