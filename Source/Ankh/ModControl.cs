using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Ankh
{
    public class GodSettings : ModSettings
    {
        public Dictionary<StringWrapper, bool> enabledGods;

        public override void ExposeData() => base.ExposeData();
    }

    class GodsOfRimworld : Mod
    {

        Vector2 scrollPosition;

        public GodsOfRimworld(ModContentPack content) : base(content)
        {
        }

        public override string SettingsCategory() => "Enabled Gods";


        public override void DoSettingsWindowContents(Rect inRect)
        {
            GUI.BeginGroup(inRect);
            Widgets.BeginScrollView(inRect, ref this.scrollPosition, new Rect(0f, 0f, inRect.width, GetSettings<GodSettings>().enabledGods.Count * 55f), true);

            float num = 6f;
            Text.Font = GameFont.Medium;
            for (int i = 0; i < GetSettings<GodSettings>().enabledGods.Count; i++)
            {
                StringWrapper godsName = GetSettings<GodSettings>().enabledGods.Keys.ToList()[i];
                bool enabled = GetSettings<GodSettings>().enabledGods[godsName];
                Widgets.Label(new Rect(0f, num+5, inRect.width-16f, 40f), godsName.value.Replace("_", " ").CapitalizeFirst());
                Widgets.Checkbox(inRect.width-64f, num + 6f, ref enabled);
                GetSettings<GodSettings>().enabledGods[godsName] = enabled;
                Widgets.DrawLineHorizontal(0, num, inRect.width);
                num += 50;
            }
            Widgets.DrawLineHorizontal(0, num, inRect.width);

            Widgets.EndScrollView();
            GUI.EndGroup();
            Text.Font = GameFont.Small;
            if(Widgets.ButtonText(new Rect(inRect.width - 160f, inRect.height + 40, 140f, 40f), "Deactivate All"))
                for (int i = 0; i < GetSettings<GodSettings>().enabledGods.Keys.Count; i++)
                    GetSettings<GodSettings>().enabledGods[GetSettings<GodSettings>().enabledGods.Keys.ToList()[i]] = false;

            BehaviourInterpreter.gods.Clear();
            BehaviourInterpreter.AddGods();

            foreach (StringWrapper s in GetSettings<GodSettings>().enabledGods.Keys)
                if (!GetSettings<GodSettings>().enabledGods[s])
                    BehaviourInterpreter.gods.Remove(s);

            GetSettings<GodSettings>().Write();
        }
    }
}
