using BepInEx;
using R2API;
using RoR2;
using SpyMod.Spy.Components;
using SpyMod.Spy.Content;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Networking;

namespace SpyKnifeTweak
{
    [BepInDependency("com.RiskyLives.RiskyMod", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kenko.Spy")]
    [BepInDependency("com.bepis.r2api.damagetype")]
    [BepInPlugin("com.Moffein.SpyKnifeTweak", "SpyKnifeTweak", "1.0.1")]
    public class SpyKnifeTweak : BaseUnityPlugin
    {
        public static bool riskymodFreeze = false;
        public static BuffDef riskymodFreezeDebuff = null;
        public static float riskymodFreezeChampionMult;

        public static float executeGracePeriod = 0.25f;
        public static float executeThreshold = 0.3f;

        private void Awake()
        {
            On.RoR2.HealthComponent.TakeDamage += OverrideKnifeDamage;

            RoR2Application.onLoad += OnLoad;
        }

        private void OnLoad()
        {
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.RiskyLives.RiskyMod"))
            {
                CheckRiskyModFreeze();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private void CheckRiskyModFreeze()
        {
            if (RiskyMod.Tweaks.CharacterMechanics.FreezeChampionExecute.enabled)
            {
                riskymodFreeze = true;
                riskymodFreezeDebuff = RiskyMod.Tweaks.CharacterMechanics.FreezeChampionExecute.FreezeDebuff;
                riskymodFreezeChampionMult = RiskyMod.Tweaks.CharacterMechanics.FreezeChampionExecute.bossExecuteFractionMultiplier;
            }
        }

        private void OverrideKnifeDamage(On.RoR2.HealthComponent.orig_TakeDamage orig, RoR2.HealthComponent self, RoR2.DamageInfo damageInfo)
        {
            bool isBackstab = false;
            CharacterBody attackerBody = null;
            CharacterBody victimBody = self.body;
            if (NetworkServer.active && damageInfo.HasModdedDamageType(DamageTypes.SpyBackStab))
            {
                if (damageInfo.attacker) attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
                if (attackerBody && victimBody)
                {
                    isBackstab = true;
                }
            }

            orig(self, damageInfo);

            if (NetworkServer.active && !damageInfo.rejected && isBackstab && attackerBody && victimBody)
            {
                ExecuteMarker em = self.gameObject.GetComponent<ExecuteMarker>();
                if (!em) em = self.gameObject.AddComponent<ExecuteMarker>();
                em.healthComponent = self;
                em.body = victimBody;
                em.attackerBody = attackerBody;
                em.lifetime = executeGracePeriod;
            }
        }
    }

    //Bad way to do it
    public class ExecuteMarker : MonoBehaviour
    {
        public CharacterBody attackerBody;
        public float lifetime = 0f;
        public HealthComponent healthComponent;
        public CharacterBody body;

        private bool firedExecute = false;

        private void FixedUpdate()
        {
            if (NetworkServer.active && !firedExecute && attackerBody && healthComponent && healthComponent.combinedHealthFraction <= GetExecuteFraction())
            {
                firedExecute = true;
                var damageInfo = new DamageInfo
                {
                    inflictor = attackerBody.gameObject,
                    attacker = attackerBody.gameObject,
                    procCoefficient = 0f,
                    procChainMask = default,
                    damage = healthComponent.combinedHealth,
                    damageType = DamageType.BypassOneShotProtection | DamageType.BypassArmor | DamageType.BypassBlock,
                    crit = false,
                    damageColorIndex = DamageColorIndex.SuperBleed,
                    position = base.transform.position,
                    force = Vector3.zero
                };
                damageInfo.AddModdedDamageType(DamageTypes.SpyExecute);
                healthComponent.TakeDamage(damageInfo);
                Destroy(this);
                return;
            }

            lifetime -= Time.fixedDeltaTime;
            if (lifetime <= 0f)
            {
                Destroy(this);
            }
        }

        public float GetExecuteFraction()
        {
            if (body.bodyFlags.HasFlag(CharacterBody.BodyFlags.ImmuneToExecutes) || body.isPlayerControlled) return 0f;

            float threshold = SpyKnifeTweak.executeThreshold;
            if (body.isChampion) threshold *= 0.5f;

            float freezeFactor = 0.3f;
            if (SpyKnifeTweak.riskymodFreeze)
            {
                freezeFactor *= SpyKnifeTweak.riskymodFreezeChampionMult;
            }

            if (healthComponent.isInFrozenState
                || (SpyKnifeTweak.riskymodFreezeDebuff != null && body.HasBuff(SpyKnifeTweak.riskymodFreezeDebuff)))
            {
                threshold += freezeFactor;
            }

            if (attackerBody)
            {
                if (body.isElite) threshold += attackerBody.executeEliteHealthFraction;
            }

            return threshold;
        }
    }
}
