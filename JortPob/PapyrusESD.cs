using JortPob.Scripts;
using System;
using System.Linq;
using static JortPob.Papyrus;

namespace JortPob
{
    /* Big stub! We may end up using this at some point but for the moment I am going to plan on avoiding ESD for papyrus. See PapyrusEMEVD for the real implementation */
    public class PapyrusESD
    {
        private readonly ESM esm;
        private readonly ScriptManager scriptManager;
        private readonly Paramanager paramanager;
        private readonly TextManager textManager;
        private readonly BaseScript areaScript;
        private readonly Content content;

        private readonly string esd;

        public PapyrusESD(ESM esm, ScriptManager scriptManager, Paramanager paramanager, TextManager textManager, BaseScript areaScript, Content content, Papyrus papyrus, uint id)
        {
            this.esm = esm;
            this.scriptManager = scriptManager;
            this.paramanager = paramanager;
            this.textManager = textManager;
            this.areaScript = areaScript;
            this.content = content;

            string s;
            s =  $"# script '{papyrus.id}' for object '{content.id}' with with entity id '{content.entity}'\r\n";
            s += $"def t{id:D9}_1():\r\n";
            s += $"    while True:\r\n";

            for (int i = 0; i < papyrus.scope.calls.Count(); i++)
            {
                Papyrus.Call call = papyrus.scope.calls[i];
                s += HandlePapyrus(call, 2);
            }

            s += $"    Quit()\r\n";

            esd = s;
        }

        private string HandlePapyrus(Papyrus.Call call, int indent)
        {
            switch (call)
            {
                case Papyrus.ConditionalBranch celif:
                    return HandleElif(celif, indent);
                case Papyrus.Conditional cif:
                    return HandleIf(cif, indent);
                case Papyrus.Main cm:
                    return " ## INVALID BEGIN CALL ##";
                default:
                    return HandleCall(call, indent);
            }
        }

        private string HandleElif(Papyrus.ConditionalBranch call, int indent)
        {
            return $"{Indent(indent)}if False:\r\n{HandleScope(call.pass, indent+1)}{Indent(indent)}else:\r\n{HandleScope(call.fail, indent+1)}";
        }

        private string HandleIf(Papyrus.Conditional call, int indent)
        {
            return $"{Indent(indent)}if False:\r\n{HandleScope(call.pass, indent+1)}{Indent(indent)}else:\r\n{HandleScope(call.fail, indent+1)}";
        }

        private string HandleScope(Papyrus.Scope scope, int indent)
        {
            if(scope.calls.Count() <= 0) { return $"{Indent(indent)}pass\r\n"; } // empty scope becomes a 'pass' for now @TODO: temp

            string s = "";
            foreach(Papyrus.Call call in scope.calls)
            {
                s += $"{HandlePapyrus(call, indent)}";
            }
            return s;
        }

        private string HandleCall(Papyrus.Call call, int indent)
        {
            string s;

            switch(call.type)
            {
                case Call.Type.Journal:
                    Script.Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, call.parameters[0]); // look for flag, if not found make one
                    if (jvar == null) { jvar = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Journal, call.parameters[0]); }
                    s = $"SetEventFlagValue({jvar.id}, {jvar.Bits()}, {int.Parse(call.parameters[1])})";
                    break;

                case Call.Type.AddTopic:
                    Script.Flag tvar = scriptManager.GetFlag(Script.Flag.Designation.TopicEnabled, call.parameters[0]);
                    s = $"SetEventFlag({tvar.id}, FlagState.On)";
                    break;

                case Call.Type.Return:
                    s = $"continue";
                    break;

                case Call.Type.Activate:
                    s = $"pass";
                    break;

                default:
                    s = "pass";
                    break;
            }

            return $"{Indent(indent)}## {(call.target!=null?$"{call.target}->":"")}{call.type} {string.Join(" ", call.parameters)}\r\n{Indent(indent)}{s}\r\n";
        }

        private string Indent(int indent)
        {
            return new String(' ', indent * 4);
        }
    }
}
