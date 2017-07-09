using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ankh
{
    public class ITab_Wraths : ITab
    {
        private static readonly Vector2 SmallSize = new Vector2(420f, 100f);
        private static readonly Vector2 FullSize = new Vector2(420f, 480f);

        private static int State => BehaviourInterpreter._instance.instanceVariableHolder.altarState;

        public ITab_Wraths()
        {
            this.labelKey = "TabWraths";
            this.tutorTag = "Wraths";
        }

        public override void OnOpen()
        {
            base.OnOpen();
            this.size = State <= 1 ? SmallSize : FullSize;
            List<KeyValuePair<int, List<string[]>>> scheduleList;
            scheduleList = BehaviourInterpreter._instance.instanceVariableHolder.scheduler.Where(kvp => kvp.Key < Find.TickManager.TicksGame + GenDate.TicksPerDay).ToList().ListFullCopyOrNull();
            scheduleList.ForEach(kvp => kvp.Value.RemoveAll(s => !s[0].Equals("callTheGods") || !s[2].Equals("False")));
            scheduleList.RemoveAll(kvp => kvp.Value.NullOrEmpty());
            float longitude = Find.WorldGrid.LongLatOf(this.SelThing.Map.Tile).x;

            this.relevantSchedules = scheduleList.Select(kvp =>
            {
                float hour = GenDate.HourFloat(GenDate.TickGameToAbs(kvp.Key), longitude);
                int hourInt = Mathf.FloorToInt(hour);
                return new KeyValuePair<string, List<string[]>>((hourInt%24).ToString("##") + ":" + Mathf.RoundToInt((hour - hourInt) * 60f).ToString("##"), kvp.Value);
            }).ToList();
        }

        Vector2 scrollPosition = Vector2.zero;

        List<KeyValuePair<string, List<string[]>>> relevantSchedules;

        protected override void FillTab()
        {
            Rect rect = new Rect(0f, 0f, this.size.x, this.size.y).ContractedBy(10f);
            if (State <= 1)
            {
                Widgets.Label(rect, State == 0 ? "The gods offer you a way to delay their wrath, but they require a sacrifice.\nSend one of your colonists to serve the Gods." :
                    "The gods enjoy their new servant. New wraths will take a day before arriving.\nThey have a new offer for you. Want to see the wraths before they arrive, mortal?");
            } else
            {
                GUI.BeginGroup(rect);
                Widgets.Label(rect.TopPart(20f), "The gods are pleased for now. Enjoy their gifts.\nWraths: " + this.relevantSchedules.Count);
                //Widgets.BeginScrollView(rect.BottomPart(80f), ref this.scrollPosition, new Rect(rect.x, rect.y, rect.width, this.relevantSchedules.Count * 55f));
                int num = 45;
                this.relevantSchedules.ForEach(kvp =>
                {
                    kvp.Value.ForEach(s =>
                    {
                        Widgets.Label(new Rect(rect.x, num + 5, rect.width - 16f, 40f), s[1] + "\t" + kvp.Key.ToString());
                        num += 45;
                    });
                });
                //Widgets.EndScrollView();
                GUI.EndGroup();
            }
        }

        protected override void CloseTab()
        {
            base.CloseTab();
            this.relevantSchedules = null;
        }
    }

    public class JobDriver_Sacrifice : JobDriver
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A, 1, 1);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil toil = Toils_General.Wait(500);
            toil.WithProgressBarToilDelay(TargetIndex.B);
            toil.AddFinishAction(() => MoteMaker.ThrowLightningGlow(this.pawn.Position.ToVector3(), this.Map, 50f));
            toil.AddFinishAction(() => BehaviourInterpreter._instance.WaitAndExecute(() => this.pawn.DeSpawn()));
            toil.AddFinishAction(() => this.Map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(this.Map, this.TargetLocA)));
            toil.AddFinishAction(() => BehaviourInterpreter._instance.instanceVariableHolder.altarState++);
            yield return toil;
        }
    }
}
