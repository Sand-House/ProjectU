using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class AttackValidation
    {
        private const string Running = "ProjectU.AttackValidation";

        static AttackValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                    new GameObject("Attack Validation Driver").AddComponent<AttackValidationDriver>();
            };
        }

        [MenuItem("ProjectU/Validation/Validate Attacks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run attack validation from Edit mode.");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        internal static void Finish(int code)
        {
            SessionState.SetBool(Running, false);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }

    public sealed class AttackValidationDriver : MonoBehaviour
    {
        private GameObject root;
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private readonly List<string> checks = new List<string>();
        private float originalTimeScale;

        private IEnumerator Start()
        {
            EnemyValidationIsolation.DisableDemoEnemies();
            originalTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            root = new GameObject("Isolated Attack Validation");
            root.transform.position = new Vector3(1000f, 1000f, 1000f);
            IEnumerator validation = Validate();
            while (true)
            {
                bool more;
                object current = null;
                try
                {
                    more = validation.MoveNext();
                    if (more) current = validation.Current;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    Cleanup();
                    AttackValidation.Finish(1);
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            Debug.Log("ATTACK VALIDATION PASSED: " + checks.Count + " checks; " + string.Join(", ", checks));
            Cleanup();
            AttackValidation.Finish(0);
        }

        private IEnumerator Validate()
        {
            Check(AssetDatabase.LoadAssetAtPath<HitscanAttackData>("Assets/Data/Attacks/SoldierRifleAttack.asset") != null &&
                AssetDatabase.LoadAssetAtPath<MeleeAttackData>("Assets/Data/Attacks/StrikerMeleeAttack.asset") != null &&
                AssetDatabase.LoadAssetAtPath<ProjectileAttackData>("Assets/Data/Attacks/SoldierGrenadeAttack.asset").ProjectilePrefab != null,
                "example assets and projectile reference import");
            Check(AssetDatabase.LoadAssetAtPath<HitscanAttackData>("Assets/Data/Attacks/TempPrimaryFire.asset").SpreadAngle == 3f &&
                AssetDatabase.LoadAssetAtPath<HitscanAttackData>("Assets/Data/Attacks/SoldierRifleAttack.asset").SpreadAngle == 3f,
                "saved hitscan assets preserve their three-degree spread angles");
            var bare = new GameObject("Required Components");
            bare.transform.SetParent(root.transform, false);
            bare.AddComponent<HitscanAttack>();
            Check(bare.GetComponent<Combatant>() != null && bare.GetComponent<Mortal>() != null, "inherited component requirements");
            HitscanAttackData rifle = Data<HitscanAttackData>(10f);
            Set(rifle, "windupTime", 0.08f);
            Set(rifle, "recoveryTime", 0.12f);
            Set(rifle, "cooldown", 0.24f);
            Set(rifle, "spreadAngle", 0f);
            Combatant source = Unit("Player", Vector3.zero, Faction.Player);
            Combatant friendly = Unit("Friendly", Vector3.forward, Faction.Player);
            Combatant near = Unit("Near Enemy", Vector3.forward * 3f, Faction.Enemy);
            Combatant far = Unit("Far Enemy", Vector3.forward * 5f, Faction.Enemy);
            AddHitbox(near, Vector3.zero, false); // Two colliders on one unit must count once.
            HitscanAttack attack = Attack<HitscanAttack>(source, rifle);
            Physics.SyncTransforms();
            Check(PlayerActionValidationUtility.For(attack).TryStart(source.transform.position + Vector3.forward * 20f) && !PlayerActionValidationUtility.For(attack).TryStart(), "request accepted once");
            Check(near.Mortal.HP == 100 && PlayerActionValidationUtility.For(attack).Phase == ActionPhase.Windup, "windup has no immediate damage");
            yield return new WaitForSeconds(0.04f);
            Check(near.Mortal.HP == 100, "windup waits in game time");
            yield return new WaitForSeconds(0.08f);
            Check(near.Mortal.HP == 90 && far.Mortal.HP == 100 && friendly.Mortal.HP == 100, "hostile-only hitscan and zero penetration");
            Check(Vector3.Distance(attack.LastShotEndPoint, near.transform.position - Vector3.forward * 0.25f) < 0.001f,
                "debug line skips ally and stops at first target surface with zero penetration");
            Check(PlayerActionValidationUtility.For(attack).CooldownRemaining > 0f && !PlayerActionValidationUtility.For(attack).CanStart && PlayerActionValidationUtility.For(attack).Phase == ActionPhase.Recovery,
                "execution starts cooldown and recovery together");
            PlayerActionValidationUtility.For(attack).enabled = false;
            PlayerActionValidationUtility.For(attack).enabled = true;
            Check(!PlayerActionValidationUtility.For(attack).CanStart, "disable does not reset executed cooldown");
            float remaining = PlayerActionValidationUtility.For(attack).CooldownRemaining;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.06f);
            Check(Mathf.Abs(PlayerActionValidationUtility.For(attack).CooldownRemaining - remaining) < 0.001f && !PlayerActionValidationUtility.For(attack).TryStart(), "pause freezes cooldown and rejects requests");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.3f);
            Check(PlayerActionValidationUtility.For(attack).CanStart, "cooldown resumes");

            Set(rifle, "windupTime", 0f);
            Set(rifle, "enemyPenetrationCount", 1);
            Check(PlayerActionValidationUtility.For(attack).TryStart(), "penetrating hitscan request");
            yield return null;
            Check(near.Mortal.HP == 80 && far.Mortal.HP == 90, "additional enemy penetration and collider deduplication");
            Check(Vector3.Distance(attack.LastShotEndPoint, far.transform.position - Vector3.forward * 0.25f) < 0.001f,
                "debug line passes duplicate hitboxes and stops at penetration limit target surface");
            HitscanAttack twin = Attack<HitscanAttack>(source, rifle);
            Check(!PlayerActionValidationUtility.For(twin).CanStart, "shared owner recovery blocks a different attack");
            yield return new WaitForSeconds(0.13f);
            Check(twin.Data == attack.Data && PlayerActionValidationUtility.For(twin).CanStart && !PlayerActionValidationUtility.For(attack).CanStart, "shared asset has independent runtime state");
            far.gameObject.SetActive(false);
            Physics.SyncTransforms();
            PlayerActionValidationUtility.For(twin).TryStart(source.transform.position + Vector3.forward * 20f);
            yield return new WaitForSeconds(0.01f);
            Check(Vector3.Distance(twin.LastShotEndPoint,
                twin.LastAttackPosition + Vector3.forward * rifle.MaxDistance) < 0.001f,
                "debug line reaches maximum range when fewer targets than penetration limit are hit");
            far.gameObject.SetActive(true);
            Physics.SyncTransforms();

            Combatant enemyShooter = Unit("Enemy Shooter", Vector3.right * 10f, Faction.Enemy);
            Combatant playerTarget = Unit("Player Head", new Vector3(10f, 0f, 3f), Faction.Player);
            playerTarget.gameObject.layer = 2; // Existing player uses Ignore Raycast.
            playerTarget.GetComponent<BoxCollider>().enabled = false;
            AddHitbox(playerTarget, Vector3.zero, true);
            HitscanAttackData critical = Data<HitscanAttackData>(12f);
            Set(critical, "spreadAngle", 0f);
            Set(critical, "canCritical", true);
            HitscanAttack enemyAttack = Attack<HitscanAttack>(enemyShooter, critical);
            PlayerActionValidationUtility.For(enemyAttack).DamageMultiplier = 1.5f;
            Physics.SyncTransforms();
            Check(PlayerActionValidationUtility.For(enemyAttack).TryStart(playerTarget.transform.position), "enemy uses same attack component");
            yield return null;
            Check(playerTarget.Mortal.HP == 64, "trigger headshot, damage multiplier and Ignore Raycast target");
            Check(Vector3.Distance(enemyAttack.LastShotEndPoint, playerTarget.transform.position - Vector3.forward * 0.2f) < 0.001f,
                "debug line stops at target trigger hitbox surface");
            var immune = new DamageContext(enemyShooter, critical);
            playerTarget.Mortal.IsInvincible = true;
            Check(DamageResolver.Apply(immune, playerTarget, true) == 0 && playerTarget.Mortal.HP == 64, "Mortal immunity preserved");
            Check(!enemyShooter.CanTarget(enemyShooter) && !enemyShooter.CanTarget(near) &&
                enemyShooter.CanTarget(near, AttackTargetRule.Friendly), "self exclusion and friendly target rule");
            Set(critical, "damage", float.MaxValue);
            Check(DamageResolver.CalculateDamage(new DamageContext(enemyShooter, critical, float.MaxValue), true) == int.MaxValue, "damage overflow clamps");
            Set(critical, "damage", 1.5f);
            Check(DamageResolver.CalculateDamage(new DamageContext(enemyShooter, critical), false) == 2, "fractional damage rounds once");

            Combatant wallShooter = Unit("Wall Shooter", Vector3.right * 20f, Faction.Player);
            Combatant covered = Unit("Covered", new Vector3(20f, 0f, 5f), Faction.Enemy);
            Box("Wall", new Vector3(20f, 0f, 2f), new Vector3(2f, 2f, 0.2f));
            Box("Ignored Trigger", new Vector3(20f, 0f, 1f), Vector3.one * 0.5f).GetComponent<BoxCollider>().isTrigger = true;
            HitscanAttack wallAttack = Attack<HitscanAttack>(wallShooter, rifle);
            Physics.SyncTransforms();
            PlayerActionValidationUtility.For(wallAttack).TryStart(covered.transform.position);
            yield return null;
            Check(covered.Mortal.HP == 100, "world geometry blocks penetrating hitscan");
            Check(Vector3.Distance(wallAttack.LastShotEndPoint, root.transform.position + new Vector3(20f, 0f, 1.9f)) < 0.001f,
                "debug line ignores non-hitbox triggers and stops at wall surface");

            Combatant spreadShooter = Unit("Spread Shooter", Vector3.right * 30f, Faction.Player);
            HitscanAttackData spread = Data<HitscanAttackData>(1f);
            Set(spread, "spreadStartShot", 3);
            Set(spread, "spreadAngle", 6f);
            Set(spread, "spreadResetDelay", 10f);
            HitscanAttack spreadAttack = Attack<HitscanAttack>(spreadShooter, spread);
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(12345);
            for (int shot = 1; shot <= 3; shot++)
            {
                Check(PlayerActionValidationUtility.For(spreadAttack).TryStart(spreadShooter.transform.position + Vector3.forward * 20f), "spread shot " + shot);
                yield return null;
                float angle = Vector3.Angle(Vector3.forward, spreadAttack.LastShotDirection);
                Check(spreadAttack.ConsecutiveShots == shot && (shot < 3 ? angle < 0.001f : angle > 0.01f && angle <= 6.01f), "spread threshold " + shot);
                Check(Vector3.Distance(spreadAttack.LastShotEndPoint,
                    spreadAttack.LastAttackPosition + spreadAttack.LastShotDirection * spread.MaxDistance) < 0.001f,
                    "missed debug line follows actual spread direction to maximum range " + shot);
            }
            Set(spread, "spreadAngle", 0f);
            bool zeroSpreadAccepted = PlayerActionValidationUtility.For(spreadAttack).TryStart();
            yield return null;
            Check(zeroSpreadAccepted && spreadAttack.ConsecutiveShots == 4 &&
                Vector3.Angle(Vector3.forward, spreadAttack.LastShotDirection) < 0.001f,
                "zero angle disables spread even after the consecutive-shot threshold");
            UnityEngine.Random.state = randomState;
            Set(spread, "spreadResetDelay", 0.04f);
            yield return new WaitForSeconds(0.08f);
            Check(spreadAttack.ConsecutiveShots == 0, "spread resets after idle");

            Combatant meleeSource = Unit("Melee Source", Vector3.right * 40f, Faction.Player);
            Combatant front = Unit("Melee Front", new Vector3(40f, 0f, 1f), Faction.Enemy);
            Combatant side = Unit("Melee Side", new Vector3(41f, 0f, 0f), Faction.Enemy);
            Combatant back = Unit("Melee Back", new Vector3(40f, 0f, -1f), Faction.Enemy);
            Combatant high = Unit("Melee High", new Vector3(40f, 4f, 1f), Faction.Enemy);
            AddHitbox(front, Vector3.zero, true);
            MeleeAttackData melee = Data<MeleeAttackData>(10f);
            Set(melee, "originOffset", Vector3.zero);
            MeleeAttack meleeAttack = Attack<MeleeAttack>(meleeSource, melee);
            Physics.SyncTransforms();
            PlayerActionValidationUtility.For(meleeAttack).TryStart(front.transform.position);
            yield return null;
            Check(front.Mortal.HP == 90 && side.Mortal.HP == 100 && back.Mortal.HP == 100 && high.Mortal.HP == 100, "sector, height and area deduplication");
            Set(melee, "shape", (int)MeleeShape.Circle);
            PlayerActionValidationUtility.For(meleeAttack).TryStart();
            yield return null;
            Check(front.Mortal.HP == 80 && side.Mortal.HP == 90 && back.Mortal.HP == 90 && high.Mortal.HP == 100, "circle attack");

            ProjectileBase bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Bullet.prefab").GetComponent<ProjectileBase>();
            ProjectileBase grenadePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Grenade.prefab").GetComponent<ProjectileBase>();
            ProjectilePool pool = Pool(bulletPrefab, grenadePrefab);
            Combatant projectileSource = Unit("Projectile Source", Vector3.right * 60f, Faction.Player);
            Combatant projectileFriendly = Unit("Projectile Friendly", new Vector3(60f, 0f, 1f), Faction.Player);
            Combatant projectileNear = Unit("Projectile Near", new Vector3(60f, 0f, 2f), Faction.Enemy);
            Combatant projectileFar = Unit("Projectile Far", new Vector3(60f, 0f, 4f), Faction.Enemy);
            Combatant behindWall = Unit("Behind Projectile Wall", new Vector3(60f, 0f, 8f), Faction.Enemy);
            Box("Projectile Wall", new Vector3(60f, 0f, 6f), new Vector3(2f, 2f, 0.3f));
            ProjectileAttackData projectile = Data<ProjectileAttackData>(10f);
            Set(projectile, "projectilePrefab", bulletPrefab);
            Set(projectile, "piercesEnemies", true);
            Set(projectile, "sizeMultiplier", 2f);
            ProjectileAttack projectileAttack = Attack<ProjectileAttack>(projectileSource, projectile);
            projectileAttack.Pool = pool;
            Physics.SyncTransforms();
            PlayerActionValidationUtility.For(projectileAttack).TryStart(projectileSource.transform.position + Vector3.forward * 20f);
            yield return null;
            ProjectileBase shotInstance = projectileAttack.LastProjectile;
            Check(shotInstance != null && shotInstance.HasAttackData &&
                Vector3.Distance(shotInstance.transform.localScale, bulletPrefab.transform.localScale * 2f) < 0.001f, "projectile executes and scales");
            projectileSource.Faction = Faction.Enemy;
            PlayerActionValidationUtility.For(projectileAttack).DamageMultiplier = 3f;
            yield return new WaitForSeconds(0.3f + 6f / bulletPrefab.Speed);
            Check(projectileNear.Mortal.HP == 90 && projectileFar.Mortal.HP == 90 && projectileFriendly.Mortal.HP == 100,
                "projectile piercing, ally passage and launch snapshot");
            Check(!shotInstance.IsActive && behindWall.Mortal.HP == 100, "wall stops piercing projectile");
            Check(!Physics.GetIgnoreCollision(shotInstance.GetComponent<Collider>(), projectileNear.GetComponent<Collider>()), "piercing collision exclusions restored");
            projectileSource.Faction = Faction.Player;
            ProjectileBase legacy = pool.Spawn(bulletPrefab, projectileSource.gameObject, projectileSource.transform.position, Vector3.forward);
            Check(!legacy.HasAttackData && Vector3.Distance(legacy.transform.localScale, bulletPrefab.transform.localScale) < 0.001f, "legacy spawn resets payload and scale");
            legacy.Despawn();
            Set(projectile, "piercesEnemies", false);
            PlayerActionValidationUtility.For(projectileAttack).DamageMultiplier = 1f;
            PlayerActionValidationUtility.For(projectileAttack).TryStart();
            yield return new WaitForSeconds(0.15f + 2f / bulletPrefab.Speed);
            Check(projectileNear.Mortal.HP == 80 && projectileFar.Mortal.HP == 90 && !projectileAttack.LastProjectile.IsActive, "nonpiercing projectile stops at first target");
            ProjectileBase first = pool.Spawn(bulletPrefab, null, root.transform.position + Vector3.up * 100f, Vector3.forward);
            ProjectileBase second = pool.Spawn(bulletPrefab, null, root.transform.position + Vector3.up * 110f, Vector3.forward);
            int failed = 0;
            PlayerActionValidationUtility.For(projectileAttack).OnFailed += () => failed++;
            Check(PlayerActionValidationUtility.For(projectileAttack).TryStart(), "capacity failure accepts windup request");
            yield return null;
            Check(failed == 1 && PlayerActionValidationUtility.For(projectileAttack).CooldownRemaining == 0f && projectileAttack.LastProjectile == null, "pool failure does not claim execution or cooldown");
            first.Despawn();
            second.Despawn();

            Vector3 explosion = root.transform.position + Vector3.right * 80f;
            Combatant explosionInner = Unit("Explosion Inner", new Vector3(80f, 0f, 1f), Faction.Enemy);
            Combatant explosionOuter = Unit("Explosion Outer", new Vector3(80f, 0f, 3f), Faction.Enemy);
            AddHitbox(explosionInner, Vector3.zero, true);
            Set(projectile, "damage", 12f);
            var blastContext = new DamageContext(projectileSource, projectile);
            var blastLaunch = new ProjectileLaunchData(blastContext, false, 2f, 0.5f);
            var grenade = (ProjectileGrenade)pool.Spawn(grenadePrefab, projectileSource.gameObject, explosion, Vector3.forward, blastLaunch);
            Set(grenade, "maxLifeTime", 0.08f);
            grenade.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            grenade.GetComponent<Rigidbody>().useGravity = false;
            Physics.SyncTransforms();
            Check(Mathf.Abs(grenade.ExplosionRadius - 2f) < 0.001f, "explosion radius independent of projectile size");
            yield return new WaitForSeconds(0.4f);
            Check(explosionInner.Mortal.HP == 88 && explosionOuter.Mortal.HP == 100 && !grenade.IsActive, "fuse explosion, scaled area, deduplication and return");
            var reusedGrenade = (ProjectileGrenade)pool.Spawn(grenadePrefab, null, explosion + Vector3.up * 20f, Vector3.forward);
            Check(!reusedGrenade.HasAttackData && Mathf.Abs(reusedGrenade.ExplosionRadius - 4f) < 0.001f, "grenade reuse resets radius multiplier");
            reusedGrenade.Despawn();

            Combatant cancelSource = Unit("Cancel Source", Vector3.right * 100f, Faction.Player);
            HitscanAttackData delayed = Data<HitscanAttackData>(10f);
            Set(delayed, "windupTime", 0.1f);
            HitscanAttack cancel = Attack<HitscanAttack>(cancelSource, delayed);
            int executed = 0;
            int cancelled = 0;
            cancel.OnExecuted += () => executed++;
            PlayerActionValidationUtility.For(cancel).OnCancelled += reason => cancelled++;
            PlayerActionValidationUtility.For(cancel).TryStart();
            PlayerActionValidationUtility.For(cancel).enabled = false;
            PlayerActionValidationUtility.For(cancel).enabled = true;
            yield return new WaitForSeconds(0.12f);
            Check(executed == 0 && cancelled == 1 && PlayerActionValidationUtility.For(cancel).CanStart, "disable cancels pending windup");
            PlayerActionValidationUtility.For(cancel).TryStart();
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.14f);
            Check(executed == 0 && PlayerActionValidationUtility.For(cancel).Phase == ActionPhase.Windup, "pause freezes windup");
            Time.timeScale = 1f;
            cancelSource.Mortal.Dead();
            yield return null;
            Check(executed == 0 && cancelled == 2 && !PlayerActionValidationUtility.For(cancel).CanStart, "death cancels pending attack");

            Combatant positionedSource = Unit("Positioned Hitscan", Vector3.right * 110f, Faction.Player);
            positionedSource.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            positionedSource.transform.localScale = new Vector3(2f, 1f, 2f);
            Combatant positionedTarget = Unit("Offset Target", new Vector3(115f, 1f, 0f), Faction.Enemy);
            Combatant unshiftedTarget = Unit("Unshifted Target", new Vector3(115f, 0f, 0f), Faction.Enemy);
            HitscanAttackData positionedData = Data<HitscanAttackData>(10f);
            Set(positionedData, "spreadAngle", 0f);
            Set(positionedData, "windupTime", 0.05f);
            HitscanAttack positionedAttack = Attack<HitscanAttack>(positionedSource, positionedData);
            Transform positionedMuzzle = Origin(positionedSource, new Vector3(0f, 0.5f, 0.5f));
            Check(PlayerActionValidationUtility.For(positionedAttack).TryStart(positionedMuzzle, positionedTarget.transform.position), "request accepts runtime muzzle transform");
            positionedMuzzle.localPosition = new Vector3(0f, 1f, 1f);
            Vector3 expectedOrigin = root.transform.position + new Vector3(112f, 1f, 0f);
            Check(Vector3.Distance(PlayerActionValidationUtility.For(positionedAttack).WorldAttackPosition, expectedOrigin) < 0.001f,
                "requested muzzle follows local movement character rotation and scale during windup");
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.08f);
            Check(positionedTarget.Mortal.HP == 90 && unshiftedTarget.Mortal.HP == 100,
                "hitscan fires from current muzzle position after windup");
            Check(Vector3.Distance(positionedAttack.LastAttackPosition, expectedOrigin) < 0.001f &&
                Vector3.Distance(positionedAttack.LastShotEndPoint, positionedTarget.transform.position - Vector3.right * 0.25f) < 0.001f,
                "debug line uses runtime muzzle origin and target surface");

            Combatant otherSource = Unit("Shared Data Other Muzzle", Vector3.right * 120f, Faction.Player);
            Transform otherMuzzle = Origin(otherSource, Vector3.up);
            HitscanAttack otherAttack = Attack<HitscanAttack>(otherSource, positionedData);
            PlayerActionValidationUtility.For(otherAttack).TryStart(otherMuzzle, otherMuzzle.position + Vector3.forward * 20f);
            yield return new WaitForSeconds(0.08f);
            Check(otherAttack.Data == positionedAttack.Data &&
                Vector3.Distance(otherAttack.LastAttackPosition, otherMuzzle.position) < 0.001f &&
                Vector3.Distance(positionedAttack.LastAttackPosition, expectedOrigin) < 0.001f,
                "shared attack data keeps independent runtime origins");

            Combatant positionedMeleeSource = Unit("Positioned Melee", Vector3.right * 130f, Faction.Player);
            Combatant positionedMeleeTarget = Unit("Offset Melee Target", new Vector3(130f, 0f, 4f), Faction.Enemy);
            Combatant unshiftedMeleeTarget = Unit("Unshifted Melee Target", new Vector3(130f, 0f, -1f), Faction.Enemy);
            MeleeAttackData positionedMeleeData = Data<MeleeAttackData>(10f);
            Set(positionedMeleeData, "originOffset", Vector3.zero);
            Set(positionedMeleeData, "shape", (int)MeleeShape.Circle);
            MeleeAttack positionedMelee = Attack<MeleeAttack>(positionedMeleeSource, positionedMeleeData);
            Physics.SyncTransforms();
            Vector3 meleePosition = positionedMeleeSource.transform.position + Vector3.forward * 3f;
            PlayerActionValidationUtility.For(positionedMelee).TryStart(meleePosition, positionedMeleeTarget.transform.position);
            yield return null;
            Check(positionedMeleeTarget.Mortal.HP == 90 && unshiftedMeleeTarget.Mortal.HP == 100,
                "melee range starts at caller-supplied world position");

            Combatant angledSource = Unit("Angled Projectile", Vector3.right * 150f, Faction.Player);
            Transform muzzle = Origin(angledSource, new Vector3(0.35f, 0.55f, 0.65f));
            ProjectileAttackData angledData = Data<ProjectileAttackData>(10f);
            Set(angledData, "projectilePrefab", grenadePrefab);
            Set(angledData, "launchAngle", 12f);
            Set(angledData, "windupTime", 0.05f);
            ProjectileAttack angledAttack = Attack<ProjectileAttack>(angledSource, angledData);
            angledAttack.Pool = pool;
            Vector3 firedPosition = Vector3.zero;
            Vector3 firedDirection = Vector3.zero;
            angledAttack.OnExecuted += () =>
            {
                Rigidbody body = angledAttack.LastProjectile.GetComponent<Rigidbody>();
                firedPosition = body.position;
                firedDirection = body.linearVelocity.normalized;
            };
            Vector3 angledAim = muzzle.position + Vector3.forward * 20f;
            PlayerActionValidationUtility.For(angledAttack).TryStart(muzzle, angledAim);
            angledSource.transform.position += Vector3.up * 0.3f;
            angledSource.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            yield return new WaitForSeconds(0.1f);
            Check(Vector3.Distance(firedPosition, muzzle.position) < 0.001f,
                "projectile reads animated muzzle position at execution time");
            Check(Mathf.Abs(Vector3.Angle((angledAim - firedPosition).normalized, firedDirection) - 12f) < 0.01f && firedDirection.y > 0f,
                "projectile data launch angle pitches upward by twelve degrees");
            angledAttack.LastProjectile.Despawn();
            Set(angledData, "launchAngle", 0f);
            Set(angledData, "windupTime", 0f);
            // Aim from the offset origin, so an offset muzzle still converges on the requested point.
            Vector3 aimDirection = new Vector3(-0.2f, 0.1f, 1f).normalized;
            PlayerActionValidationUtility.For(angledAttack).TryStart(muzzle, muzzle.position + aimDirection * 20f);
            yield return null;
            Check(Vector3.Angle(aimDirection, firedDirection) < 0.05f,
                "zero launch angle aims from offset position at requested point");
            angledAttack.LastProjectile.Despawn();

            Combatant missingSource = Unit("Missing Muzzle", Vector3.right * 170f, Faction.Player);
            Transform missingMuzzle = Origin(missingSource, Vector3.up);
            HitscanAttack missingAttack = Attack<HitscanAttack>(missingSource, positionedData);
            int missingFailures = 0;
            int missingExecutions = 0;
            PlayerActionValidationUtility.For(missingAttack).OnFailed += () => missingFailures++;
            missingAttack.OnExecuted += () => missingExecutions++;
            Check(!PlayerActionValidationUtility.For(missingAttack).TryStart((Transform)null, Vector3.forward) &&
                !PlayerActionValidationUtility.For(missingAttack).TryStart(new Vector3(float.NaN, 0f, 0f), Vector3.forward), "invalid runtime origins are rejected");
            PlayerActionValidationUtility.For(missingAttack).TryStart(missingMuzzle, missingMuzzle.position + Vector3.forward * 10f);
            Destroy(missingMuzzle.gameObject);
            yield return new WaitForSeconds(0.08f);
            Check(missingFailures == 1 && missingExecutions == 0 && PlayerActionValidationUtility.For(missingAttack).CooldownRemaining == 0f,
                "lost muzzle during windup fails without firing from a fallback position");

            Transform defaultMuzzle = Origin(missingSource, Vector3.up);
            Set(missingAttack, "origin", defaultMuzzle);
            PlayerAttackActionBase defaultAction = PlayerActionValidationUtility.For(missingAttack);
            Check(defaultAction.TryStart(), "parameterless action uses the Attack's configured default origin");
            defaultMuzzle.localPosition += Vector3.right * 0.3f;
            yield return new WaitForSeconds(0.08f);
            Check(missingExecutions == 1 && Vector3.Distance(missingAttack.LastAttackPosition, defaultMuzzle.position) < 0.001f,
                "Attack default origin follows transform movement during windup");
            Check(defaultAction.TryStart(), "configured default origin accepts another request");
            Destroy(defaultMuzzle.gameObject);
            yield return new WaitForSeconds(0.08f);
            Check(missingFailures == 2 && missingExecutions == 1 && defaultAction.CooldownRemaining == 0f,
                "lost Attack default origin fails without execution or cooldown");
            Check(!defaultAction.CanStart && !defaultAction.CanBuffer,
                "missing Attack default origin rejects both direct and buffered requests");
            Vector3 fixedOrigin = missingSource.transform.position + Vector3.up;
            Check(defaultAction.TryStart(fixedOrigin, fixedOrigin + Vector3.forward * 10f),
                "explicit world position is valid without an Attack default origin");
            yield return new WaitForSeconds(0.08f);
            Check(missingExecutions == 2 && Vector3.Distance(missingAttack.LastAttackPosition, fixedOrigin) < 0.001f,
                "explicit world position still executes after losing the default origin");

            Combatant blockedSource = Unit("Blocked Runtime Muzzle", Vector3.right * 180f, Faction.Player);
            Transform blockedMuzzle = Origin(blockedSource, Vector3.forward * 0.3f);
            GameObject muzzleWall = Box("Muzzle Wall", new Vector3(180f, 0f, 1f), new Vector3(2f, 2f, 0.2f));
            Combatant blockedTarget = Unit("Behind Muzzle Wall", new Vector3(181f, 0f, 4f), Faction.Enemy);
            HitscanAttack blockedAttack = Attack<HitscanAttack>(blockedSource, positionedData);
            PlayerActionValidationUtility.For(blockedAttack).TryStart(blockedMuzzle, blockedTarget.transform.position);
            blockedMuzzle.localPosition = Vector3.forward * 2f;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.08f);
            Check(blockedTarget.Mortal.HP == 100 && blockedAttack.LastAttackPosition.z < muzzleWall.GetComponent<Collider>().bounds.min.z &&
                blockedMuzzle.position.z > muzzleWall.GetComponent<Collider>().bounds.max.z,
                "hitscan origin stays on character side when animated muzzle crosses wall");
            Check(Vector3.Angle(blockedAttack.LastShotDirection, blockedTarget.transform.position - blockedAttack.LastAttackPosition) < 0.01f &&
                Mathf.Abs(blockedAttack.LastShotEndPoint.z - muzzleWall.GetComponent<Collider>().bounds.min.z) < 0.001f,
                "corrected hitscan recalculates aim and debug line stops on intervening wall");
            ProjectileAttackData blockedBulletData = Data<ProjectileAttackData>(10f);
            Set(blockedBulletData, "projectilePrefab", bulletPrefab);
            ProjectileAttack blockedBullet = Attack<ProjectileAttack>(blockedSource, blockedBulletData);
            blockedBullet.Pool = pool;
            Vector3 blockedSpawn = Vector3.zero;
            blockedBullet.OnExecuted += () => blockedSpawn = blockedBullet.LastProjectile.transform.position;
            PlayerActionValidationUtility.For(blockedBullet).TryStart(blockedMuzzle, blockedTarget.transform.position);
            yield return new WaitForSeconds(0.1f);
            Check(blockedTarget.Mortal.HP == 100 && blockedSpawn.z < muzzleWall.GetComponent<Collider>().bounds.min.z &&
                !blockedBullet.LastProjectile.IsActive, "projectile also spawns before wall and returns on impact");

            Combatant lineSource = Unit("Runtime Line", Vector3.right * 200f, Faction.Player);
            lineSource.gameObject.layer = 31;
            HitscanAttackData lineData = Data<HitscanAttackData>(10f);
            Set(lineData, "spreadAngle", 0f);
            Set(lineData, "maxDistance", 10f);
            HitscanAttack lineAttack = Attack<HitscanAttack>(lineSource, lineData);
            Set(lineAttack, "debugLineDuration", 0.5f);
            Set(lineAttack, "debugLineWidth", 0.08f);
            PlayerAttackActionBase lineAction = PlayerActionValidationUtility.For(lineAttack);
            Check(lineAction.TryStart(), "runtime line shot accepted");
            yield return null;
            LineRenderer[] lines = lineAttack.GetComponentsInChildren<LineRenderer>();
            Check(lines.Length == 1 && lines[0].enabled && lines[0].useWorldSpace &&
                Vector3.Distance(lines[0].GetPosition(0), lineAttack.LastAttackPosition) < 0.001f &&
                Vector3.Distance(lines[0].GetPosition(1), lineAttack.LastShotEndPoint) < 0.001f,
                "runtime line matches actual hitscan endpoints");
            Check(lines[0].startColor == Color.yellow && Mathf.Abs(lines[0].widthMultiplier - 0.08f) < 0.001f &&
                lines[0].sharedMaterial.shader == Resources.Load<Shader>("HitscanDebugLine"),
                "runtime line uses configured color width and included shader");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Check(RenderRuntimeLine(lineSource.transform.position + Vector3.forward * 5f),
                    "camera renders yellow trajectory pixels without drawing gizmos");
            Vector3 firstStart = lines[0].GetPosition(0);
            Vector3 firstEnd = lines[0].GetPosition(1);
            lineSource.transform.position += Vector3.right;
            lineSource.transform.rotation = Quaternion.Euler(0f, 15f, 0f);
            Check(lineAction.TryStart(), "second overlapping runtime line shot accepted");
            yield return null;
            lines = lineAttack.GetComponentsInChildren<LineRenderer>();
            Check(lines.Length == 2 && lines[0].enabled && lines[1].enabled &&
                lines[0].GetPosition(0) == firstStart && lines[0].GetPosition(1) == firstEnd,
                "consecutive shots coexist and old trajectory stays fixed when owner moves");
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.55f);
            Check(lines[0].enabled && lines[1].enabled, "pause freezes runtime line lifetime");
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.55f);
            Check(!lines[0].enabled && !lines[1].enabled, "runtime lines expire after configured game time");
            Check(lineAction.TryStart(), "expired runtime line can be reused");
            yield return null;
            Check(lineAttack.GetComponentsInChildren<LineRenderer>().Length == 2 && lines[0].enabled,
                "expired line renderer is reused without allocating another object");
            lineAttack.enabled = false;
            Check(!lines[0].enabled && !lines[1].enabled, "disabling hitscan immediately clears runtime lines");
            lineAttack.enabled = true;
            Set(lineAttack, "drawDebugLine", false);
            Check(lineAction.TryStart(), "debug display disabled still accepts attacks");
            yield return null;
            Check(!lines[0].enabled && !lines[1].enabled, "debug display toggle suppresses runtime lines");
            Set(lineAttack, "drawDebugLine", true);
            Set(lineAttack, "debugLineDuration", 0f);
            bool zeroDurationVisible = false;
            lineAttack.OnExecuted += () => zeroDurationVisible = lines[0].enabled;
            Check(lineAction.TryStart(), "zero duration runtime line shot accepted");
            yield return null;
            yield return null;
            Check(zeroDurationVisible && !lines[0].enabled, "zero duration line displays firing frame then expires");
            Material lineMaterial = lines[0].sharedMaterial;
            Destroy(lineAttack);
            yield return null;
            yield return null;
            Check(lines[0] == null && lines[1] == null && lineMaterial == null,
                "destroying hitscan releases its renderers and runtime material");
        }

        private bool RenderRuntimeLine(Vector3 center)
        {
            var observer = new GameObject("Runtime Line Camera");
            observer.transform.SetParent(root.transform, false);
            Camera camera = observer.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = center + Vector3.up * 10f;
            camera.transform.LookAt(center, Vector3.forward);
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 31;
            var texture = new RenderTexture(256, 256, 24);
            var pixels = new Texture2D(256, 256, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render(); // Camera.Render does not request Editor gizmo rendering.
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                pixels.Apply();
                const string folder = "Logs/HitscanRuntimeLine_20261114";
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(folder + "/RuntimeLine.png", pixels.EncodeToPNG());
                int yellowPixels = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.r > 180 && pixel.g > 180 && pixel.b < 60) yellowPixels++;
                return yellowPixels > 16;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                texture.Release();
                Destroy(texture);
                Destroy(pixels);
                Destroy(observer);
            }
        }

        private Transform Origin(Combatant source, Vector3 localPosition)
        {
            var origin = new GameObject("Muzzle").transform;
            origin.SetParent(source.transform, false);
            origin.localPosition = localPosition;
            return origin;
        }

        private T Data<T>(float damage) where T : AttackDataBase
        {
            var data = ScriptableObject.CreateInstance<T>();
            assets.Add(data);
            Set(data, "damage", damage);
            Set(data, "windupTime", 0f);
            Set(data, "recoveryTime", 0f);
            Set(data, "cooldown", 0f);
            return data;
        }

        private T Attack<T>(Combatant unit, AttackDataBase data) where T : AttackBase
        {
            T attack = unit.gameObject.AddComponent<T>();
            Set(attack, "data", data);
            return attack;
        }

        private Combatant Unit(string name, Vector3 offset, Faction faction)
        {
            GameObject unit = Box(name, offset, Vector3.one * 0.5f);
            Combatant combatant = unit.AddComponent<Combatant>();
            combatant.Faction = faction;
            return combatant;
        }

        private GameObject Box(string name, Vector3 offset, Vector3 size)
        {
            var item = new GameObject(name);
            item.transform.SetParent(root.transform, false);
            item.transform.localPosition = offset;
            item.AddComponent<BoxCollider>().size = size;
            return item;
        }

        private void AddHitbox(Combatant unit, Vector3 offset, bool critical)
        {
            var child = new GameObject("Hitbox") { layer = unit.gameObject.layer };
            child.transform.SetParent(unit.transform, false);
            child.transform.localPosition = offset;
            var collider = child.AddComponent<SphereCollider>();
            collider.radius = 0.2f;
            collider.isTrigger = true;
            Set(child.AddComponent<Hitbox>(), "isCritical", critical);
        }

        private ProjectilePool Pool(params ProjectileBase[] prefabs)
        {
            var item = new GameObject("Validation Pool");
            item.SetActive(false);
            item.transform.SetParent(root.transform, false);
            ProjectilePool pool = item.AddComponent<ProjectilePool>();
            var serialized = new SerializedObject(pool);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
                entry.FindPropertyRelative("prewarmCount").intValue = 1;
                entry.FindPropertyRelative("capacity").intValue = 2;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            item.SetActive(true);
            return pool;
        }

        private static void Set(UnityEngine.Object target, string name, object value)
        { PlayerActionValidationUtility.Set(target, name, value); }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Attack validation failed: " + message);
            checks.Add(message);
        }

        private void Cleanup()
        {
            Time.timeScale = originalTimeScale;
            if (root != null) Destroy(root);
            foreach (ScriptableObject asset in assets) if (asset != null) Destroy(asset);
        }
    }
}
