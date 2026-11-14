using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ProjectU.Editor
{
    // Attack-slot regression fixtures stay independent of the Soldier demo skills.
    public sealed class PlayerActionAttackProbe : PlayerAttackActionBase
    {
        public PlayerActionSlot ActionSlot { get; set; } = PlayerActionSlot.None;
        public override PlayerActionSlot Slot => ActionSlot;
    }

    // Fixture construction only: legacy test data copies identify timing presets for their actions.
    // These presets are never serialized into AttackData and are not used by runtime code.
    internal static class PlayerActionValidationUtility
    {
        private static readonly string[] timingFields = { "windupTime", "recoveryTime", "cooldown", "repeatWhileHeld" };
        private static readonly Dictionary<AttackDataBase, Dictionary<string, object>> presets = new Dictionary<AttackDataBase, Dictionary<string, object>>();

        // Older generic attack/input regressions exercise a second weapon, not ADS.
        // Replace only their unsaved Play-mode binding; the saved scene keeps ADS.
        public static void UseSecondaryAttackFixture(PlayerCombat combat)
        {
            if (!(combat.GetAction(PlayerActionSlot.SecondaryFire) is SoilderSecondaryFireAction)) return;
            bool wasEnabled = combat.enabled;
            combat.enabled = false;
            ProjectileAttack attack = combat.GetComponents<ProjectileAttack>()
                .Single(item => item.Data != null && item.Data.name == "TempSecondaryFire");
            PlayerAttackActionBase action = Attach(attack, PlayerActionSlot.SecondaryFire, attack.Origin);
            SetDirect(action, "recoveryTime", 0.5f);
            SetDirect(action, "repeatWhileHeld", true);
            SetDirect(combat.GetBinding(PlayerActionSlot.SecondaryFire), "action", action);
            combat.enabled = wasEnabled;
        }

        private static IEnumerable<PlayerAttackActionBase> ActionsFor(AttackDataBase data)
            => UnityEngine.Object.FindObjectsByType<PlayerAttackActionBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(action => action.Attack != null && action.Attack.Data == data);

        public static PlayerAttackActionBase For(AttackBase attack)
        {
            if (attack == null) throw new ArgumentNullException(nameof(attack));
            PlayerAttackActionBase action = attack.GetComponents<PlayerAttackActionBase>().FirstOrDefault(item => item.Attack == attack);
            return action != null ? action : Attach(attack, PlayerActionSlot.PrimaryFire,
                attack.Origin != null ? attack.Origin : attack.transform);
        }

        public static PlayerAttackActionBase Attach(AttackBase attack, PlayerActionSlot slot, Transform origin)
        {
            PlayerAttackActionBase action = attack.GetComponents<PlayerAttackActionBase>()
                .FirstOrDefault(item => item.Slot == slot && (item.Attack == null || item.Attack == attack));
            if (action == null)
            {
                Type type = slot switch
                {
                    PlayerActionSlot.PrimaryFire => typeof(SoilderPrimaryFireAction),
                    PlayerActionSlot.SecondaryFire => typeof(PlayerActionAttackProbe),
                    PlayerActionSlot.Skill1 => typeof(SoilderSkill1Action),
                    PlayerActionSlot.Skill2 => typeof(PlayerActionAttackProbe),
                    PlayerActionSlot.Ultimate => typeof(PlayerActionAttackProbe),
                    _ => throw new ArgumentOutOfRangeException(nameof(slot))
                };
                action = (PlayerAttackActionBase)attack.gameObject.AddComponent(type);
                if (action is PlayerActionAttackProbe probe) probe.ActionSlot = slot;
            }
            SetDirect(action, "attack", attack);
            SetDirect(attack, "origin", origin);
            ApplyPreset(action, attack.Data);
            return action;
        }

        public static void CopyTiming(AttackDataBase source, AttackDataBase destination)
        {
            foreach (string field in timingFields) Set(destination, field, Timing(source, field));
        }

        private static object Timing(AttackDataBase data, string name)
        {
            if (presets.TryGetValue(data, out Dictionary<string, object> values) && values.TryGetValue(name, out object value)) return value;
            PlayerAttackActionBase action = ActionsFor(data).FirstOrDefault();
            if (action != null) return FindField(action.GetType(), name).GetValue(action);
            return name == "repeatWhileHeld" ? (object)false : 0f;
        }

        public static float TimingFloat(AttackDataBase data, string name) => Convert.ToSingle(Timing(data, name));
        public static bool Repeat(AttackDataBase data) => (bool)Timing(data, "repeatWhileHeld");

        private static void ApplyPreset(PlayerAttackActionBase action, AttackDataBase data)
        {
            if (data != null && presets.TryGetValue(data, out Dictionary<string, object> values))
                foreach (KeyValuePair<string, object> value in values) SetDirect(action, value.Key, value.Value);
        }

        public static void Set(object target, string name, object value)
        {
            if (target is AttackDataBase data && timingFields.Contains(name))
            {
                if (!presets.TryGetValue(data, out Dictionary<string, object> values))
                    presets.Add(data, values = new Dictionary<string, object>());
                values[name] = value;
                foreach (PlayerAttackActionBase action in ActionsFor(data)) SetDirect(action, name, value);
                return;
            }
            if (target is PlayerActionBinding binding && name == "attack")
            {
                SetDirect(binding, "action", Attach((AttackBase)value, binding.Slot, ((AttackBase)value).transform));
                return;
            }
            if (target is PlayerActionBinding originBinding && name == "origin")
            {
                SetDirect(originBinding.Attack, "origin", value);
                return;
            }
            if (target is PlayerCombat combat && name == "actionBindings")
            {
                var bindings = new List<PlayerActionBinding>((PlayerActionBinding[])value);
                bindings.AddRange(combat.ActionBindings.Where(item =>
                    (item.Slot == PlayerActionSlot.Dash || item.Slot == PlayerActionSlot.Reload) &&
                    bindings.All(existing => existing.Slot != item.Slot)));
                SetDirect(target, name, bindings.ToArray());
                return;
            }
            if (target is PlayerMotor motor && name == "dashPostDelay")
            {
                SetDirect(motor.GetComponent<SoilderDashAction>(), "recoveryTime", value);
                return;
            }
            if (target is AttackBase attack && name == "data")
            {
                PlayerAttackActionBase[] actions = attack.GetComponents<PlayerAttackActionBase>().Where(item => item.Attack == attack).ToArray();
                if (attack.Data != null && !presets.ContainsKey(attack.Data) && actions.Length > 0)
                {
                    var snapshot = new Dictionary<string, object>();
                    foreach (string field in timingFields) snapshot[field] = FindField(actions[0].GetType(), field).GetValue(actions[0]);
                    presets.Add(attack.Data, snapshot);
                }
                SetDirect(target, name, value);
                foreach (PlayerAttackActionBase action in actions) ApplyPreset(action, (AttackDataBase)value);
                return;
            }
            SetDirect(target, name, value);
        }

        private static void SetDirect(object target, string name, object value)
            => FindField(target.GetType(), name).SetValue(target, value);

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(name);
        }
    }
}
