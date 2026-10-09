using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectU.Editor
{
    public static class MortalValidation
    {
        [MenuItem("ProjectU/Validation/Validate Mortal")]
        public static void Run()
        {
            try
            {
                Validate();
                Debug.Log("MORTAL VALIDATION PASSED: damage, attacker, invincibility, lethal damage, event order, duplicate death, null attacker, reentrancy, listener failure.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }

        private static void Validate()
        {
            var root = new GameObject("Mortal Validation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                GameObject attacker = CreateUnit(root.transform, "Attacker").gameObject;
                Mortal target = CreateUnit(root.transform, "Target");
                int damagedCount = 0;
                int deadCount = 0;
                int receivedDamage = 0;
                GameObject damageAttacker = null;
                GameObject deathAttacker = null;
                var eventOrder = new List<string>();
                target.OnDamaged += (damage, source) =>
                {
                    damagedCount++;
                    receivedDamage = damage;
                    damageAttacker = source;
                    eventOrder.Add("Damaged");
                };
                target.OnDead += source =>
                {
                    deadCount++;
                    deathAttacker = source;
                    eventOrder.Add("Dead");
                    Check(target.HP == 0 && target.isDead, "Death state must be set before OnDead.");
                };

                Check(target.HP == 100 && !target.isDead && !target.IsInvincible, "Initial state.");
                target.TakeDamage(0, attacker);
                target.TakeDamage(-1, attacker);
                target.TakeDamage(int.MinValue, attacker);
                target.IsInvincible = true;
                target.TakeDamage(int.MaxValue, attacker);
                Check(target.HP == 100 && damagedCount == 0 && deadCount == 0, "Invalid and invincible damage must be ignored.");

                target.IsInvincible = false;
                target.TakeDamage(25, attacker);
                Check(target.HP == 75 && !target.isDead && damagedCount == 1 && deadCount == 0, "Nonlethal damage.");
                Check(receivedDamage == 25 && damageAttacker == attacker, "Damage event attribution.");

                target.TakeDamage(int.MaxValue, attacker);
                Check(target.HP == 0 && target.isDead && damagedCount == 2 && deadCount == 1, "Lethal damage clamps HP and causes one death.");
                Check(receivedDamage == 75 && deathAttacker == attacker, "Overkill reports actual HP lost and the killer.");
                Check(eventOrder.Count == 3 && eventOrder[1] == "Damaged" && eventOrder[2] == "Dead", "OnDamaged must precede OnDead.");
                target.TakeDamage(10, attacker);
                target.Dead(attacker);
                Check(damagedCount == 2 && deadCount == 1, "Dead units cannot take damage or die again.");

                Mortal environmentTarget = CreateUnit(root.transform, "Environment Target");
                bool nullDamageAttacker = false;
                bool nullDeathAttacker = false;
                environmentTarget.OnDamaged += (damage, source) => nullDamageAttacker = source == null;
                environmentTarget.OnDead += source => nullDeathAttacker = source == null;
                environmentTarget.TakeDamage(100, null);
                Check(nullDamageAttacker && nullDeathAttacker && environmentTarget.isDead, "Environmental damage permits a null attacker.");

                Mortal forcedTarget = CreateUnit(root.transform, "Forced Death Target");
                int forcedDeathCount = 0;
                forcedTarget.OnDead += source => forcedDeathCount++;
                forcedTarget.IsInvincible = true;
                forcedTarget.Dead();
                forcedTarget.Dead();
                Check(forcedTarget.HP == 0 && forcedTarget.isDead && forcedDeathCount == 1, "Direct death is idempotent and bypasses damage immunity.");

                Mortal reentrantTarget = CreateUnit(root.transform, "Reentrant Target");
                int reentrantDamageCount = 0;
                int reentrantDeathCount = 0;
                reentrantTarget.OnDamaged += (damage, source) =>
                {
                    reentrantDamageCount++;
                    reentrantTarget.TakeDamage(1, source);
                };
                reentrantTarget.OnDead += source => reentrantDeathCount++;
                reentrantTarget.TakeDamage(100, attacker);
                Check(reentrantDamageCount == 1 && reentrantDeathCount == 1 && reentrantTarget.isDead, "A lethal damage listener cannot recursively damage a zero-HP target.");

                Mortal failingTarget = CreateUnit(root.transform, "Failing Listener Target");
                var expectedException = new InvalidOperationException("Expected validation listener failure.");
                int failingDeathCount = 0;
                failingTarget.OnDamaged += (damage, source) => throw expectedException;
                failingTarget.OnDead += source => failingDeathCount++;
                bool exceptionPropagated = false;
                try
                {
                    failingTarget.TakeDamage(100, attacker);
                }
                catch (InvalidOperationException exception) when (exception == expectedException)
                {
                    exceptionPropagated = true;
                }
                Check(exceptionPropagated && failingTarget.HP == 0 && failingTarget.isDead && failingDeathCount == 1, "Listener failure must not prevent lethal death processing.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mortal CreateUnit(Transform parent, string name)
        {
            var unit = new GameObject(name);
            unit.transform.SetParent(parent);
            return unit.AddComponent<Mortal>();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }
    }
}
