using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AYellowpaper.SerializedCollections;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace ProjectU.Editor
{
    public static class StageDataDictionaryValidation
    {
        private const string FixturePath = "Assets/Editor/StageDataDictionaryValidation.Temp.asset";

        [MenuItem("ProjectU/Validation/Validate Stage Dictionary Data")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            if (AssetDatabase.LoadMainAssetAtPath(FixturePath) != null) throw new InvalidOperationException("Validation fixture path is occupied.");
            int checks = 0;
            Action<bool, string> check = (success, label) =>
            {
                if (!success) throw new InvalidOperationException("STAGE DICTIONARY CHECK FAILED: " + label);
                checks++; Debug.Log("STAGE DICTIONARY CHECK PASSED: " + label);
            };
            StageData fixture = ScriptableObject.CreateInstance<StageData>();
            bool saved = false;
            try
            {
                check(!fixture.TryValidate(out _), "empty stage rejected");
                SetWaves(fixture, null);
                check(!fixture.TryValidate(out _), "null wave array rejected");
                SetWaves(fixture, new WaveData[] { null });
                check(!fixture.TryValidate(out _), "missing wave rejected");
                check(Group(3).TryValidate(out _, out int count) && count == 3, "single type and quantity");
                var mixed = new EnemySpawnGroup(new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, 3 }, { EnemyType.Gunner, 2 } });
                check(mixed.TryValidate(out _, out count) && count == 5, "mixed dictionary count");
                check(!new EnemySpawnGroup().TryValidate(out _, out _), "empty dictionary rejected");
                check(!new EnemySpawnGroup(null).TryValidate(out _, out _), "null dictionary rejected");
                check(!Group(0).TryValidate(out _, out _), "zero quantity rejected");
                check(!Group(-1).TryValidate(out _, out _), "negative quantity rejected");
                check(!new EnemySpawnGroup(new SerializedDictionary<EnemyType, int> { { (EnemyType)999, 1 } }).TryValidate(out _, out _), "undefined enum rejected");
                check(!new EnemySpawnGroup(new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, int.MaxValue }, { EnemyType.Gunner, 1 } }).TryValidate(out _, out _),
                    "group sum overflow rejected");
                check(Group(int.MaxValue).TryValidate(out _, out count) && count == int.MaxValue, "integer boundary represented exactly");

                var source = new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, 3 } };
                EnemySpawnGroup copied = new EnemySpawnGroup(source);
                source[EnemyType.TempEnemy] = 9;
                check(copied.Enemies[EnemyType.TempEnemy] == 3, "group copies caller dictionary");
                check(new EnemySpawnGroup(new Dictionary<EnemyType, int> { { EnemyType.TempEnemy, 2 } }).TryValidate(out _, out count) && count == 2,
                    "ordinary read-only dictionary input supported");
                source.AddConflictAllowed(EnemyType.TempEnemy, 7);
                var duplicate = new EnemySpawnGroup(source);
                check(!duplicate.TryValidate(out string reason, out _) && reason.Contains("Duplicate"), "hidden serialized duplicate rejected");
                check(!new WaveData(0, duplicate).TryValidate(true, out _), "wave copy preserves invalid conflict rows");
                check(source.Count == 1, "package dictionary enumeration alone would hide the duplicate");
                check(new WaveData(0, Group(1)).TryValidate(false, out _), "one group accepted");
                check(new WaveData(0, Group(1), Group(1), Group(1), Group(1)).TryValidate(false, out _), "four groups accepted");
                check(!new WaveData(0).TryValidate(false, out _), "empty wave rejected");
                check(!new WaveData(0, (EnemySpawnGroup[])null).TryValidate(false, out _), "null groups rejected");
                check(!new WaveData(0, new EnemySpawnGroup[] { null }).TryValidate(false, out _), "missing group rejected");
                check(!new WaveData(0, Group(1), Group(1), Group(1), Group(1), Group(1)).TryValidate(false, out _), "five groups rejected");
                check(!new WaveData(-1, Group(2)).TryValidate(false, out _), "negative transition threshold rejected");
                check(!new WaveData(2, Group(2)).TryValidate(false, out _), "threshold equal to wave count rejected");
                check(!new WaveData(3, Group(2)).TryValidate(false, out _), "threshold above wave count rejected");
                check(new WaveData(1, Group(2)).TryValidate(false, out _), "valid transition threshold accepted");
                check(new WaveData(999, Group(2)).TryValidate(true, out _), "final threshold ignored");
                check(!new WaveData(0, Group(int.MaxValue), Group(1)).TryValidate(true, out _), "wave sum overflow rejected");
                check(new WaveData(0, Group(1), Group(1)).TryValidate(false, out _), "same type in different groups allowed");
                var originalGroup = Group(3);
                var waveCopy = new WaveData(0, originalGroup);
                ((SerializedDictionary<EnemyType, int>)originalGroup.Enemies)[EnemyType.TempEnemy] = 9;
                check(waveCopy.SpawnGroups[0].Enemies[EnemyType.TempEnemy] == 3, "wave copy is independent");

                SetWaves(fixture, new WaveData(1, mixed), new WaveData(999, Group(2)));
                check(fixture.TryValidate(out _), "complete stage validates");
                AssetDatabase.CreateAsset(fixture, FixturePath); saved = true;
                string guid = AssetDatabase.AssetPathToGUID(FixturePath);
                AssetDatabase.SaveAssets();
                Resources.UnloadAsset(fixture);
                fixture = AssetDatabase.LoadAssetAtPath<StageData>(FixturePath);
                check(fixture.TryValidate(out _) && fixture.Waves.Count == 2 && fixture.Waves[0].SpawnGroups[0].Enemies.Count == 2 &&
                    fixture.Waves[0].SpawnGroups[0].Enemies[EnemyType.TempEnemy] == 3 && fixture.Waves[0].SpawnGroups[0].Enemies[EnemyType.Gunner] == 2,
                    "nested stage dictionaries survive native asset save and reload");
                check(AssetDatabase.AssetPathToGUID(FixturePath) == guid, "asset GUID preserved across reload");

                using (var serialized = new SerializedObject(fixture))
                {
                    SerializedProperty rows = serialized.FindProperty("waves").GetArrayElementAtIndex(0).FindPropertyRelative("spawnGroups")
                        .GetArrayElementAtIndex(0).FindPropertyRelative("enemies").FindPropertyRelative("_serializedList");
                    check(rows != null && rows.arraySize == 2, "nested Inspector dictionary rows are available");
                    rows.GetArrayElementAtIndex(0).FindPropertyRelative("Value").intValue = 6;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    check(fixture.TryValidate(out _) && fixture.Waves[0].SpawnGroups[0].Enemies[EnemyType.TempEnemy] == 6, "Inspector quantity change reaches runtime dictionary");
                    rows.arraySize = 3;
                    rows.GetArrayElementAtIndex(2).FindPropertyRelative("Key").intValue = (int)EnemyType.TempEnemy;
                    rows.GetArrayElementAtIndex(2).FindPropertyRelative("Value").intValue = 50;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    check(!fixture.TryValidate(out reason) && reason.Contains("Duplicate"), "Inspector duplicate fails stage validation");
                    rows.arraySize = 2;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    check(fixture.TryValidate(out _), "removing Inspector duplicate restores validity");
                }

                StageData migrated = AssetDatabase.LoadAssetAtPath<StageData>(StageSetup.StagePath);
                check(migrated != null && migrated.TryValidate(out _) && migrated.Waves.Count == 3, "migrated TempStage loads and validates");
                for (int index = 0; index < 3; index++)
                {
                    WaveData wave = migrated.Waves[index];
                    bool correct = wave.SpawnGroups.Count == index + 2 && wave.NextWaveRemainingEnemyThreshold == (index < 2 ? 1 : 0);
                    foreach (EnemySpawnGroup group in wave.SpawnGroups) correct &= group.Enemies.Count == 1 && group.Enemies[EnemyType.TempEnemy] == 2;
                    check(correct, "migrated wave " + (index + 1) + " retains groups types counts and threshold");
                }
                Debug.Log("STAGE DICTIONARY VALIDATION PASSED: " + checks + " checks.");
            }
            finally
            {
                if (saved) AssetDatabase.DeleteAsset(FixturePath);
                else if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        private static EnemySpawnGroup Group(int count) => new EnemySpawnGroup(new SerializedDictionary<EnemyType, int> { { EnemyType.TempEnemy, count } });
        private static void SetWaves(StageData data, params WaveData[] waves)
            => typeof(StageData).GetField("waves", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(data, waves);

        public static void CompilePlayerScripts()
        {
            const string output = "Logs/DictionaryMigration_20261105/PlayerAssemblies";
            Directory.CreateDirectory(output);
            var settings = new ScriptCompilationSettings
            {
                target = BuildTarget.StandaloneWindows64,
                group = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = ScriptCompilationOptions.None
            };
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, output);
            if (result.assemblies == null || !result.assemblies.Any(path => Path.GetFileName(path) == "Assembly-CSharp.dll") ||
                !result.assemblies.Any(path => Path.GetFileName(path) == "AYellowpaper.SerializedCollections.dll"))
                throw new InvalidOperationException("Player script compilation did not produce the project and dictionary assemblies.");
            result.typeDB.Dispose();
            Debug.Log("STAGE DICTIONARY PLAYER SCRIPT COMPILATION PASSED: Windows 64-bit, without UNITY_EDITOR.");
        }
    }
}
