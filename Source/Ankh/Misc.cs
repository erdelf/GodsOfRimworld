using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ankh
{
    // ReSharper disable once InconsistentNaming
    public class ITab_Wraths : ITab
    {
        private static readonly Vector2 smallSize = new Vector2(x: 420f, y: 100f);
        private static readonly Vector2 fullSize = new Vector2(x: 420f, y: 480f);

        private static int State => BehaviourInterpreter.instance.instanceVariableHolder.altarState;

        public ITab_Wraths()
        {
            this.labelKey = "TabWraths";
            this.tutorTag = "Wraths";
        }

        public override void OnOpen()
        {
            base.OnOpen();
            this.size = State <= 1 ? smallSize : fullSize;
            List<KeyValuePair<int, List<string[]>>> scheduleList = BehaviourInterpreter.instance.instanceVariableHolder.scheduler.Where(predicate: kvp => kvp.Key < Find.TickManager.TicksGame + GenDate.TicksPerDay).ToList().ListFullCopyOrNull();
            scheduleList.ForEach(action: kvp => kvp.Value.RemoveAll(match: s => !s[0].Equals(value: "callTheGods") || !s[2].Equals(value: "False")));
            scheduleList.RemoveAll(match: kvp => kvp.Value.NullOrEmpty());
            float longitude = Find.WorldGrid.LongLatOf(tileID: this.SelThing.Map.Tile).x;

            this.relevantSchedules = scheduleList.Select(selector: kvp =>
            {
                float hour = GenDate.HourFloat(absTicks: GenDate.TickGameToAbs(gameTick: kvp.Key), longitude: longitude);
                int hourInt = Mathf.FloorToInt(f: hour);
                return new KeyValuePair<string, List<string[]>>(key: (hourInt%24).ToString(format: "D2") + ":" + Mathf.RoundToInt(f: (hour - hourInt) * 60f).ToString(format: "D2"), value: kvp.Value);
            }).ToList();
        }

        private Vector2 scrollPosition = Vector2.zero;
        private List<KeyValuePair<string, List<string[]>>> relevantSchedules;

        protected override void FillTab()
        {
            Rect rect = new Rect(x: 0f, y: 0f, width: this.size.x, height: this.size.y).ContractedBy(margin: 10f);
            if (State <= 1)
            {
                Widgets.Label(rect: rect, label: State == 0 ? "The gods offer you a way to delay their wrath, but they require a sacrifice.\nSend one of your colonists to serve the Gods." :
                    "The gods enjoy their new servant. New wraths will take a day before arriving.\nThey have a new offer for you. Want to see the wraths before they arrive, mortal?");
            } else
            {
                GUI.BeginGroup(position: rect);
                Widgets.Label(rect: rect.TopPart(pct: 0.10f), label: "The gods are pleased for now. Enjoy their gifts.\nWraths: " + this.relevantSchedules.SelectMany(selector: kvp => kvp.Value).Count());
                Widgets.BeginScrollView(outRect: rect.BottomPart(pct: 0.90f).ContractedBy(margin: 20f), scrollPosition: ref this.scrollPosition, viewRect: new Rect(x: rect.x, y: rect.y, width: rect.width/3*2, height: this.relevantSchedules.SelectMany(selector: kvp => kvp.Value).Count() * 55f));
                int num = 45;
                this.relevantSchedules.ForEach(action: kvp =>
                {
                    kvp.Value.ForEach(action: s =>
                    {
                        Widgets.Label(rect: new Rect(x: rect.x, y: num + 5, width: rect.width - 16f, height: 40f), label: s[1] + "\t" + kvp.Key.ToString());
                        num += 45;
                    });
                });
                Widgets.EndScrollView();
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
        public override bool TryMakePreToilReservations(bool errorOnFailed) => this.pawn.Reserve(target: this.job.targetA, job: this.job);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(ind: TargetIndex.A, maxPawns: 1, stackCount: 1);
            yield return Toils_Goto.GotoThing(ind: TargetIndex.A, peMode: PathEndMode.InteractionCell);
            Toil toil = Toils_General.Wait(ticks: 500);
            toil.WithProgressBarToilDelay(ind: TargetIndex.B);
            toil.AddFinishAction(newAct: () => MoteMaker.ThrowLightningGlow(loc: this.pawn.Position.ToVector3(), map: this.Map, size: 50f));
            toil.AddFinishAction(newAct: () => BehaviourInterpreter.instance.WaitAndExecute(action: () => this.pawn.DeSpawn()));
            toil.AddFinishAction(newAct: () => this.Map.weatherManager.eventHandler.AddEvent(newEvent: new WeatherEvent_LightningStrike(map: this.Map, forcedStrikeLoc: this.TargetLocA)));
            toil.AddFinishAction(newAct: () => BehaviourInterpreter.instance.instanceVariableHolder.altarState++);
            yield return toil;
        }
    }
}
