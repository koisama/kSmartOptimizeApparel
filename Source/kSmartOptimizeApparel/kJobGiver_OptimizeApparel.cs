using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse.AI;
using RimWorld;
using UnityEngine;
using Verse;

/*

This is the modified copy of JobGiver_OptimizeApparel
Changes include:
 * Proper temperature curve - uses outdoor temperature for current month and real InsulationCold instead of base value
 * Modified HP curve - pawns won't really mind worn out apparel as long as it's above 60% health
 * Sharp and blunt protection values will use real values as well
 * Apparel optimize check interval is increased and randomized in order to reduce CPU load

*/

namespace kSmartOptimizeApparel
{
    public class kJobGiver_OptimizeApparel
    {
        private const int ApparelOptimizeCheckInterval = 5500;

        private const float ScoreFactorIfNotReplacing = 10f;

        private static float wantedWarmthTemperature = 0.1f;


        private static float MinWarmthToCareAbout = 5f;
        private static float MinScoreGainToCare = 0.05f;
        
        private static float ApparelBaseConstant = 0.1f;
        private static float ArmorBonusConstant = 1.25f;
        private static float SharpConstant = 1f;
        private static float BluntConstant = 0.75f;
        

        private static StringBuilder debugSb;

        private static SimpleCurve coldCurve = new SimpleCurve
        {
            new CurvePoint(-100f, 1f),
            new CurvePoint(0f, 0.1f)
        };

        private static readonly SimpleCurve HitPointsPercentScoreFactorCurve = new SimpleCurve
        {
            new CurvePoint(0f, 0.1f),
            new CurvePoint(0.6f, 0.8f),
            new CurvePoint(1f, 1f)
        };

        private void SetNextOptimizeTick(Pawn pawn)
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + kJobGiver_OptimizeApparel.ApparelOptimizeCheckInterval + Rand.Range(1,5) * 101;
        }

        internal Job TryGiveTerminalJob(Pawn pawn)
        {

            if (pawn.outfits == null)
            {
                Log.ErrorOnce(pawn + " tried to run JobGiver_OptimizeApparel without an OutfitTracker", 5643897);
                return null;
            }
            if (pawn.Faction != Faction.OfColony)
            {
                Log.ErrorOnce("Non-colonist " + pawn + " tried to optimize apparel.", 764323);
                return null;
            }
            if (!DebugViewSettings.debugApparelOptimize)
            {
                if (Find.TickManager.TicksGame < pawn.mindState.nextApparelOptimizeTick)
                {
                    return null;
                }
            }
            else
            {
                kJobGiver_OptimizeApparel.debugSb = new StringBuilder();
                kJobGiver_OptimizeApparel.debugSb.AppendLine(string.Concat(new object[]
                {
                "Scanning for ",
                pawn,
                " at ",
                pawn.Position
                }));
            }            

            Outfit currentOutfit = pawn.outfits.CurrentOutfit;
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                if (!currentOutfit.filter.Allows(wornApparel[i]) && pawn.outfits.forcedHandler.AllowedToAutomaticallyDrop(wornApparel[i]))
                {
                    return new Job(JobDefOf.RemoveApparel, wornApparel[i])
                    {
                        haulDroppedApparel = true
                    };
                }
            }
            Thing thing = null;
            float num = 0f;
            List<Thing> list = Find.ListerThings.ThingsInGroup(ThingRequestGroup.Apparel);
            if (list.Count == 0)
            {
                this.SetNextOptimizeTick(pawn);
                return null;
            }

            //determining temperature curve for current month for current pawn

            //calculating trait offset because there's no way to get comfytemperaturemin without clothes
            List<Trait> traitList = (
                from tr in pawn.story.traits.allTraits
                where tr.CurrentData.statOffsets != null && tr.CurrentData.statOffsets.Any((StatModifier se) => se.stat == StatDefOf.ComfyTemperatureMin)
                select tr
                ).ToList<Trait>();
            float traitAmount = 0f;
            foreach(Trait t in traitList)
            {
                traitAmount += t.CurrentData.statOffsets.First((StatModifier se) => se.stat == StatDefOf.ComfyTemperatureMin).value;
            }

            //normally positive difference between pawns temp and outdoors temp
            wantedWarmthTemperature = traitAmount + pawn.def.GetStatValueAbstract(StatDefOf.ComfyTemperatureMin, null) - GenTemperature.AverageTemperatureAtWorldCoordsForMonth(Find.Map.WorldCoords, GenDate.CurrentMonth);
            wantedWarmthTemperature = (wantedWarmthTemperature == 0f) ? 0.1f : wantedWarmthTemperature;

            if (wantedWarmthTemperature > MinWarmthToCareAbout)
            {
                kJobGiver_OptimizeApparel.coldCurve = new SimpleCurve
                {
                    new CurvePoint(wantedWarmthTemperature * 2, 1f),
                    new CurvePoint(wantedWarmthTemperature, 1f),
                    new CurvePoint(wantedWarmthTemperature * 0.85f, 0.9f),
                    new CurvePoint(wantedWarmthTemperature * 0.50f, 0.4f),
                    new CurvePoint(0f, 0.5f)
                };
            }

            for (int j = 0; j < list.Count; j++)
            {
                Apparel apparel = (Apparel)list[j];
                if (currentOutfit.filter.Allows(apparel))
                {
                    if (Find.SlotGroupManager.SlotGroupAt(apparel.Position) != null)
                    {
                        if (!apparel.IsForbidden(pawn))
                        {
                            float num2 = kJobGiver_OptimizeApparel.ApparelScoreGain(pawn, apparel);
                            if (DebugViewSettings.debugApparelOptimize)
                            {
                                kJobGiver_OptimizeApparel.debugSb.AppendLine(apparel.LabelCap + ": " + num2.ToString("F2"));
                            }
                            if (num2 >= kJobGiver_OptimizeApparel.MinScoreGainToCare && num2 >= num)
                            {
                                if (ApparelUtility.HasPartsToWear(pawn, apparel.def))
                                {
                                    if (pawn.CanReserveAndReach(apparel, PathEndMode.OnCell, pawn.NormalMaxDanger(), 1))
                                    {
                                        thing = apparel;
                                        num = num2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (DebugViewSettings.debugApparelOptimize)
            {
                kJobGiver_OptimizeApparel.debugSb.AppendLine("BEST: " + thing);
                Log.Message(kJobGiver_OptimizeApparel.debugSb.ToString());
                kJobGiver_OptimizeApparel.debugSb = null;
            }
            if (thing == null)
            {
                this.SetNextOptimizeTick(pawn);
                return null;
            }
            return new Job(JobDefOf.Wear, thing);
        }

        public static float ApparelScoreGain(Pawn pawn, Apparel ap)
        {
            if (ap.def == ThingDefOf.Apparel_PersonalShield && pawn.equipment.Primary != null && !pawn.equipment.Primary.def.Verbs[0].MeleeRange)
            {
                return -1000f;
            }
            float num = kJobGiver_OptimizeApparel.ApparelScoreRaw(ap);
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            bool flag = false;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (!ApparelUtility.CanWearTogether(wornApparel[i].def, ap.def))
                {
                    if (!pawn.outfits.forcedHandler.AllowedToAutomaticallyDrop(wornApparel[i]))
                    {
                        return -1000f;
                    }
                    num -= kJobGiver_OptimizeApparel.ApparelScoreRaw(wornApparel[i]);
                    flag = true;
                }
            }
            if (!flag)
            {
                num *= kJobGiver_OptimizeApparel.ScoreFactorIfNotReplacing;
            }
            return num;
        }

        public static float ApparelScoreRaw(Apparel ap)
        {

            //base score
            float score = kJobGiver_OptimizeApparel.ApparelBaseConstant;

            //calculating protection, it also gets a little buff
            float protectionScore =
                ap.GetStatValue(StatDefOf.ArmorRating_Sharp) * kJobGiver_OptimizeApparel.SharpConstant +
                ap.GetStatValue(StatDefOf.ArmorRating_Blunt) * kJobGiver_OptimizeApparel.BluntConstant;
            score += protectionScore * kJobGiver_OptimizeApparel.ArmorBonusConstant;

            //calculating HP
            if (ap.def.useHitPoints)
            {
                float hpPercent = (float)ap.HitPoints / (float)ap.MaxHitPoints;
                score *= kJobGiver_OptimizeApparel.HitPointsPercentScoreFactorCurve.Evaluate(hpPercent);
            }

            //calculating warmth
            if (wantedWarmthTemperature > MinWarmthToCareAbout && ap.def.GetStatValueAbstract(StatDefOf.Insulation_Cold) < 0)
            {
                float warmth = Math.Abs(ap.GetStatValue(StatDefOf.Insulation_Cold, true));
                score *= kJobGiver_OptimizeApparel.coldCurve.Evaluate(warmth);
            }

            return score;
        }
    }
}
