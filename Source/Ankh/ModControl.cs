using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Ankh
{
    public class GodSettings : ModSettings
    {
        public Dictionary<StringWrapper, bool> enabledGods;
    }

    internal class GodsOfRimworld : Mod
    {
        private Vector2 scrollPosition;

        public GodsOfRimworld(ModContentPack content) : base(content: content)
        {
        }

        public override string SettingsCategory() => "Enabled Gods";


        public override void DoSettingsWindowContents(Rect inRect)
        {
            GodSettings settings = this.GetSettings<GodSettings>();
            GUI.BeginGroup(position: inRect);
            Widgets.BeginScrollView(outRect: inRect, scrollPosition: ref this.scrollPosition, viewRect: new Rect(x: 0f, y: 0f, width: inRect.width, height: settings.enabledGods.Count * 55f));

            float num = 6f;
            Text.Font = GameFont.Medium;
            for (int i = 0; i < settings.enabledGods.Count; i++)
            {
                StringWrapper godsName = settings.enabledGods.Keys.ToList()[index: i];
                bool enabled = settings.enabledGods[key: godsName];
                Widgets.Label(rect: new Rect(x: 0f, y: num+5, width: inRect.width-16f, height: 40f), label: godsName.value.Replace(oldValue: "_", newValue: " ").CapitalizeFirst());
                Widgets.Checkbox(x: inRect.width-64f, y: num + 6f, checkOn: ref enabled);
                settings.enabledGods[key: godsName] = enabled;
                Widgets.DrawLineHorizontal(x: 0, y: num, length: inRect.width);
                num += 50;
            }
            Widgets.DrawLineHorizontal(x: 0, y: num, length: inRect.width);

            Widgets.EndScrollView();
            GUI.EndGroup();
            Text.Font = GameFont.Small;
            if(Widgets.ButtonText(rect: new Rect(x: inRect.width - 160f, y: inRect.height + 40, width: 140f, height: 40f), label: "Deactivate All"))
                for (int i = 0; i < settings.enabledGods.Keys.Count; i++)
                    settings.enabledGods[key: settings.enabledGods.Keys.ToList()[index: i]] = false;

            BehaviourInterpreter.gods.Clear();
            BehaviourInterpreter.AddGods();

            foreach (StringWrapper s in settings.enabledGods.Keys)
                if (!settings.enabledGods[key: s])
                    BehaviourInterpreter.gods.Remove(key: s);

            settings.Write();
        }
    }
}
