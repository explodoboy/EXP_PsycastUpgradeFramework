using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace PsycastUpgradeFramework
{
    public class PsycastUpgradeFrameworkMod : Mod
    {
        public PsycastUpgradeFrameworkMod(ModContentPack pack) : base(pack)
        {
            new Harmony("PsycastUpgradeFramework.Mod").PatchAll();
        }
    }

    [HarmonyPatch(typeof(ThingDefGenerator_Neurotrainer), "ImpliedThingDefs")]
    public class ImpliedThingDefs_Patch
    {
        private static IEnumerable<ThingDef> Postfix(IEnumerable<ThingDef> __result)
        {
            foreach (var r in __result)
            {
                var comp = r.GetCompProperties<CompProperties_Neurotrainer>();
                if (comp?.ability != null)
                {

                    var extension = comp.ability.GetModExtension<PsycastExtension>();
                    if (extension != null && extension.upgradeOnly)
                    {
                        continue;
                    }
                }
                yield return r;
            }
        }
    }

    [HarmonyPatch(typeof(CompUseEffect_GainAbility), "CanBeUsedBy")]
    public class CanBeUsedBy_Patch
    {
        private static void Postfix(CompUseEffect_GainAbility __instance, ref bool __result, Pawn p, ref string failReason)
        {
            if (__result)
            {
                var ability = __instance.parent.GetComp<CompNeurotrainer>().ability;
                if (HasDescendantAbilities(ability, p))
                {
                    failReason = "PUF.HasUpgradedAbility".Translate();
                    __result = false;
                }
            }
        }

        public static bool HasDescendantAbilities(AbilityDef ability, Pawn p)
        {
            var psycastExtension = ability.GetModExtension<PsycastExtension>();
            if (psycastExtension?.upgradesTo != null)
            {
                foreach (var upgrade in psycastExtension.upgradesTo)
                {
                    if (p.abilities.GetAbility(upgrade.ability) != null)
                    {
                        return true;
                    }
                    else if (HasDescendantAbilities(upgrade.ability, p))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public class AdditionalHediff
    {
        public HediffDef hediff;
        public float severity;
    }
    public class UpgradablePsycastDef
    {
        public AbilityDef ability;
        public int upgradeCost;
        public int upgradeTime;
        public int? refundAmountIfInterrupted;
        public AdditionalHediff inflictHediffOnInterrupt;
        public ResearchProjectDef prerequisiteTechnology;
    }
    public class PsycastExtension : DefModExtension
    {
        public List<UpgradablePsycastDef> upgradesTo;
        public bool upgradeOnly;
    }

    public class CompProperties_PsychicReshaper : CompProperties
    {
        public CompProperties_PsychicReshaper()
        {
            this.compClass = typeof(CompPsychicReshaper);
        }
    }

    public enum UpgradeStage
    {
        NotStarted,
        Active,
    }
    [StaticConstructorOnStartup]
    public class CompPsychicReshaper : ThingComp
    {
        private static readonly Texture2D CancelCommandTex = ContentFinder<Texture2D>.Get("UI/Designators/Cancel");
        private Ability activeUpgradableAbility;
        private int upgradableIndex;
        public Building_Casket Building_Casket => this.parent as Building_Casket;
        public Pawn InnerPawn => Building_Casket.GetDirectlyHeldThings().OfType<Pawn>().FirstOrDefault();

        private CompRefuelable compRefuelable;
        private CompPowerTrader compPower;


        private UpgradeStage upgradeStage;

        private int finishUpgradeTick;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            compRefuelable = this.parent.TryGetComp<CompRefuelable>();
            compPower = this.parent.TryGetComp<CompPowerTrader>();
        }
        public IEnumerable<Ability> GetUpgradableAbilities(Pawn pawn)
        {
            foreach (var ability in pawn.abilities?.abilities ?? Enumerable.Empty<Ability>())
            {
                var extension = ability.def.GetModExtension<PsycastExtension>();
                if (extension != null)
                {
                    if (!pawn.abilities.abilities.Any(ab => extension.upgradesTo.Any(upgradable => upgradable.ability == ab.def)))
                    {
                        yield return ability;
                    }
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder(base.CompInspectStringExtra());
            if (upgradeStage == UpgradeStage.Active)
            {
                var progress = ((UpgradablePsycast.upgradeTime - (finishUpgradeTick - Find.TickManager.TicksGame)) / (float)UpgradablePsycast.upgradeTime) * 100f;
                sb.AppendLine("PUF.UpgradeProgress".Translate(progress.ToStringDecimalIfSmall()));
            }
            return sb.ToString().TrimEndNewlines();
        }
        public IEnumerable<UpgradablePsycastDef> UpgradesFrom(AbilityDef abilityDef)
        {
            return abilityDef.GetModExtension<PsycastExtension>().upgradesTo.Where(upgradable => upgradable.prerequisiteTechnology is null || upgradable.prerequisiteTechnology.IsFinished);
        }
        private UpgradablePsycastDef UpgradablePsycast
        {
            get
            {
                if (activeUpgradableAbility != null)
                {
                    try
                    {
                        return activeUpgradableAbility.def.GetModExtension<PsycastExtension>().upgradesTo[upgradableIndex];
                    }
                    catch
                    {
                        activeUpgradableAbility = null;
                        upgradableIndex = -1;
                    }
                }
                return null;
            }
        }
        private bool EnoughFuelToStart(UpgradablePsycastDef upgradablePsycastDef)
        {
            return compRefuelable.Fuel >= upgradablePsycastDef.upgradeCost;
        }
        public override void CompTick()
        {
            base.CompTick();
            if (upgradeStage == UpgradeStage.NotStarted && activeUpgradableAbility != null)
            {
                var upgradablePsycast = UpgradablePsycast;
                if (EnoughFuelToStart(upgradablePsycast))
                {
                    upgradeStage = UpgradeStage.Active;
                    compRefuelable.ConsumeFuel(upgradablePsycast.upgradeCost);
                    finishUpgradeTick = Find.TickManager.TicksGame + upgradablePsycast.upgradeTime;
                }
            }
            else if (upgradeStage == UpgradeStage.Active)
            {
                if (Find.TickManager.TicksGame >= finishUpgradeTick)
                {
                    upgradeStage = UpgradeStage.NotStarted;
                    var pawn = InnerPawn;
                    pawn.abilities.RemoveAbility(activeUpgradableAbility.def);
                    pawn.abilities.GainAbility(UpgradablePsycast.ability);
                    Building_Casket.EjectContents();
                    activeUpgradableAbility = null;
                }
                else if (compPower != null && !compPower.PowerOn)
                {
                    Interrupt();
                }
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            Interrupt();
            base.PostDestroy(mode, previousMap);
        }
        private void Interrupt()
        {
            if (upgradeStage == UpgradeStage.Active)
            {
                var upgradableDef = UpgradablePsycast;
                var refund = upgradableDef.refundAmountIfInterrupted;
                if (refund.HasValue)
                {
                    var toRefund = ThingMaker.MakeThing(ThingCategoryDef.Named("NeurotrainersPsycast").DescendantThingDefs.RandomElement());
                    toRefund.stackCount = refund.Value;
                    GenSpawn.Spawn(toRefund, this.parent.Position, this.parent.Map);
                }
                if (upgradableDef.inflictHediffOnInterrupt != null)
                {
                    HealthUtility.AdjustSeverity(InnerPawn, upgradableDef.inflictHediffOnInterrupt.hediff, upgradableDef.inflictHediffOnInterrupt.severity);
                }
                upgradeStage = UpgradeStage.NotStarted;
            }
            activeUpgradableAbility = null;
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
            if (base.parent.Faction == Faction.OfPlayer)
            {
                var pawn = InnerPawn;
                if (activeUpgradableAbility != null)
                {
                    Command_Action command_Action = new Command_Action();
                    command_Action.defaultLabel = "PUF.CancelUpgrade".Translate();
                    command_Action.defaultDesc = "PUF.CancelUpgradeDesc".Translate();
                    command_Action.icon = CancelCommandTex;
                    command_Action.hotKey = KeyBindingDefOf.Designator_Cancel;
                    command_Action.action = delegate
                    {
                        Interrupt();
                    };
                    yield return command_Action;
                }
                else if (pawn != null)
                {
                    var upgradePsycastGizmo = new Command_Action
                    {
                        defaultLabel = "PUF.UpgradePsycast".Translate(),
                        defaultDesc = "PUF.UpgradePsycastDesc".Translate(),
                        icon = ContentFinder<Texture2D>.Get("UI/Buttons/UpgradePsycast"),
                        action = delegate
                        {
                            var floatList = new List<FloatMenuOption>();
                            foreach (var ability in GetUpgradableAbilities(pawn))
                            {
                                var upgrades = UpgradesFrom(ability.def).ToList();
                                foreach (var upgrade in upgrades)
                                {
                                    floatList.Add(new FloatMenuOption("PUF.UpgradeFromTo".Translate(ability.def.label, upgrade.ability.label), delegate
                                    {
                                        activeUpgradableAbility = ability;
                                        upgradableIndex = upgrades.IndexOf(upgrade);
                                    }));
                                }
                            }
                            Find.WindowStack.Add(new FloatMenu(floatList));
                        }
                    };
                    if (pawn is null)
                    {
                        upgradePsycastGizmo.Disable("PUF.NoStoredPawn".Translate());
                    }
                    else if (!GetUpgradableAbilities(pawn).Any(ability => UpgradesFrom(ability.def).Any()))
                    {
                        upgradePsycastGizmo.Disable("PUF.NoAbilitiesToUpgrade".Translate(pawn.Named("PAWN")));
                    }

                    yield return upgradePsycastGizmo;
                }

                if (pawn != null)
                {
                    Command_Action command_Action = new Command_Action();
                    command_Action.action = delegate
                    {
                        Interrupt();
                        Building_Casket.EjectContents();
                    };
                    command_Action.defaultLabel = "CommandPodEject".Translate();
                    command_Action.defaultDesc = "CommandPodEjectDesc".Translate();
                    command_Action.icon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject");
                    yield return command_Action;
                }
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref activeUpgradableAbility, "activeUpgradableAbility");
            Scribe_Values.Look(ref upgradableIndex, "upgradableIndex");
            Scribe_Values.Look(ref finishUpgradeTick, "finishUpgradeTick");
            Scribe_Values.Look(ref upgradeStage, "upgradeStage");
        }
    }
}
