using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Ankh
{
    using System.Globalization;
    using JetBrains.Annotations;

    [StaticConstructorOnStartup]
    public class BehaviourInterpreter : MonoBehaviour
    {
        private string logPath;
        
        private XDocument configFile;
        
        private const string CONFIG_PATH = "AnkhConfig.xml";
        
        private static readonly string configFullPath;

        private List<string> log;

        public static StaticVariables staticVariables;

        public static Dictionary<string, Action<bool, bool>> gods;

        private static List<Action<string, bool>> wrathActions;

        private static List<Action> congratsActions;

        public InstanceVariables instanceVariableHolder;

        static BehaviourInterpreter()
        {
            GameObject initializer = new GameObject(name: "Ankh_Interpreter");

            configFullPath = Path.Combine(path1: LoadedModManager.RunningMods.First(predicate: mcp => mcp.assemblies.loadedAssemblies.Contains(item: typeof(BehaviourInterpreter).Assembly)).RootDir, path2: "Assemblies");
            
            {
                if (File.Exists(path: InstanceVariablePath))
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(filename: InstanceVariablePath);
                    string xmlString = xmlDocument.OuterXml;

                    using (StringReader read = new StringReader(s: xmlString))
                    {
                        XmlSerializer instanceSerializer = new XmlSerializer(type: typeof(StaticVariables));

                        using (XmlReader reader = new XmlTextReader(input: read)) staticVariables = (StaticVariables)instanceSerializer.Deserialize(xmlReader: reader);
                    }
                }
            }
            instance = initializer.AddComponent<BehaviourInterpreter>();
            
            DontDestroyOnLoad(target: initializer);
        }

        public static string ConfigFullPath => Path.Combine(path1: configFullPath, path2: CONFIG_PATH);

        public static string InstanceVariablePath => Path.Combine(path1: configFullPath, path2: "vars.erdelf");

        public static BehaviourInterpreter instance;

        [UsedImplicitly]
        private void Awake()
        {
            PrepareDefs();
            this.InitializeStaticVariables();
        }

        private void InitializeStaticVariables()
        {
            AddGods();

            GodsOfRimworld modHandler = LoadedModManager.GetMod<GodsOfRimworld>();

            modHandler.GetSettings<GodSettings>().enabledGods = new Dictionary<StringWrapper, bool>();

            foreach (string s in gods.Keys)
                if (!modHandler.GetSettings<GodSettings>().enabledGods.ContainsKey(key: s))
                    modHandler.GetSettings<GodSettings>().enabledGods.Add(key: s, value: true);

            this.AddCongrats();
            this.AddDeadWraths();

            if (this.log == null)
                this.log = new List<string>();


            this.configFile = XDocument.Load(uri: ConfigFullPath);

            

            if(staticVariables.instanceVariables.NullOrEmpty())
                staticVariables.instanceVariables = new List<InstanceVariables>();

            foreach (XElement b in this.configFile.Descendants(name: "settings"))
            {
                foreach (XElement x in b.Descendants())
                {
                    if (x.Attributes().First() == null) continue;
                    switch (x.Attributes().First().Value)
                    {
                        case "logPath":
                            this.logPath = x.DescendantNodes().First().ToString().Trim();
                            break;
                    }
                }
            }

            this.UpdateLog();
        }

        private void InitializeVariables()
        {
            try
            {
                InstanceVariables instanceVariable = staticVariables.instanceVariables.FirstOrDefault(predicate: iv => iv.seed.Equals(obj: Current.Game.World.info.Seed));
                if (instanceVariable.seed != 0)
                {
                    this.instanceVariableHolder = instanceVariable;
                }
                else
                {
                    instanceVariable.seed = Current.Game.World.info.Seed;
                    instanceVariable.moodTracker = new Dictionary<string, float>();
                    instanceVariable.scheduler = new Dictionary<int, List<string[]>>();
                    instanceVariable.deadWraths = new List<string>();
                    instanceVariable.altarState = 0;

                    this.instanceVariableHolder = instanceVariable;
                    staticVariables.instanceVariables.Add(item: instanceVariable);
                }
            }
            catch (Exception ex) { Debug.Log(message: ex.Message + "\n" + ex.StackTrace); }
        }

        [UsedImplicitly]
        private void FixedUpdate()
        {
            try
            {
                ((Action)(() =>
                {
                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        if (Current.Game?.World?.info?.Seed != this.instanceVariableHolder.seed) this.InitializeVariables();


                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerHour / 8) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 8)))
                        {
                            IEnumerable<int> keyList = this.instanceVariableHolder.scheduler.Keys.Where(predicate: i => i < Find.TickManager.TicksGame).ToList();
                            List<string[]> actionList = keyList.SelectMany(selector: i => this.instanceVariableHolder.scheduler[key: i]).ToList();
                            keyList.ToList().ForEach(action: i => this.instanceVariableHolder.scheduler.Remove(key: i));

                            actionList.ForEach(action: a =>
                            {
                                try
                                {
                                    Log.Message(text: string.Join(separator: " | ", value: a) + actionList.Count);
                                    this.ExecuteScheduledCommand(parameters: a);
                                    actionList.Where(predicate: l => l != a && l[0].Equals(value: a[0]) && (l.Length == 1 || l[1].Equals(value: a[1]) && l[2].Equals(value: a[2]))).ToList().ForEach(action: l =>
                                    {
                                        Log.Message(text: "Re-adding to avoid spamming");
                                        this.AddToScheduler(ticks: GenDate.TicksPerHour / 2 - 1, parameters: l);
                                        actionList.Remove(item: l);
                                    });
                                }
                                catch (Exception e)
                                {
                                    Debug.Log(message: e.Message + "\n" + e.StackTrace);
                                    this.AddToScheduler(ticks: 10, parameters: a);
                                }
                            });

                            Find.ColonistBar.GetColonistsInOrder().ForEach(action: p =>
                            {
                                if (p.story.traits.HasTrait(tDef: AnkhDefOf.coneOfShame) && !p.health.hediffSet.HasHediff(def: AnkhDefOf.coneOfShameHediff))
                                    p.health.hediffSet.AddDirect(hediff: HediffMaker.MakeHediff(def: AnkhDefOf.coneOfShameHediff, pawn: p));
                                if (p.story.traits.HasTrait(tDef: AnkhDefOf.fiveKnuckleShuffle) && !p.health.hediffSet.HasHediff(def: AnkhDefOf.fiveKnuckleShuffleHediff))
                                    p.health.hediffSet.AddDirect(hediff: HediffMaker.MakeHediff(def: AnkhDefOf.fiveKnuckleShuffleHediff, pawn: p));
                            });

                            if (Find.TickManager.TicksGame / (GenDate.TicksPerHour / 2) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerHour / 2))
                            {
                                List<string> curLog = this.UpdateLog();
                                if (!this.log.NullOrEmpty())
                                    curLog.ForEach(
                                        action: delegate (string s)
                                        {
                                            string[] split = s.Split(' ');
                                            Log.Message(text: s);
                                            if (split.Length == 5)
                                            {
                                                bool favor = split[1].ToLower().Equals(value: "favor");
                                                if (int.TryParse(s: split[3], result: out int points) && int.TryParse(s: split[4], result: out int cost))
                                                    if (points >= cost || split[0].EqualsIgnoreCase(B: "itspladd") || split[0].EqualsIgnoreCase(B: "erdelf") || (split[0].EqualsIgnoreCase(B: "serphentalis") && points >= cost / 2))
                                                    {
                                                        this.AddToScheduler(favor ? 1 : Find.TickManager.TicksGame > GenDate.TicksPerDay * 3 ? (this.instanceVariableHolder.altarState == 0 ? 1 : GenDate.TicksPerDay) : GenDate.TicksPerDay * 3 - Find.TickManager.TicksGame, "callTheGods", split[2].ToLower(), favor.ToString(), true.ToString());
                                                        Messages.Message(text: (favor ? "favor" : "wrath") + " received", def: favor ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent);
                                                    }
                                            }
                                        }
                                    );

                                if (Find.TickManager.TicksGame / GenDate.TicksPerHour > this.instanceVariableHolder.lastTickTick / GenDate.TicksPerHour)
                                {
                                    Find.ColonistBar.GetColonistsInOrder().Where(predicate: p => !p.Dead && p.story.traits.HasTrait(tDef: AnkhDefOf.teaAndScones)).ToList().ForEach(action: p =>
                                    {
                                        int i = 2;

                                        List<Hediff_Injury> hediffs = p.health.hediffSet.GetHediffs<Hediff_Injury>().Where(predicate: hi => !hi.IsPermanent()).ToList().ListFullCopy();
                                        while (i > 0 && hediffs.Count > 0)
                                        {
                                            Hediff_Injury hediff = hediffs.First();
                                            float val = Math.Min(val1: hediff.Severity, val2: i);
                                            i -= Mathf.RoundToInt(f: val);
                                            hediff.Heal(amount: val);
                                            hediffs.Remove(item: hediff);
                                        }
                                    });

                                    if (Find.TickManager.TicksGame / (GenDate.TicksPerDay) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerDay))
                                    {
                                        Log.Message(text: "Day: " + GenDate.DaysPassed);

                                        List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder();

                                        pawns.Where(predicate: p => !p.Dead).ToList().ForEach(action: delegate (Pawn p)
                                        {
                                            if (!this.instanceVariableHolder.moodTracker.ContainsKey(key: p.Name.ToStringShort))
                                                this.instanceVariableHolder.moodTracker.Add(key: p.Name.ToStringShort, value: 0f);
                                            this.instanceVariableHolder.moodTracker[key: p.Name.ToStringShort] = p.needs.mood.CurInstantLevelPercentage;
                                        });

                                        pawns.Where(predicate: p => p.Dead && !this.instanceVariableHolder.deadWraths.Contains(item: p.Name.ToStringShort)).ToList().ForEach(action: p =>
                                        {
                                            this.instanceVariableHolder.deadWraths.Add(item: p.Name.ToStringShort);
                                            if (Rand.Value > 0.3)
                                            {
                                                int scheduledFor = Mathf.RoundToInt(f: UnityEngine.Random.Range(min: 0.0f, max: GenDate.DaysPerSeason) * GenDate.TicksPerDay);
                                                this.AddToScheduler(scheduledFor, "wrathCall", p.Name.ToStringShort, p.gender.ToString());
                                                Log.Message(text: "Scheduled " + p.Name.ToStringShort + "s wrath. Will happen in " + scheduledFor.TicksToDays().ToString(CultureInfo.InvariantCulture) + " days");
                                            }
                                        });

                                        if ((Find.TickManager.TicksGame / (GenDate.TicksPerTwelfth) > this.instanceVariableHolder.lastTickTick / (GenDate.TicksPerTwelfth)) && Find.TickManager.TicksGame > 0)
                                            if (Find.ColonistBar.GetColonistsInOrder().Any(predicate: p => !p.Dead))
                                                this.AddToScheduler(5, "survivalReward");
                                    }
                                }
                            }
                            this.instanceVariableHolder.lastTickTick = Find.TickManager.TicksGame;
                        }
                        
                        if (staticVariables.instaResearch > 0)
                            if (Find.ResearchManager.currentProj != null)
                            {
                                Find.ResearchManager.ResearchPerformed(amount: 400f / 0.007f, researcher: null);
                                staticVariables.instaResearch--;
                            }
                    }
                }))();

            }
            catch (Exception e) { Debug.Log(message: e.Message + "\n" + e.StackTrace); }
        }

        private void ExecuteScheduledCommand(params string[] parameters)
        {
            switch (parameters[0])
            {
                case "callTheGods":
                    if (gods.TryGetValue(key: parameters[1], value: out Action<bool, bool> action))
                        action.Invoke(arg1: bool.Parse(value: parameters[2]), arg2: bool.Parse(value: parameters[3]));
                    else
                        Log.Message(text: parameters[1] + " is not in the active god pool");
                    break;
                case "survivalReward":
                    Find.LetterStack.ReceiveLetter(label: "survival reward", text: "You survived a month on this rimworld, the gods are pleased", textLetterDef: LetterDefOf.PositiveEvent);
                    congratsActions.RandomElement().Invoke();
                    break;
                case "wrathCall":
                    CustomIncidentClasses.WeatherEvent_WrathBombing.erdelf = parameters[1].EqualsIgnoreCase(B: "erdelf");
                    wrathActions.RandomElement().Invoke(arg1: parameters[1], arg2: (Gender)Enum.Parse(enumType: typeof(Gender), value: parameters[2], ignoreCase: true) == Gender.Male);
                    this.instanceVariableHolder.moodTracker.Remove(key: parameters[1]);
                    break;
            }
        }

        public void AddToScheduler(int ticks, params string[] parameters)
        {
            Log.Message(text: "Scheduled in " + ticks.TicksToDays() + " days: " + string.Join(separator: " ", value: parameters));

            ticks += Find.TickManager.TicksGame;
            if (!this.instanceVariableHolder.scheduler.ContainsKey(key: ticks))
                this.instanceVariableHolder.scheduler.Add(key: ticks, value: new List<string[]>());
            this.instanceVariableHolder.scheduler[key: ticks].Add(item: parameters);
        }

        [UsedImplicitly]
        private void OnDestroy()
        {
            XmlDocument xmlDocument = new XmlDocument();
            XmlSerializer serializer = new XmlSerializer(type: typeof(StaticVariables));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream: stream, o: staticVariables);
                stream.Position = 0;
                xmlDocument.Load(inStream: stream);
                xmlDocument.Save(filename: InstanceVariablePath);
            }
        }

        public List<string> UpdateLog()
        {
            List<string> curLog = ReadFileAndFetchStrings(file: this.logPath);

            int dels = 0;
            int count = curLog.Count;
            for (int i = 0; i < this.log.Count && i < count; i++)
            {
                if (this.log[index: i] == curLog[index: i - dels])
                {
                    curLog.RemoveAt(index: i - dels);
                    dels++;
                }
            }
            foreach (string s in curLog)
                this.log.Add(item: s);
            return curLog;
        }

        private void AddCongrats() => congratsActions = new List<Action>
            {
                () =>
                {
                    for (int i = 0; i < 15; i++) this.AddToScheduler(250 +i *50, "callTheGods", gods.Keys.RandomElement(), true.ToString(), true.ToString());
                    AnkhDefOf.miracleHeal.Worker.TryExecute(parms: null);
                }
            };

        private void AddDeadWraths()
        {
            wrathActions = new List<Action<string, bool>>();

            void RaidDelegate(string pawnName, bool gender)
            {
                if (!this.instanceVariableHolder.moodTracker.ContainsKey(key: pawnName)) this.instanceVariableHolder.moodTracker.Add(key: pawnName, value: 75f);

                Map map = Find.AnyPlayerHomeMap;
                IncidentParms parms = new IncidentParms()
                {
                    target          = map,
                    points          = 3.25f * Mathf.Pow(f: 1.1f, p: 0.75f * (-(0.4f * this.instanceVariableHolder.moodTracker[key: pawnName]) + 60f)),
                    faction         = Find.FactionManager.FirstFactionOfDef(facDef: FactionDefOf.AncientsHostile),
                    raidStrategy    = RaidStrategyDefOf.ImmediateAttack,
                    raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                    podOpenDelay    = 50,
                    spawnCenter = map.listerBuildings.ColonistsHaveBuildingWithPowerOn(def: ThingDefOf.OrbitalTradeBeacon) ?
                                      DropCellFinder.TradeDropSpot(map: map) :
                                      RCellFinder.TryFindRandomSpotJustOutsideColony(originCell: map.IsPlayerHome ? map.mapPawns.FreeColonists.RandomElement().Position : CellFinder.RandomCell(map: map), map: map, result: out IntVec3 spawnPoint) ?
                                          spawnPoint :
                                          CellFinder.RandomCell(map: map),
                    generateFightersOnly    = true,
                    forced                  = true,
                    raidNeverFleeIndividual = true
                };
                List<Pawn> pawns = new PawnGroupMaker() {kindDef = new PawnGroupKindDef() {workerClass = typeof(CustomIncidentClasses.PawnGroupKindWorker_Wrath)},}.GeneratePawns(parms: new PawnGroupMakerParms()
                    {
                        tile                 = ((Map) parms.target).Tile,
                        faction              = parms.faction,
                        points               = parms.points,
                        generateFightersOnly = true,
                        raidStrategy         = parms.raidStrategy
                    })
                   .ToList();

                DropPodUtility.DropThingsNear(dropCenter: parms.spawnCenter, map: map, things: pawns.Cast<Thing>(), openDelay: parms.podOpenDelay, canInstaDropDuringInit: false, leaveSlag: true);
                parms.raidStrategy.Worker.MakeLords(parms: parms, pawns: pawns);
                AvoidGridMaker.RegenerateAvoidGridsFor(faction: parms.faction, map: map);

                SendWrathLetter(name: pawnName, possessive: gender, info: new GlobalTargetInfo(cell: parms.spawnCenter, map: (Map) parms.target));
            }

            wrathActions.Add(item: RaidDelegate);
            wrathActions.Add(item: RaidDelegate);
            wrathActions.Add(item: (s, b) =>
            {
                SendWrathLetter(name: s, possessive: b, info: GlobalTargetInfo.Invalid);
                gods.Values.RandomElement().Invoke(arg1: false, arg2: true);
            });
            wrathActions.Add(item: (s, b) =>
            {
                SendWrathLetter(name: s, possessive: b, info: GlobalTargetInfo.Invalid);
                gods.Values.RandomElement().Invoke(arg1: false, arg2: true);
            });

            wrathActions.Add(item: (s, b) =>
            {
                Find.AnyPlayerHomeMap.gameConditionManager.RegisterCondition(cond: GameConditionMaker.MakeCondition(def: AnkhDefOf.wrathCondition, duration: GenDate.TicksPerDay * 1));
                SendWrathLetter(name: s, possessive: b, info: GlobalTargetInfo.Invalid);
            });
        }

        public static void AddGods() => gods = new Dictionary<string, Action<bool, bool>>
        {
                {
                    "zap",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.zapCount++;

                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: AnkhDefOf.zapActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "zap's favor",
                                    text: "The god of lightning shows mercy on your colony. He commands the fire in the sky to obey you for once", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }
                        else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "Serpenthalis"));


                            Pawn p = pawns.Where((pawn => pawn.Map.roofGrid.RoofAt(c: pawn.Position) != RoofDefOf.RoofRockThick)).RandomElement();

                            p.Map.weatherManager.eventHandler.AddEvent(newEvent: new WeatherEvent_LightningStrike(map: p.Map, forcedStrikeLoc: p.Position));
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "zap's wrath",
                                    text: "The god of lightning is angry at your colony. He commands the fire in the sky to strike down on " + p.Name.ToStringShort, textLetterDef: LetterDefOf.ThreatBig, lookTargets: p);
                        }
                    }
                },
                {
                    "sparto",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            
                            if (!map.areaManager.Home.ActiveCells.Where(predicate: i => i.Standable(map: map)).TryRandomElement(result: out IntVec3 position))
                                throw new Exception(message: "no home cell");

                            if(!DefDatabase<ThingDef>.AllDefsListForReading.Where(predicate: def => def.equipmentType == EquipmentType.Primary && !(def.weaponTags?.TrueForAll(match: s => s.Contains(value: "Mechanoid") || s.Contains(value: "Turret") || s.Contains(value: "Artillery")) ?? false)).ToList().TryRandomElement(result: out ThingDef tDef))
                                throw new Exception(message: "no weapon");

                            Thing thing = ThingMaker.MakeThing(def: tDef, stuff: tDef.MadeFromStuff ? GenStuff.RandomStuffFor(td: tDef) : null);
                            CompQuality qual = thing.TryGetComp<CompQuality>();
                            qual?.SetQuality(q: QualityCategory.Normal, source: ArtGenerationContext.Colony);

                            GenSpawn.Spawn(newThing: thing, loc: position, map: map);
                            Vector3 vec = position.ToVector3();
                            MoteMaker.ThrowSmoke(loc: vec, map: map, size: 5);
                            MoteMaker.ThrowMetaPuff(loc: vec, map: map);

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "sparto's favor",
                                    text: "The god of war shows mercy on your colony. A piece of equipment below his godly standards got thrown on your colony", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: thing);
                        }
                        else
                        {
                            IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(incCat: IncidentCategoryDefOf.ThreatBig, target: Find.AnyPlayerHomeMap);

                            Letter letterobj = LetterMaker.MakeLetter(label: "sparto's wrath",
                                    text: "The god of war is angry at your colony. She commands the locals of this world to attack", def: LetterDefOf.ThreatBig);

                            CustomIncidentClasses.CallEnemyRaid(parms: incidentParms, letter: letter ? letterobj : null);

                        }
                    }
                },
                {
                    "peg",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.pegCount++;

                            ThingDef peg = AnkhDefOf.pegActivator;
                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: peg)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "peg's favor",
                                    text: "The god of pirates shows mercy on your colony. He commands the fires of this world to defend you", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }
                        else
                        {
                            for(int i=0; i<3; i++)
                            {
                                Map map = Find.AnyPlayerHomeMap;
                                if (!map.areaManager.Home.ActiveCells.Where(predicate: iv => iv.Standable(map: map)).TryRandomElement( result: out IntVec3 position))
                                    throw new Exception();
                                GenExplosion.DoExplosion(center: position, map: map, radius: 3.9f, damType: DamageDefOf.Bomb, instigator: null);
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "peg's wrath",
                                    text: "The god of pirates is angry at your colony. He commands the fires to strike down on your colony", textLetterDef: LetterDefOf.ThreatBig);
                        }
                    }
                },
                {
                    "repo",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.repoCount++;
                            ThingDef peg = AnkhDefOf.repoActivator;
                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "repo's favor",
                                    text: "The god of organs shows mercy on your colony", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }
                        else
                        {
                            List<BodyPartRecord> parts = new List<BodyPartRecord>();
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "Serpenthalis"));

                            Predicate<BodyPartRecord> bodyPartRecord = delegate (BodyPartRecord x)
                            {
                                if (!(!x.def.canSuggestAmputation || x.depth == BodyPartDepth.Inside || x.def.conceptual))
                                    return true;
                                return false;
                            };

                            if (pawns.Count <= 0)
                                throw new Exception();

                            Pawn p = null;
                            while (parts.NullOrEmpty())
                            {
                                p = pawns.RandomElement();
                                parts = p.health.hediffSet.GetNotMissingParts().Where(predicate: x => bodyPartRecord.Invoke(obj: x) && !bodyPartRecord.Invoke(obj: x.parent)).ToList();
                                pawns.Remove(item: p);
                            }

                            if (parts.NullOrEmpty() || p == null)
                                throw new Exception();

                            BodyPartRecord part = parts.RandomElement();

                            p.health.AddHediff(def: HediffDefOf.MissingBodyPart, part: part);
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "repo's wrath",
                                        text: "The god of organs is angry at your colony. He commands the " + part.def.LabelCap.ToLower() + " of " + p.Name.ToStringShort + " to damage itself", textLetterDef: LetterDefOf.ThreatBig, lookTargets: p);
                        }
                    }
                },
                {
                    "bob",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.bobCount++;
                            ThingDef peg = AnkhDefOf.bobActivator;
                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "bob's favor",
                                    text: "The god of buildings shows mercy on your colony", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        }else
                        {
                            List<Building> walls = Find.AnyPlayerHomeMap.listerBuildings.AllBuildingsColonistOfDef(def: ThingDefOf.Wall).ToList();

                            if(walls.Count >= 2)
                                throw new Exception();

                            GlobalTargetInfo target = default(GlobalTargetInfo);
                            for(int i = 0; i<2; i++)
                            {
                                if(walls.TryRandomElement(result: out Building wall))
                                {
                                    target = new GlobalTargetInfo(cell: wall.Position, map: wall.Map);
                                    wall.Destroy();
                                    walls.Remove(item: wall);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "bob's wrath",
                                        text: "The god of buildings is angry at your colony.", textLetterDef: LetterDefOf.ThreatBig, lookTargets: target);
                        }
                    }
                },
                {
                    "rootsy",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.rootsyCount++;
                            ThingDef peg = AnkhDefOf.rootsyActivator;
                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: peg)).ToList();
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "rootsy's favor",
                                    text: "The god of plants shows mercy on your colony", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            List<Plant> list = new List<Plant>();
                            foreach (Map map in Find.Maps.Where(predicate: map => map.ParentFaction == Faction.OfPlayer))
                                list.AddRange(collection: map.listerThings.ThingsInGroup(group: ThingRequestGroup.FoodSource).Where(predicate: thing => thing is Plant plant && plant.def.plant.growDays <= 16f && plant.LifeStage == PlantLifeStage.Growing && plant.Map.zoneManager.ZoneAt(c: plant.Position) is Zone_Growing && !plant.def.defName.EqualsIgnoreCase(B: ThingDefOf.Plant_Grass.defName)).Cast<Plant>());
                            if (list.Count < 10)
                                throw new Exception();
                            list = list.InRandomOrder().Take(count: list.Count > 30 ? 30 : list.Count).ToList();

                            list.ForEach(action: plant =>
                            {
                                plant.CropBlighted();
                                list.Remove(item: plant);
                            });

                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "rootsy's wrath",
                                     text: "The god of flowers is angry at your colony. He commands the roots under your colony to blight", textLetterDef: LetterDefOf.NegativeEvent);

                        }
                    }
                },
                {
                    "fondle",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            if (!map.areaManager.Home.ActiveCells.Where(predicate: i => i.Standable(map: map)).TryRandomElement(result: out IntVec3 position))
                                throw new Exception();

                            DefDatabase<ThingDef>.AllDefsListForReading.Where(predicate: td => td.thingClass == typeof(Building_Art)).TryRandomElement(result: out ThingDef tDef);

                            Thing thing = ThingMaker.MakeThing(def: tDef, stuff: tDef.MadeFromStuff ? GenStuff.RandomStuffFor(td: tDef) : null);
                            CompQuality qual = thing.TryGetComp<CompQuality>();
                            qual?.SetQuality(q: Rand.Bool ?
                                                    QualityCategory.Normal : Rand.Bool ?
                                                        QualityCategory.Good : Rand.Bool ?
                                                            QualityCategory.Excellent : Rand.Bool ?
                                                                QualityCategory.Masterwork :
                                                                QualityCategory.Legendary, source: ArtGenerationContext.Colony);
                            thing.SetFactionDirect(newFaction: Faction.OfPlayer);
                            GenSpawn.Spawn(newThing: thing, loc: position, map: map);
                            Vector3 vec = position.ToVector3();
                            MoteMaker.ThrowSmoke(loc: vec, map: map, size: 5);
                            MoteMaker.ThrowMetaPuff(loc: vec, map: map);

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "fondle's favor",
                                    text: "The god of art shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: thing);
                        }else
                        {
                            Map map = Find.AnyPlayerHomeMap;

                            map.listerThings.AllThings.Where(predicate: t => t.TryGetComp<CompQuality>()?.Quality > QualityCategory.Awful && (t.Position.GetZone(map: map) is Zone_Stockpile || t.Faction == Faction.OfPlayer)).TryRandomElement(result: out Thing thing);

                            if(thing == null)
                                throw new Exception();

                            thing.TryGetComp<CompQuality>().SetQuality(q: QualityCategory.Awful, source: ArtGenerationContext.Colony);

                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "fondle's wrath",
                                     text: "The god of art is angry at your colony.", textLetterDef: LetterDefOf.NegativeEvent, lookTargets: thing);
                        }
                    }
                },
                {
                    "moo",
                    delegate(bool favor, bool letter)
                    {
                        Map map = Find.AnyPlayerHomeMap;
                        if (!DefDatabase<PawnKindDef>.AllDefsListForReading.Where(predicate: p => p.RaceProps.Animal).ToList().TryRandomElement(result: out PawnKindDef pawnKindDef)) throw new Exception();
                        if (!RCellFinder.TryFindRandomPawnEntryCell(result: out IntVec3 root, map: map, roadChance: 50f)) throw new Exception();
                        Pawn pawn = PawnGenerator.GeneratePawn(kindDef: pawnKindDef);

                        IntVec3 loc = CellFinder.RandomClosewalkCellNear(root: root, map: map, radius: 10);
                        GenSpawn.Spawn(newThing: pawn, loc: loc, map: map);

                        if(favor)
                        {
                            pawn.SetFaction(newFaction: Faction.OfPlayer);

                            TrainableUtility.TrainableDefsInListOrder.ForEach(action: td =>
                            {
                                if(pawn.training.CanAssignToTrain(td: td, visible: out bool _).Accepted)
                                    while(!pawn.training.HasLearned(td: td))
                                        pawn.training.Train(td: td, trainer: map.mapPawns.FreeColonists.RandomElement());
                            });

                            pawn.jobs.TryTakeOrderedJob(job: new Job(def: JobDefOf.GotoWander, targetA: map.areaManager.Home.ActiveCells.Where(predicate: iv => iv.Standable(map: map)).RandomElement()));
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "moo's favor",
                                     text: "The god of animals shows mercy on your colony. He commands his subordinate to be devoted to your colony", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: pawn);

                        } else
                        {
                            pawn.mindState.mentalStateHandler.TryStartMentalState(stateDef: MentalStateDefOf.ManhunterPermanent, reason: null, forceWake: true);
                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "moo's wrath",
                                    text: "The god of animals is angry at your colony. He commands his subordinate to teach you a lesson", textLetterDef: LetterDefOf.ThreatBig, lookTargets: pawn);

                        }
                    }
                },
                {
                    "clink",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            IncidentDefOf.OrbitalTraderArrival.Worker.TryExecute(parms: new IncidentParms() { target = Find.AnyPlayerHomeMap });
                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "clink's favor",
                                         text: "The god of commerce shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent);
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            List<Thing> silver = map.listerThings.ThingsOfDef(def: ThingDefOf.Silver);

                            if(silver.Sum(selector: t => t.stackCount) < 200)
                                throw new Exception();

                            int i = 200;

                            while(i > 0)
                            {
                                Thing piece = silver.First();
                                int x = Math.Min(val1: piece.stackCount, val2: i);
                                i -= x;
                                piece.stackCount -= x;

                                if(piece.stackCount == 0)
                                {
                                    silver.Remove(item: piece);
                                    piece.Destroy();
                                }
                            }
                            for(int c = 0; c<50; c++)
                            {
                                if (!map.areaManager.Home.ActiveCells.Where(predicate: l => l.Standable(map: map)).TryRandomElement(result: out IntVec3 position))
                                    throw new Exception();

                                Vector3 vec = position.ToVector3();
                                MoteMaker.ThrowSmoke(loc: vec, map: map, size: 5);
                                MoteMaker.ThrowMetaPuff(loc: vec, map: map);

                                GenSpawn.Spawn(newThing: ThingMaker.MakeThing(def: ThingDefOf.Steel), loc: position, map: map);
                            }
                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "clink's wrath",
                                    text: "The god of commerce is angry at your colony.", textLetterDef: LetterDefOf.NegativeEvent);
                        }
                    }
                },
                {
                    "fnargh",
                    delegate (bool favor, bool letter)
                    {
                         if (favor)
                         {
                             Pawn p = Find.ColonistBar?.GetColonistsInOrder()?.Where(predicate: x => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).RandomElement();
                             if (p != null)
                             {
                                 p.needs.mood.thoughts.memories.TryGainMemory(def: AnkhDefOf.fnarghFavor);
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter(label: "fnargh's favor",
                                          text: "The god fnargh shows mercy on your colony. He commands the web of thought to make " + p.Name.ToStringShort + " happy", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: p);
                             }
                             else
                             {
                                 throw new Exception();
                             }
                         }
                         else
                         {
                             List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).ToList();
                             if (pawns.Count > 1)
                                 pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "erdelf"));
                             if (pawns.Count > 1)
                                 pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "Serpenthalis"));
                             Pawn p = pawns.RandomElement();
                             if (p != null)
                             {
                                 p.needs.mood.thoughts.memories.TryGainMemory(def: AnkhDefOf.fnarghWrath);
                                 p.mindState.mentalStateHandler.TryStartMentalState(stateDef: DefDatabase<MentalStateDef>.AllDefs.Where(predicate: msd => msd != MentalStateDefOf.SocialFighting && msd != MentalStateDefOf.PanicFlee && !msd.defName.EqualsIgnoreCase(B: "GiveUpExit") && msd.Worker.GetType().GetField(name: "otherPawn", bindingAttr: (BindingFlags) 60) == null).RandomElement(), reason: "Fnargh's wrath", forceWake: true, causedByMood: true);
                                 if (letter)
                                     Find.LetterStack.ReceiveLetter(label: "fnargh's wrath",
                                         text: "The god fnargh is angry at your colony. He commands the web of thought to make " + p.Name.ToStringShort + " mad", textLetterDef: LetterDefOf.NegativeEvent, lookTargets: p);
                             }
                             else
                             {
                                 throw new Exception();
                             }
                         }
                    }
                },
                {
                    "therm",
                    delegate (bool favor, bool letter)
                    {
                        if (favor)
                        {
                            staticVariables.thermCount++;

                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: AnkhDefOf.thermActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "therm's favor",
                                    text: "The god of fire shows mercy on your colony. He commands the fires of this little world to follow your orders.", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "Serpenthalis"));

                            Pawn p = pawns.RandomElement();
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "therms's wrath",
                                    text: "The god therm is angry at your colony. He commands the body of " + p.Name.ToStringShort + " to combust", textLetterDef: LetterDefOf.ThreatBig, lookTargets: p);
                            foreach (IntVec3 intVec in GenAdjFast.AdjacentCells8Way(thingCenter: p.Position, thingRot: p.Rotation, thingSize: p.RotatedSize))
                                GenExplosion.DoExplosion(center: intVec, map: p.Map, radius: 2f, damType: Rand.Bool ? DamageDefOf.Flame : DamageDefOf.Stun, instigator: null);
                        }
                    }
                },
                {
                    "beepboop",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.instaResearch++;
                            if (letter)
                               Find.LetterStack.ReceiveLetter(label: "beepboop's favor",
                                    text: "The god beepboop shows mercy on your colony. The colonists find the missing link to finish their current research.", textLetterDef: LetterDefOf.PositiveEvent);

                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            if (!map.areaManager.Home.ActiveCells.Where(predicate: iv => iv.Standable(map: map)).TryRandomElement( result: out IntVec3 position))
                                throw new Exception();

                            PawnKindDef localKindDef = PawnKindDef.Named(defName: "Scyther");
                            Faction faction = FactionUtility.DefaultFactionFrom(ft: localKindDef.defaultFactionType);
                            Pawn newPawn = PawnGenerator.GeneratePawn(kindDef: localKindDef, faction: faction);

                            GenSpawn.Spawn(newThing: newPawn, loc: position, map: map);

                            if (faction != null && faction != Faction.OfPlayer)
                            {
                                Lord lord = null;
                                if (newPawn.Map.mapPawns.SpawnedPawnsInFaction(faction: faction).Any(predicate: p => p != newPawn))
                                {
                                    bool Validator(Thing p) => p != newPawn && ((Pawn) p).GetLord() != null;
                                    Pawn p2 = (Pawn)GenClosest.ClosestThing_Global(center: newPawn.Position, searchSet: newPawn.Map.mapPawns.SpawnedPawnsInFaction(faction: faction), maxDistance: 99999f, validator: Validator);
                                    lord = p2.GetLord();
                                }
                                if (lord == null)
                                {
                                    LordJob_DefendPoint lordJob = new LordJob_DefendPoint(point: newPawn.Position);
                                    lord = LordMaker.MakeNewLord(faction: faction, lordJob: lordJob, map: Find.CurrentMap);
                                }
                                lord.AddPawn(p: newPawn);
                            }
                            if(position.Roofed(map: map))
                            {
                                newPawn.DeSpawn();
                                TradeUtility.SpawnDropPod(dropSpot: position, map: map, t: newPawn);
                            } else
                            {
                                MoteMaker.ThrowMetaPuffs(rect: CellRect.CenteredOn(center: position, radius: 10), map: map);
                            }


                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "beepboop's wrath",
                                    text: "The god of robots is angry at your colony.", textLetterDef: LetterDefOf.ThreatBig, lookTargets: new GlobalTargetInfo(cell: position, map: map));
                        }
                    }
                },
                {
                    "humour",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.humourCount++;

                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: AnkhDefOf.humourActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "humour's favor",
                                    text: "The god of healing shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            Pawn p = Find.ColonistBar?.GetColonistsInOrder()?.Where(predicate: x => !x.Dead && !x.Downed && !x.mindState.mentalStateHandler.InMentalState && !x.jobs.curDriver.asleep).RandomElement();
                            if (p != null)
                            {
                                p.health.AddHediff(def: HediffDefOf.WoundInfection, part: p.health.hediffSet.GetRandomNotMissingPart(damDef: DamageDefOf.Bullet));
                                if (letter)
                                    Find.LetterStack.ReceiveLetter(label: "humour's wrath",
                                        text: "The god humour is angry at your colony.", textLetterDef: LetterDefOf.ThreatBig, lookTargets: p);
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                    }
                },
                {
                    "taylor",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            IEnumerable<ThingDef> apparelList = DefDatabase<ThingDef>.AllDefsListForReading.Where(predicate: td => td.IsApparel).ToList();

                            IntVec3 intVec = map.areaManager.Home.ActiveCells.Where(predicate: iv => iv.Standable(map: map)).RandomElement();
                            for(int i=0;i<5;i++)
                            {
                                if(apparelList.TryRandomElement(result: out ThingDef apparelDef))
                                {
                                    intVec = intVec.RandomAdjacentCell8Way();
                                    Thing apparel = ThingMaker.MakeThing(def: apparelDef, stuff: apparelDef.MadeFromStuff ? GenStuff.RandomStuffFor(td: apparelDef) : null);
                                    CompQuality qual = apparel.TryGetComp<CompQuality>();
                                    qual?.SetQuality(q: Rand.Bool ?
                                                            QualityCategory.Normal : Rand.Bool ?
                                                                QualityCategory.Good : Rand.Bool ?
                                                                    QualityCategory.Excellent : Rand.Bool ?
                                                                        QualityCategory.Masterwork :
                                                                        QualityCategory.Legendary, source: ArtGenerationContext.Colony);

                                    GenSpawn.Spawn(newThing: apparel, loc: intVec, map: map);
                                    Vector3 vec = intVec.ToVector3();
                                    MoteMaker.ThrowSmoke(loc: vec, map: map, size: 5);
                                    MoteMaker.ThrowMetaPuff(loc: vec, map: map);
                                }
                            }
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "taylor's favor",
                                    text: "The god of clothing shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(cell: intVec, map: map));
                        } else
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead).ToList();
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "erdelf"));
                            if (pawns.Count > 1)
                                pawns.RemoveAll(match: x => x.Name.ToStringShort.EqualsIgnoreCase(B: "Serpenthalis"));



                            Pawn p = pawns.Where(predicate: pawn => !pawn.apparel.PsychologicallyNude).RandomElement();
                            p.apparel.DestroyAll();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "taylor's wrath",
                                    text: "The god of clothing is angry at your colony. He commands the clothing on " + p.Name.ToStringShort + " to destroy itself", textLetterDef: LetterDefOf.ThreatBig, lookTargets: p);
                        }
                    }
                },
                {
                    "dorf",
                    delegate (bool favor, bool letter)
                    {
                        if(favor)
                        {
                            staticVariables.dorfCount++;

                            List<Thing> activators = Find.Maps.Where(predicate: m => m.IsPlayerHome).SelectMany(selector: m => m.listerThings.ThingsOfDef(def: AnkhDefOf.dorfActivator)).ToList();

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "dorf's favor",
                                    text: "The god of ming shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent, lookTargets: new GlobalTargetInfo(thing: activators.NullOrEmpty() ? null : activators.RandomElement()));
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            if (!map.areaManager.Home.ActiveCells.Where(predicate: iv => iv.Standable(map: map)).TryRandomElement( result: out IntVec3 position))
                                throw new Exception();

                            CellRect cellRect = CellRect.CenteredOn(center: position, radius: 2);
                            cellRect.ClipInsideMap(map: Find.CurrentMap);
                            ThingDef granite = ThingDefOf.Granite;
                            foreach (IntVec3 current in cellRect) GenSpawn.Spawn(def: granite, loc: current, map: Find.CurrentMap);

                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "dorf's wrath",
                                    text: "The god of mining is angry at your colony.", textLetterDef: LetterDefOf.ThreatBig, lookTargets: new GlobalTargetInfo(cell: position, map: map));
                        }
                    }
                },
                {
                    "downward_dick",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {
                            List<Pawn> pawns = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead && !AnkhDefOf.ankhTraits.TrueForAll(match: t => x.story.traits.HasTrait(tDef: t))).ToList();

                            if(pawns.NullOrEmpty())
                                throw new Exception();

                            pawns.ForEach(action: p =>
                            {
                                Trait trait;
                                do
                                {
                                    trait = new Trait(def: AnkhDefOf.ankhTraits.RandomElement(), degree: 0, forced: true);
                                    if(!p.story.traits.HasTrait(tDef: trait.def))
                                        p.story.traits.GainTrait(trait: trait);
                                }while (!p.story.traits.allTraits.Contains(item: trait));
                                p.story.traits.allTraits.Remove(item: trait);
                                p.story.traits.allTraits.Insert(index: 0, item: trait);
                            });
                            if (letter)
                                Find.LetterStack.ReceiveLetter(label: "dick's favor",
                                    text: "The god of dicks shows mercy on your colony.", textLetterDef: LetterDefOf.PositiveEvent);
                        } else
                        {
                            Map map = Find.AnyPlayerHomeMap;
                            List<Pawn> colonists = Find.ColonistBar.GetColonistsInOrder().Where(predicate: x => !x.Dead).ToList();

                            IncidentParms parms = new IncidentParms()
                            {
                                target = map,
                                points = colonists.Count * PawnKindDefOf.AncientSoldier.combatPower,
                                faction = Find.FactionManager.FirstFactionOfDef(facDef: FactionDefOf.AncientsHostile),
                                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                                raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop,
                                podOpenDelay = GenDate.TicksPerHour/2,
                                spawnCenter = map.listerBuildings.ColonistsHaveBuildingWithPowerOn(def: ThingDefOf.OrbitalTradeBeacon) ? DropCellFinder.TradeDropSpot(map: map) : RCellFinder.TryFindRandomSpotJustOutsideColony(originCell: map.IsPlayerHome ? map.mapPawns.FreeColonists.RandomElement().Position : CellFinder.RandomCell(map: map), map: map, result: out IntVec3 spawnPoint) ? spawnPoint : CellFinder.RandomCell(map: map),
                                generateFightersOnly = true,
                                forced = true,
                                raidNeverFleeIndividual = true
                            };
                            List<Pawn> pawns = new PawnGroupMaker()
                            {
                                kindDef = new PawnGroupKindDef()
                                {
                                    workerClass = typeof(CustomIncidentClasses.PawnGroupKindWorker_Wrath)
                                },
                            }.GeneratePawns(parms: new PawnGroupMakerParms()
                            {
                                tile = ((Map) parms.target).Tile,
                                faction = parms.faction,
                                points = parms.points,
                                generateFightersOnly = true,
                                raidStrategy = parms.raidStrategy
                            }).ToList();

                            IEnumerable<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading.Where(predicate: rd => (rd.addsHediff?.addedPartProps?.betterThanNatural ?? false) && 
                                (rd.fixedIngredientFilter?.AllowedThingDefs.Any(predicate: td => td.techHediffsTags?.Contains(item: "Advanced") ?? false) ?? false) && !rd.appliedOnFixedBodyParts.NullOrEmpty()).ToList();

                            for(int i = 0; i<pawns.Count;i++)
                            {
                                Pawn colonist = colonists[index: i];
                                Pawn pawn = pawns[index: i];

                                pawn.Name = colonist.Name;
                                pawn.story.traits.allTraits = colonist.story.traits.allTraits.ListFullCopy();
                                pawn.story.childhood = colonist.story.childhood;
                                pawn.story.adulthood = colonist.story.adulthood;
                                pawn.skills.skills = colonist.skills.skills.ListFullCopy();
                                pawn.health.hediffSet.hediffs = colonist.health.hediffSet.hediffs.ListFullCopy().Where(predicate: hediff => hediff is Hediff_AddedPart).ToList();
                                pawn.story.bodyType = colonist.story.bodyType;
                                (typeof(Pawn_StoryTracker).GetField(name: "headGraphicPath", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new NullReferenceException()).SetValue(obj: pawn.story, value: colonist.story.HeadGraphicPath);
                                FieldInfo recordInfo = typeof(Pawn_RecordsTracker).GetField(name: "records", bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance);
                                (recordInfo ?? throw new NullReferenceException()).SetValue(obj: pawn.records, value: recordInfo.GetValue(obj: colonist.records));
                                pawn.gender = colonist.gender;
                                pawn.story.hairDef = colonist.story.hairDef;
                                pawn.story.hairColor = colonist.story.hairColor;
                                pawn.apparel.DestroyAll();

                                colonist.apparel.WornApparel.ForEach(action: ap =>
                                {
                                    Apparel copy = ThingMaker.MakeThing(def: ap.def, stuff: ap.Stuff) as Apparel;
                                    copy.TryGetComp<CompQuality>().SetQuality(q: ap.TryGetComp<CompQuality>().Quality, source: ArtGenerationContext.Colony);
                                    pawn.apparel.Wear(newApparel: copy);
                                });
                                
                                foreach(FieldInfo fi in typeof(Pawn_AgeTracker).GetFields(bindingAttr: BindingFlags.NonPublic | BindingFlags.Instance))
                                    if(!fi.Name.EqualsIgnoreCase(B: "pawn"))
                                        fi.SetValue(obj: pawn.ageTracker, value: fi.GetValue(obj: colonist.ageTracker));

                                pawn.story.melanin = colonist.story.melanin;

                                for(int x = 0; x<5; x++)
                                {
                                    recipes.Where(predicate: rd => rd.appliedOnFixedBodyParts != null).TryRandomElement(result: out RecipeDef recipe);
                                    BodyPartRecord record;
                                    do
                                        record = pawn.health.hediffSet.GetRandomNotMissingPart(damDef: DamageDefOf.Bullet);
                                    while(!recipe.appliedOnFixedBodyParts.Contains(item: record.def));
                                    recipe.Worker.ApplyOnPawn(pawn: pawn, part: record, billDoer: null, ingredients: recipe.fixedIngredientFilter.AllowedThingDefs.Select(selector: td => ThingMaker.MakeThing(def: td, stuff: td.MadeFromStuff ? GenStuff.DefaultStuffFor(bd: td) : null)).ToList(), bill: null);
                                }
                                pawn.equipment.DestroyAllEquipment();
                                ThingDef weaponDef = new[] { ThingDef.Named(defName: "Gun_AssaultRifle"), ThingDef.Named(defName: "Gun_ChargeRifle"), ThingDef.Named(defName: "MeleeWeapon_LongSword") }.RandomElement();
                                if(weaponDef.IsRangedWeapon)
                                    pawn.apparel.WornApparel.RemoveAll(match: ap => ap.def == ThingDefOf.Apparel_ShieldBelt);
                                ThingWithComps weapon = ThingMaker.MakeThing(def: weaponDef, stuff: weaponDef.MadeFromStuff ? ThingDefOf.Plasteel : null) as ThingWithComps;
                                weapon.TryGetComp<CompQuality>().SetQuality(q: Rand.Bool ? QualityCategory.Normal : Rand.Bool ? QualityCategory.Good : QualityCategory.Excellent, source: ArtGenerationContext.Colony);
                                pawn.equipment.AddEquipment(newEq: weapon);
                                pawn.story.traits.GainTrait(trait: new Trait(def: AnkhDefOf.ankhTraits.RandomElement(), degree: 0, forced: true));
                            }

                            DropPodUtility.DropThingsNear(dropCenter: parms.spawnCenter, map: map, things: pawns.Cast<Thing>(), openDelay: parms.podOpenDelay, canInstaDropDuringInit: false, leaveSlag: true);
                            parms.raidStrategy.Worker.MakeLords(parms: parms, pawns: pawns);
                            AvoidGridMaker.RegenerateAvoidGridsFor(faction: parms.faction, map: map);

                            MoteMaker.ThrowMetaPuffs(rect: CellRect.CenteredOn(center: parms.spawnCenter, radius: 10), map: map);

                            if(letter)
                                Find.LetterStack.ReceiveLetter(label: "dick's wrath",
                                    text: "The god of dicks is angry at your colony.", textLetterDef: LetterDefOf.ThreatBig, lookTargets: new GlobalTargetInfo(cell: parms.spawnCenter, map: map));
                        }
                    }
                }/*,
                {
                    "erdelf",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {

                        } else
                        {

                        }
                    }
                },
                {
                    "pladd",
                    delegate(bool favor, bool letter)
                    {
                        if(favor)
                        {

                        } else
                        {

                        }
                    }
                }*/
        };

        private static void PrepareDefs()
        {
            MethodInfo shortHashGiver = typeof(ShortHashGiver).GetMethod(name: "GiveShortHash", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static) ?? throw new ArgumentNullException();
            Type t = typeof(AnkhDefOf);
            StatDefOf.MeleeHitChance.maxValue = float.MaxValue;

            #region GameConditions
            {
                GameConditionDef wrathConditionDef = new GameConditionDef()
                {
                    defName = "wrathConditionDef",
                    conditionClass = typeof(CustomIncidentClasses.MapCondition_WrathBombing),
                    label = "wrath of the dead",
                    description = "The gods sent their pawn  down in human form to serve your colony... and you failed him",
                    endMessage = "The gods are satisfied with your pain",
                    preventRain = false,
                    canBePermanent = true
                };
                wrathConditionDef.ResolveReferences();
                wrathConditionDef.PostLoad();
                DefDatabase<GameConditionDef>.Add(def: wrathConditionDef);
                AnkhDefOf.wrathCondition = wrathConditionDef;
                shortHashGiver.Invoke(obj: null, parameters: new object[] { wrathConditionDef, t });
            }
            #endregion
            #region Incidents
            {
                IncidentDef miracleHeal = new IncidentDef()
                {
                    defName = "MiracleHeal",
                    label = "miracle heal",
                    targetTags = new List<IncidentTargetTagDef>() { IncidentTargetTagDefOf.Map_PlayerHome },
                    workerClass = typeof(CustomIncidentClasses.MiracleHeal),
                    category = IncidentCategoryDefOf.Misc,
                    baseChance = 10
                };
                miracleHeal.ResolveReferences();
                miracleHeal.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { miracleHeal, t });
                DefDatabase<IncidentDef>.Add(def: miracleHeal);
                AnkhDefOf.miracleHeal = miracleHeal;
            }
            {
                IncidentDef altarAppearance = new IncidentDef()
                {
                    defName = "AltarAppearance",
                    label = "altar Appearance",
                    targetTags = new List<IncidentTargetTagDef>() { IncidentTargetTagDefOf.Map_PlayerHome },
                    workerClass = typeof(CustomIncidentClasses.AltarAppearance),
                    category = IncidentCategoryDefOf.Misc,
                    baseChance = 90
                };
                altarAppearance.ResolveReferences();
                altarAppearance.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { altarAppearance, t });
                DefDatabase<IncidentDef>.Add(def: altarAppearance);
            }
            #endregion
            #region Buildings
            Type type = typeof(ThingDefGenerator_Buildings);
            MethodInfo blueprintInfo = type.GetMethod(name: "NewBlueprintDef_Thing", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static) ?? throw new ArgumentNullException();
            MethodInfo frameInfo = type.GetMethod(name: "NewFrameDef_Thing", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static) ?? throw new ArgumentNullException();
            {
                ThingDef zap = new ThingDef()
                {
                    defName = "ZAPActivator",
                    thingClass = typeof(Buildings.Building_Zap),
                    label = "ZAP Activator",
                    description = "This device is little more than an altar to Zap, engraved with his jagged yellow symbol. It will defend the ones favored by zap.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    tickerType = TickerType.Rare,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_zap",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                zap.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { zap, false, null });
                zap.blueprintDef.ResolveReferences();
                zap.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { zap, true, zap.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                zap.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { zap});
                zap.frameDef.ResolveReferences();
                zap.frameDef.PostLoad();

                zap.ResolveReferences();
                zap.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { zap, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { zap.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { zap.frameDef, t });

                DefDatabase<ThingDef>.Add(def: zap);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: zap.blueprintDef);
                DefDatabase<ThingDef>.Add(def: zap.frameDef);
                zap.designationCategory.ResolveReferences();
                zap.designationCategory.PostLoad();
                AnkhDefOf.zapActivator = zap;
            }
            {
                ThingDef therm = new ThingDef()
                {
                    defName = "THERMActivator",
                    thingClass = typeof(Buildings.Building_Therm),
                    label = "THERM Activator",
                    description = "This device is little more than an altar to Therm, engraved with his fiery symbol. Use it to invoke therm's favor.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_therm",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                therm.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { therm, false, null });
                therm.blueprintDef.ResolveReferences();
                therm.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { therm, true, therm.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                therm.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { therm });
                therm.frameDef.ResolveReferences();
                therm.frameDef.PostLoad();

                therm.ResolveReferences();
                therm.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { therm, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { therm.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { therm.frameDef, t });

                DefDatabase<ThingDef>.Add(def: therm);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: therm.blueprintDef);
                DefDatabase<ThingDef>.Add(def: therm.frameDef);
                therm.designationCategory.ResolveReferences();
                therm.designationCategory.PostLoad();
                AnkhDefOf.thermActivator = therm;
            }
            {
                ThingDef peg = new ThingDef()
                {
                    defName = "PEGActivator",
                    thingClass = typeof(Buildings.Building_Peg),
                    label = "PEG Activator",
                    description = "This device is little more than an altar to Peg, engraved with her skully sign. It will defend the ones favored by peg.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_peg",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                peg.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { peg, false, null });
                peg.blueprintDef.ResolveReferences();
                peg.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { peg, true, peg.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                peg.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { peg });
                peg.frameDef.ResolveReferences();
                peg.frameDef.PostLoad();

                peg.ResolveReferences();
                peg.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { peg, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { peg.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { peg.frameDef, t });

                DefDatabase<ThingDef>.Add(def: peg);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: peg.blueprintDef);
                DefDatabase<ThingDef>.Add(def: peg.frameDef);
                peg.designationCategory.ResolveReferences();
                peg.designationCategory.PostLoad();
                AnkhDefOf.pegActivator = peg;
            }
            {
                ThingDef repo = new ThingDef()
                {
                    defName = "REPOActivator",
                    thingClass = typeof(Buildings.Building_Repo),
                    label = "REPO Activator",
                    description = "This device is little more than an altar to Repo. Use it to restore.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_repo",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                repo.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { repo, false, null });
                repo.blueprintDef.ResolveReferences();
                repo.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { repo, true, repo.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                repo.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { repo });
                repo.frameDef.ResolveReferences();
                repo.frameDef.PostLoad();

                repo.ResolveReferences();
                repo.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { repo, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { repo.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { repo.frameDef, t });

                DefDatabase<ThingDef>.Add(def: repo);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: repo.blueprintDef);
                DefDatabase<ThingDef>.Add(def: repo.frameDef);
                repo.designationCategory.ResolveReferences();
                repo.designationCategory.PostLoad();

                AnkhDefOf.repoActivator = repo;
            }
            {
                ThingDef bob = new ThingDef()
                {
                    defName = "BOBActivator",
                    thingClass = typeof(Buildings.Building_Bob),
                    label = "BOB Activator",
                    description = "This device is little more than an altar to Bob.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_bob",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                bob.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { bob, false, null });
                bob.blueprintDef.ResolveReferences();
                bob.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { bob, true, bob.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                bob.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { bob });
                bob.frameDef.ResolveReferences();
                bob.frameDef.PostLoad();

                bob.ResolveReferences();
                bob.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { bob, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { bob.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { bob.frameDef, t });

                DefDatabase<ThingDef>.Add(def: bob);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: bob.blueprintDef);
                DefDatabase<ThingDef>.Add(def: bob.frameDef);
                bob.designationCategory.ResolveReferences();
                bob.designationCategory.PostLoad();
                AnkhDefOf.bobActivator = bob;
            }
            {
                ThingDef rootsy = new ThingDef()
                {
                    defName = "ROOTSYActivator",
                    thingClass = typeof(Buildings.Building_Rootsy),
                    label = "ROOTSY Activator",
                    description = "This device is little more than an altar to Rootsy.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_rootsy",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                rootsy.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { rootsy, false, null });
                rootsy.blueprintDef.ResolveReferences();
                rootsy.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { rootsy, true, rootsy.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                rootsy.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { rootsy });
                rootsy.frameDef.ResolveReferences();
                rootsy.frameDef.PostLoad();

                rootsy.ResolveReferences();
                rootsy.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { rootsy, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { rootsy.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { rootsy.frameDef, t });

                DefDatabase<ThingDef>.Add(def: rootsy);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: rootsy.blueprintDef);
                DefDatabase<ThingDef>.Add(def: rootsy.frameDef);
                rootsy.designationCategory.ResolveReferences();
                rootsy.designationCategory.PostLoad();
                AnkhDefOf.rootsyActivator = rootsy;
            }
            {
                ThingDef humour = new ThingDef()
                {
                    defName = "HUMOURActivator",
                    thingClass = typeof(Buildings.Building_Humour),
                    label = "HUMOUR Activator",
                    description = "This device is little more than an altar to Humour.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_humour",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                humour.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { humour, false, null });
                humour.blueprintDef.ResolveReferences();
                humour.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { humour, true, humour.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                humour.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { humour });
                humour.frameDef.ResolveReferences();
                humour.frameDef.PostLoad();

                humour.ResolveReferences();
                humour.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { humour, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { humour.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { humour.frameDef, t });

                DefDatabase<ThingDef>.Add(def: humour);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: humour.blueprintDef);
                DefDatabase<ThingDef>.Add(def: humour.frameDef);
                humour.designationCategory.ResolveReferences();
                humour.designationCategory.PostLoad();
                AnkhDefOf.humourActivator = humour;
            }
            {
                ThingDef dorf = new ThingDef()
                {
                    defName = "DORFActivator",
                    thingClass = typeof(Buildings.Building_Dorf),
                    label = "DORF Activator",
                    description = "This device is little more than an altar to Dorf.",
                    size = new IntVec2(newX: 1, newZ: 1),
                    passability = Traversability.PassThroughOnly,
                    category = ThingCategory.Building,
                    selectable = true,
                    designationCategory = DesignationCategoryDefOf.Structure,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    resourcesFractionWhenDeconstructed = 1,
                    rotatable = false,
                    graphicData = new GraphicData()
                    {
                        texPath = "SH_dorf",
                        graphicClass = typeof(Graphic_Single),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 1, y: 1)
                    },
                    statBases = new List<StatModifier>()
                        {
                            new StatModifier()
                            {
                                stat = StatDefOf.MaxHitPoints,
                                value = 500
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.WorkToMake,
                                value = 200
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Flammability,
                                value = 0
                            },
                            new StatModifier()
                            {
                                stat = StatDefOf.Beauty,
                                value = 4
                            }
                        },
                    costList = new List<ThingDefCountClass>(),
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    minifiedDef = ThingDefOf.MinifiedThing
                };
                dorf.blueprintDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { dorf, false, null });
                dorf.blueprintDef.ResolveReferences();
                dorf.blueprintDef.PostLoad();

                ThingDef minifiedDef = (ThingDef) blueprintInfo.Invoke(obj: null, parameters: new object[] { dorf, true, dorf.blueprintDef });
                minifiedDef.ResolveReferences();
                minifiedDef.PostLoad();

                dorf.frameDef = (ThingDef) frameInfo.Invoke(obj: null, parameters: new object[] { dorf });
                dorf.frameDef.ResolveReferences();
                dorf.frameDef.PostLoad();

                dorf.ResolveReferences();
                dorf.PostLoad();

                shortHashGiver.Invoke(obj: null, parameters: new object[] { dorf, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { minifiedDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { dorf.blueprintDef, t });
                shortHashGiver.Invoke(obj: null, parameters: new object[] { dorf.frameDef, t });

                DefDatabase<ThingDef>.Add(def: dorf);
                DefDatabase<ThingDef>.Add(def: minifiedDef);
                DefDatabase<ThingDef>.Add(def: dorf.blueprintDef);
                DefDatabase<ThingDef>.Add(def: dorf.frameDef);
                dorf.designationCategory.ResolveReferences();
                dorf.designationCategory.PostLoad();
                AnkhDefOf.dorfActivator = dorf;
            }
            {
                ThingDef sacrificeAltar = new ThingDef()
                {
                    defName = "sacrificeAltar",
                    thingClass = typeof(Buildings.Building_Altar),
                    label = "sacrifice altar",
                    description = "This Altar serves as a way to please the gods.",
                    size = new IntVec2(newX: 3, newZ: 1),
                    passability = Traversability.Impassable,
                    category = ThingCategory.Building,
                    selectable = true,
                    useHitPoints = false,
                    altitudeLayer = AltitudeLayer.Building,
                    leaveResourcesWhenKilled = true,
                    rotatable = false,
                    stuffCategories = new List<StuffCategoryDef>(capacity: 3) { StuffCategoryDefOf.Metallic, StuffCategoryDefOf.Stony},
                    costStuffCount = 1,
                    graphicData = new GraphicData()
                    {
                        texPath = "HumanAltar",
                        graphicClass = typeof(Graphic_Multi),
                        shaderType = ShaderTypeDefOf.CutoutComplex,
                        drawSize = new Vector2(x: 4, y: 2)
                    },
                    statBases = new List<StatModifier>()
                    {
                        new StatModifier()
                        {
                            stat = StatDefOf.MaxHitPoints,
                            value = 500
                        },
                        new StatModifier()
                        {
                            stat = StatDefOf.WorkToMake,
                            value = 200
                        },
                        new StatModifier()
                        {
                            stat = StatDefOf.Flammability,
                            value = 0
                        },
                        new StatModifier()
                        {
                            stat = StatDefOf.Beauty,
                            value = 50
                        }
                    },
                    building = new BuildingProperties()
                    {
                        isInert = true
                    },
                    inspectorTabs = new List<Type>() { typeof(ITab_Wraths) },
                    hasInteractionCell = true,
                    interactionCellOffset = new IntVec3(newX: 0, newY: 0, newZ: -1)
                };
                sacrificeAltar.ResolveReferences();
                sacrificeAltar.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { sacrificeAltar, t });
                DefDatabase<ThingDef>.Add(def: sacrificeAltar);
                AnkhDefOf.sacrificeAltar = sacrificeAltar;
            }
            #endregion
            #region Thoughts
            {
                ThoughtDef fnarghWrath = new ThoughtDef()
                {
                    defName = "FnarghWrath",
                    durationDays = 1.0f,
                    stackLimit = 100,
                    stackedEffectMultiplier = 1f,
                    stages = new List<ThoughtStage>()
                    {
                        new ThoughtStage()
                        {
                            label = "Fnargh's wrath",
                            description = "Fnargh's presence in my mind is like a thousand writhing insects.",
                            baseMoodEffect = -20f
                        }
                    }
                };
                fnarghWrath.ResolveReferences();
                fnarghWrath.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { fnarghWrath, t });
                DefDatabase<ThoughtDef>.Add(def: fnarghWrath);
                AnkhDefOf.fnarghWrath = fnarghWrath;
            }
            {
                ThoughtDef fnarghFavor = new ThoughtDef()
                {
                    defName = "FnarghFavor",
                    durationDays = 1.0f,
                    stackLimit = 100,
                    stackedEffectMultiplier = 1f,
                    stages = new List<ThoughtStage>()
                    {
                        new ThoughtStage()
                        {
                            label = "Fnargh's favor",
                            description = "Fnargh's presence in my mind soothes me. Everything seems just a little bit better.",
                            baseMoodEffect = 20f
                        }
                    }
                };
                fnarghFavor.ResolveReferences();
                fnarghFavor.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { fnarghFavor, t });
                DefDatabase<ThoughtDef>.Add(def: fnarghFavor);
                AnkhDefOf.fnarghFavor = fnarghFavor;
            }
            #endregion
            #region Traits
            {
                TraitDef fiveKnuckleShuffle = new TraitDef()
                {
                    defName = "fiveKnuckleShuffle",
                    label = "iamdar's Five Knuckle Shuffle",
                    description = "Downward Dick's good good touchin' has enhanced your focus and manipulation!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "iamdar's Five Knuckle Shuffle",
                            description = "Downward Dick's good good touchin' has enhanced your focus and manipulation!"
                        }
                    }
                };
                fiveKnuckleShuffle.ResolveReferences();
                fiveKnuckleShuffle.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { fiveKnuckleShuffle, t });
                DefDatabase<TraitDef>.Add(def: fiveKnuckleShuffle);
                AnkhDefOf.ankhTraits.Add(item: fiveKnuckleShuffle);
                AnkhDefOf.fiveKnuckleShuffle = fiveKnuckleShuffle;
            }
            {
                TraitDef coneOfShame = new TraitDef()
                {
                    defName = "coneOfShame",
                    label = "chiky's Cone of Shame",
                    description = " Downward Dick's good good touchin' has enhanced your focus and your visual acuity!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "chiky's Cone of Shame",
                            description = " Downward Dick's good good touchin' has enhanced your focus and your visual acuity!"
                        }
                    }
                };
                coneOfShame.ResolveReferences();
                coneOfShame.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { coneOfShame, t });
                DefDatabase<TraitDef>.Add(def: coneOfShame);
                AnkhDefOf.ankhTraits.Add(item: coneOfShame);
                AnkhDefOf.coneOfShame = coneOfShame;
            }
            {
                TraitDef thrustsOfVeneration = new TraitDef()
                {
                    defName = "thrustsOfVeneration",
                    label = "Ucefzach's Thrusts",
                    description = "Downward Dick's good good touchin' has made you a more effective warrior!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "Ucefzach's Thrusts",
                            description = "Downward Dick's good good touchin' has made you a more effective warrior!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.MeleeHitChance,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.AimingDelayFactor,
                                    value = -0.5f
                                }
                            }
                        }
                    }
                };
                thrustsOfVeneration.ResolveReferences();
                thrustsOfVeneration.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { thrustsOfVeneration, t });
                DefDatabase<TraitDef>.Add(def: thrustsOfVeneration);
                AnkhDefOf.ankhTraits.Add(item: thrustsOfVeneration);
            }
            {
                TraitDef armoredTouch = new TraitDef()
                {
                    defName = "armoredTouch",
                    label = "Southpond's En-Armored Touch",
                    description = "Downward Dick's good good touchin' has surrounded you with a magical armored barrier!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "Southpond's En-Armored Touch",
                            description = "Downward Dick's good good touchin' has surrounded you with a magical armored barrier!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Blunt,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Heat,
                                    value = 0.5f
                                },
                                new StatModifier()
                                {
                                    stat = StatDefOf.ArmorRating_Sharp,
                                    value = 0.5f
                                }
                            }
                        }
                    }
                };
                armoredTouch.ResolveReferences();
                armoredTouch.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { armoredTouch, t });
                DefDatabase<TraitDef>.Add(def: armoredTouch);
                AnkhDefOf.ankhTraits.Add(item: armoredTouch);
            }
            {
                TraitDef teaAndScones = new TraitDef()
                {
                    defName = "teaAndScones",
                    label = "maebak's tea and scones",
                    description = "Downward Dick's good good touchin' has increased your healing rate!",
                    degreeDatas = new List<TraitDegreeData>()
                    {
                        new TraitDegreeData()
                        {
                            label = "maebak's tea and scones",
                            description = "Downward Dick's good good touchin' has increased your healing rate!",
                            statOffsets = new List<StatModifier>()
                            {
                                new StatModifier()
                                {
                                    stat = StatDefOf.ImmunityGainSpeed,
                                    value = 1.5f
                                }
                            }
                        }
                    }
                };
                teaAndScones.ResolveReferences();
                teaAndScones.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { teaAndScones, t });
                DefDatabase<TraitDef>.Add(def: teaAndScones);
                AnkhDefOf.ankhTraits.Add(item: teaAndScones);
                AnkhDefOf.teaAndScones = teaAndScones;
            }
            #endregion
            #region Hediffs
            {
                HediffDef fiveKnuckleShuffleHediff = new HediffDef()
                {
                    defName = "fiveKnuckleShuffleHediff",
                    label = "iamdar's five knuckle shuffle",
                    description = "Whatever.. pladd tell me something",
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage>()
                    {
                        new HediffStage()
                        {
                            capMods = new List<PawnCapacityModifier>()
                            {
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Consciousness,
                                    offset = 0.4f
                                },
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Manipulation,
                                    offset = 0.4f
                                }
                            },
                            becomeVisible = false
                        }
                    }
                };
                fiveKnuckleShuffleHediff.ResolveReferences();
                fiveKnuckleShuffleHediff.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { fiveKnuckleShuffleHediff, t });
                DefDatabase<HediffDef>.Add(def: fiveKnuckleShuffleHediff);
                AnkhDefOf.fiveKnuckleShuffleHediff = fiveKnuckleShuffleHediff;
            }
            {
                HediffDef coneOfShameHediff = new HediffDef()
                {
                    defName = "coneOfShameHediff",
                    label = "chiky's Cone of Shame",
                    description = "Whatever.. pladd tell me something",
                    hediffClass = typeof(Hediff),
                    stages = new List<HediffStage>()
                    {
                        new HediffStage()
                        {
                            capMods = new List<PawnCapacityModifier>()
                            {
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Consciousness,
                                    offset = 0.4f
                                },
                                new PawnCapacityModifier()
                                {
                                    capacity = PawnCapacityDefOf.Sight,
                                    offset = 0.4f
                                }
                            },
                            becomeVisible = false
                        }
                    }
                };
                coneOfShameHediff.ResolveReferences();
                coneOfShameHediff.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { coneOfShameHediff, t });
                DefDatabase<HediffDef>.Add(def: coneOfShameHediff);
                AnkhDefOf.coneOfShameHediff = coneOfShameHediff;
            }
            #endregion
            #region Jobs
            {
                JobDef sacrifice = new JobDef()
                {
                    defName = "sacrificeYourself",
                    driverClass = typeof(JobDriver_Sacrifice),
                    reportString = "selfsacrificing"
                };
                sacrifice.ResolveReferences();
                sacrifice.PostLoad();
                shortHashGiver.Invoke(obj: null, parameters: new object[] { sacrifice, t });
                DefDatabase<JobDef>.Add(def: sacrifice);
                AnkhDefOf.sacrificeToAltar = sacrifice;
            }
            #endregion
        }

        private static void SendWrathLetter(string name, bool possessive, GlobalTargetInfo info) => 
            Find.LetterStack.ReceiveLetter(label: "wrath of " + name, text: name + " died, prepare to meet " + (possessive ? "his" : "her") + " wrath", textLetterDef: LetterDefOf.ThreatBig, lookTargets: info);

        private static List<string> ReadFileAndFetchStrings(string file)
        {
            try
            {
                List<string> sb = new List<string>();
                using (FileStream fs = File.Open(path: file, mode: FileMode.Open, access: FileAccess.Read, share: FileShare.ReadWrite))
                {
                    using (BufferedStream bs = new BufferedStream(stream: fs))
                    {
                        using (StreamReader sr = new StreamReader(stream: bs))
                        {
                            string str;
                            while ((str = sr.ReadLine()) != null) sb.Add(item: str);
                        }
                    }
                }
                return sb;
            }
            catch (Exception e)
            {
                Debug.Log(message: e.Message + "\n" + e.StackTrace);
                return new List<string>();
            }
        }

        public void WaitAndExecute(Action action) => this.StartCoroutine(routine: this.WaitAndExecuteCoroutine(action: action));

        public IEnumerator WaitAndExecuteCoroutine(Action action)
        {
            yield return 100;
            action();
        }
    }
}