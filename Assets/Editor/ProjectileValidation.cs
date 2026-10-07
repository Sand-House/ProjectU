using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectU.Editor
{
    [InitializeOnLoad]
    public static class ProjectileValidation
    {
        private const string Running = "ProjectU.ProjectileValidation";
        private static ProjectilePool pool;
        private static Projectile bullet;
        private static Projectile grenade;
        private static GameObject owner;
        private static float started;
        private static int phase;

        static ProjectileValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Running, false)) return;
                EditorApplication.update += Tick;
            };
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Tick()
        {
            try
            {
                if (phase == 0)
                {
                    pool = UnityEngine.Object.FindFirstObjectByType<ProjectilePool>();
                    owner = UnityEngine.Object.FindFirstObjectByType<PlayerInputReader>().gameObject;
                    var bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Bullet.prefab").GetComponent<ProjectileBullet>();
                    var grenadePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Grenade.prefab").GetComponent<ProjectileGrenade>();
                    Check(pool.TotalCount == 32, "Prewarm count");
                    bullet = pool.Spawn(bulletPrefab, owner, new Vector3(0, 10, 0), Vector3.forward);
                    grenade = pool.Spawn(grenadePrefab, owner, new Vector3(2, 10, 0), Vector3.forward);
                    Check(Physics.GetIgnoreCollision(bullet.GetComponent<Collider>(), owner.GetComponent<Collider>()), "Owner collision ignored");
                    started = Time.time;
                    phase = 1;
                }
                else if (phase == 1 && Time.time - started > 0.25f)
                {
                    Check(Mathf.Abs(bullet.transform.position.y - 10f) < 0.05f, "Bullet straight flight");
                    Check(grenade.transform.position.y < 9.9f, "Grenade gravity");
                    bullet.Despawn();
                    Check(!Physics.GetIgnoreCollision(bullet.GetComponent<Collider>(), owner.GetComponent<Collider>()), "Owner collision restored");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Bullet.prefab").GetComponent<ProjectileBullet>();
                    // Fire into the arena floor to check collision-driven return.
                    bullet = pool.Spawn(prefab, owner, new Vector3(5, 2, 0), Vector3.down);
                    phase = 2;
                }
                else if (phase == 2 && Time.time - started > 3.2f)
                {
                    Check(!bullet.IsActive, "Bullet collision return");
                    Check(!grenade.IsActive && pool.ActiveCount == 0, "Grenade fuse and return");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectiles/Bullet.prefab").GetComponent<ProjectileBullet>();
                    for (int i = 0; i < 128; i++)
                        Check(pool.Spawn(prefab, owner, new Vector3(i * 2, 100, 0), Vector3.forward) != null, "Pool growth");
                    Check(pool.Spawn(prefab, owner, Vector3.zero, Vector3.forward) == null, "Pool capacity");
                    pool.ReturnAll();
                    Check(pool.ActiveCount == 0, "Return all");
                    var reused = pool.Spawn(prefab, owner, new Vector3(0, 10, 0), Vector3.forward);
                    Check(reused.IsActive && reused.Owner == owner, "Reuse resets state");
                    pool.gameObject.SetActive(false);
                    Check(pool.ActiveCount == 0 && !reused.IsActive, "Pool disable returns active instances");
                    pool.gameObject.SetActive(true);
                    Debug.Log("PROJECTILE VALIDATION PASSED: prewarm, straight flight, gravity, collision return, fuse, owner filtering, capacity, reuse.");
                    Finish(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Running, false);
            EditorApplication.Exit(code);
        }
    }
}


