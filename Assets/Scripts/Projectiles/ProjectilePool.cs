using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectU
{
    public sealed class ProjectilePool : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public Projectile prefab;
            [Min(0)] public int prewarmCount = 16;
            [Min(1)] public int capacity = 64;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        private sealed class Bucket
        {
            public Entry settings;
            public readonly Queue<Projectile> available = new Queue<Projectile>();
            public int count;
        }
        private readonly Dictionary<Projectile, Bucket> buckets = new Dictionary<Projectile, Bucket>();
        private readonly Dictionary<Projectile, Bucket> instances = new Dictionary<Projectile, Bucket>();
        private readonly HashSet<Projectile> rented = new HashSet<Projectile>();
        private Transform inactiveRoot;
        public int ActiveCount => rented.Count;
        public int TotalCount => instances.Count;

        private void Awake()
        {
            var root = new GameObject("Inactive Projectiles");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            inactiveRoot = root.transform;
            foreach (Entry entry in entries)
            {
                if (entry.prefab == null || buckets.ContainsKey(entry.prefab)) continue;
                entry.capacity = Mathf.Max(1, entry.capacity);
                var bucket = new Bucket { settings = entry };
                buckets.Add(entry.prefab, bucket);
                for (int i = 0; i < Mathf.Clamp(entry.prewarmCount, 0, entry.capacity); i++)
                    bucket.available.Enqueue(Create(bucket));
            }
        }

        private Projectile Create(Bucket bucket)
        {
            Projectile instance = Instantiate(bucket.settings.prefab, inactiveRoot);
            instance.gameObject.SetActive(false);
            instance.AttachPool(this);
            instances.Add(instance, bucket);
            bucket.count++;
            return instance;
        }

        public Projectile Spawn(Projectile prefab, GameObject owner, Vector3 position, Vector3 direction)
        {
            if (!isActiveAndEnabled || prefab == null || !buckets.TryGetValue(prefab, out Bucket bucket)) return null;
            Projectile instance;
            if (bucket.available.Count > 0) instance = bucket.available.Dequeue();
            else if (bucket.count < bucket.settings.capacity) instance = Create(bucket);
            else return null;
            rented.Add(instance);
            instance.transform.SetParent(transform, true);
            instance.Launch(owner, position, direction);
            return instance;
        }

        internal void Return(Projectile instance)
        {
            if (!rented.Remove(instance)) return;
            instance.gameObject.SetActive(false);
            // Unity forbids reparenting children while their parent is being disabled.
            if (gameObject.activeInHierarchy) instance.transform.SetParent(inactiveRoot, true);
            instances[instance].available.Enqueue(instance);
        }

        public void ReturnAll()
        {
            foreach (Projectile instance in new List<Projectile>(rented)) Return(instance);
        }

        private void OnDisable() => ReturnAll();
    }
}
