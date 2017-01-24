﻿using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ensage;
using Ensage.Common.Enums;
using Ensage.Common.Extensions;
using Ensage.Common.Menu;
using Ensage.Common.Threading;
using log4net;
using PlaySharp.Toolkit.Logging;
using SharpDX;
using Zaio.Helpers;
using Zaio.Interfaces;

namespace Zaio.Heroes
{
    [Hero(ClassID.CDOTA_Unit_Hero_Lina)]
    internal class Lina : ComboHero
    {
        private static readonly ILog Log = AssemblyLogs.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly string[] SupportedAbilities =
        {
            "lina_dragon_slave",
            "lina_light_strike_array",
            "lina_laguna_blade",
            "item_cyclone"
        };

        private static readonly string[] KillstealAbilities =
        {
            "lina_dragon_slave",
            "lina_laguna_blade"
        };

        public override void OnLoad()
        {
            base.OnLoad();

            var heroMenu = new Menu("Lina", "zaioLinak", false, "npc_dota_hero_lina", true);

            heroMenu.AddItem(new MenuItem("zaioLinaAbilitiesText", "Supported Abilities"));
            var supportedStuff = new MenuItem("zaioLinaAbilities", string.Empty);
            supportedStuff.SetValue(new AbilityToggler(SupportedAbilities.ToDictionary(x => x, y => true)));
            heroMenu.AddItem(supportedStuff);

            heroMenu.AddItem(new MenuItem("zaioLinaKillstealAbilitiesText", "Supported Killsteal Abilities"));
            var supportedKillsteal = new MenuItem("zaioLinaKillstealAbilities", string.Empty);
            supportedKillsteal.SetValue(new AbilityToggler(KillstealAbilities.ToDictionary(x => x, y => true)));
            heroMenu.AddItem(supportedKillsteal);

            ZaioMenu.LoadHeroSettings(heroMenu);
        }

        protected override async Task<bool> Killsteal()
        {
            if (await base.Killsteal())
            {
                return true;
            }

            if (MyHero.IsSilenced())
            {
                return false;
            }

            var ult = MyHero.Spellbook.SpellR;
            if (ult.CanBeCasted())
            {
                var damage = ult.GetAbilityData("damage");
                var hasScepter = MyHero.HasItem(ClassID.CDOTA_Item_UltimateScepter);
                damage *= GetSpellAmp();
                var enemy =
                    ObjectManager.GetEntitiesParallel<Hero>()
                                 .FirstOrDefault(
                                     x =>
                                         x.IsAlive && x.Team != MyHero.Team && !x.IsIllusion && ult.CanBeCasted(x) &&
                                         ult.CanHit(x) && (hasScepter || !x.IsMagicImmune()) &&
                                         x.Health < damage * (hasScepter ? 1 : 1 - x.MagicResistance()) &&
                                         !x.IsLinkensProtected() && !x.CantBeAttacked() && !x.CantBeKilled());
                if (enemy != null)
                {
                    Log.Debug(
                        $"use killsteal ult because enough damage {enemy.Health} <= {damage * (hasScepter ? 1 : 1 - enemy.MagicResistance())} ");
                    ult.UseAbility(enemy);
                    await Await.Delay(GetAbilityDelay(enemy, ult));
                    return true;
                }
            }

            var salve = MyHero.Spellbook.SpellQ;
            if (salve.CanBeCasted())
            {
                var damage = (float) salve.GetDamage(salve.Level - 1);
                damage *= GetSpellAmp();

                var enemy =
                    ObjectManager.GetEntitiesParallel<Hero>()
                                 .FirstOrDefault(
                                     x =>
                                         x.IsAlive && x.Team != MyHero.Team && !x.IsIllusion && salve.CanBeCasted(x) &&
                                         salve.CanHit(x) && !x.IsMagicImmune() &&
                                         x.Health < damage * (1 - x.MagicResistance()) && !x.CantBeAttacked() &&
                                         !x.CantBeKilled());
                if (enemy != null)
                {
                    var castPoint = salve.FindCastPoint();
                    var speed = salve.GetAbilityData("dragon_slave_speed");
                    var time = (castPoint + enemy.Distance2D(MyHero) / speed) * 1000.0f;

                    var predictedPos = Prediction.Prediction.PredictPosition(enemy, (int) time);
                    Log.Debug(
                        $"use killsteal Q because enough damage {enemy.Health} <= {damage * (1.0f - enemy.MagicResistance())} ");
                    salve.UseAbility(predictedPos);

                    await Await.Delay(GetAbilityDelay(enemy, salve));
                    return true;
                }
            }

            return false;
        }

        public override async Task ExecuteComboAsync(Unit target, CancellationToken tk = new CancellationToken())
        {
            var stun = MyHero.Spellbook.SpellW;
            var eulsModifier = target.FindModifier("modifier_eul_cyclone");
            if ((stun.CanBeCasted(target) || eulsModifier != null && stun.CanBeCasted()) && stun.CanHit(target))
            {
                var stunCastpoint = stun.FindCastPoint();
                var delay = stun.GetAbilityData("light_strike_array_delay_time");

                if (eulsModifier != null)
                {
                    Log.Debug($"has euls {eulsModifier.RemainingTime}");
                    if (eulsModifier.RemainingTime < stunCastpoint + delay)
                    {
                        Log.Debug($"using stun on cycloned target");
                        stun.UseAbility(target.NetworkPosition);
                        await Await.Delay(GetAbilityDelay(target, stun), tk);
                    }
                }
                else
                {
                    var disabled = 0.0f;
                    if (target.IsRooted(out disabled) || target.IsStunned(out disabled))
                    {
                        var time = disabled - stunCastpoint - delay;
                        if (time >= 0)
                        {
                            Log.Debug($"using stun on disabled target {time}");
                            stun.UseAbility(target.NetworkPosition);
                            await Await.Delay(GetAbilityDelay(target, stun), tk);
                        }
                        else
                        {
                            var predictedPos = Prediction.Prediction.PredictPosition(target, (int) time * -1000);

                            Log.Debug($"using stun on disabled target {time} with predicted pos {predictedPos}");
                            stun.UseAbility(predictedPos);
                            await Await.Delay(GetAbilityDelay(target, stun), tk);
                        }
                    }
                    else
                    {
                        var euls = MyHero.GetItemById(ItemId.item_cyclone);
                        if (euls != null && euls.CanBeCasted(target))
                        {
                            if (euls.CanHit(target))
                            {
                                Log.Debug($"using euls to disable enemy before stun");
                                euls.UseAbility(target);
                                await Await.Delay(125, tk);
                                return;
                            }
                            // check if we are near the enemy
                            if (!await MoveOrBlinkToEnemy(tk, 250, euls.GetCastRange()))
                            {
                                Log.Debug($"return because of blink and euls ready");
                                return;
                            }
                        }

                        var predictedPos = Prediction.Prediction.PredictPosition(target,
                            (int) ((stunCastpoint + delay) * 1000), true);
                        if (predictedPos != Vector3.Zero)
                        {
                            Log.Debug($"using stun on target with predicted pos {predictedPos}");
                            stun.UseAbility(predictedPos);
                            await Await.Delay(GetAbilityDelay(target, stun), tk);
                        }
                        else
                        {
                            Log.Debug($"Not using stun due to enemy turning!");
                        }
                    }
                }
            }

            await UseItems(tk);

            // make him disabled
            if (await DisableEnemy(tk) == DisabledState.UsedAbilityToDisable)
            {
                Log.Debug($"disabled!");
                // return;
            }

            var q = MyHero.Spellbook.SpellQ;
            if (q.CanBeCasted(target) && q.CanHit(target) && !stun.CanBeCasted() && stun.CanHit(target))
            {
                var castPoint = q.FindCastPoint();
                var speed = q.GetAbilityData("dragon_slave_speed");
                var time = (castPoint + target.Distance2D(MyHero) / speed) * 1000.0f;

                var predictedPos = Prediction.Prediction.PredictPosition(target, (int) time);
                Log.Debug($"using q");
                q.UseAbility(predictedPos);

                await Await.Delay(GetAbilityDelay(target, q), tk);
            }

            var ult = MyHero.Spellbook.SpellR;
            if (ult.CanBeCasted(target) && ult.CanHit(target) && await HasNoLinkens(target, tk))
            {
                if (target.IsHexed() || target.IsStunned() ||
                    (float) target.Health / target.MaximumHealth * (1.0f + target.MagicResistance()) < 0.5f)
                {
                    Log.Debug($"using ult");
                    ult.UseAbility(target);
                    await Await.Delay(GetAbilityDelay(target, ult), tk);
                }
            }

            // check if we are near the enemy
            if (!await MoveOrBlinkToEnemy(tk))
            {
                Log.Debug($"return because of blink");
                return;
            }

            if (ZaioMenu.ShouldUseOrbwalker)
            {
                Orbwalk();
                Log.Debug($"orbwalking");
            }
            else
            {
                MyHero.Attack(target);
                await Await.Delay(125, tk);
            }
        }
    }
}