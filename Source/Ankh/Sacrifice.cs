using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Ankh
{
    public class Sacrifice
    {
        public class Zone_Sacrifice : Zone_Stockpile
        {
            ThingCount[] sacrificesWanted;
            string god;

            public Zone_Sacrifice(ZoneManager zoneManager, string god) : base(StorageSettingsPreset.DefaultStockpile, zoneManager)
            {
                this.god = god;

                switch(god)
                {
                    case "zap":
                        this.sacrificesWanted = new ThingCount[] { new ThingCount(ThingDef.Named("Steel"), 50)};
                        break;
                }

                new GameObject("sacrificeZoneTracker" + Rand.Range(1,500)).AddComponent<SacrificeZoneTracker>().SetZone(this);
            }

            

            public override IEnumerable<Gizmo> GetGizmos()
            {
                yield return new Command_Action
                {
                    icon = ContentFinder<Texture2D>.Get("UI/Buttons/Delete", true),
                    defaultLabel = "CommandDeleteZoneLabel".Translate(),
                    defaultDesc = "CommandDeleteZoneDesc".Translate(),
                    action = new Action(this.Delete),
                    hotKey = KeyBindingDefOf.Misc3
                };
            }

            public override string GetInspectString() => string.Join(", ", this.sacrificesWanted.Select(tc => tc.ThingDef.label + ": " + tc.Count).ToArray());
        }

        public class SacrificeZoneTracker : MonoBehaviour
        {
            Zone_Sacrifice zone;

            public void SetZone(Zone_Sacrifice zone) => this.zone = zone;
        }
    }
}