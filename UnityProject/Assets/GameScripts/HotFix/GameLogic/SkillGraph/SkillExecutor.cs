using System;
using System.IO;
using Fantasy;
using Fantasy.Async;
using Fantasy.Helper;
using GameShared.SkillGraph;
using UnityEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class SkillExecutor : Singleton<SkillExecutor>
    {
        private readonly SkillNodeHandlerRegistry _handlerRegistry = new SkillNodeHandlerRegistry();

        private SkillGraphRunner _runner;
        private ClientSkillRuntimeServices _runtimeServices;

        protected override void OnInit()
        {
            SkillHandlers.RegisterDefaults(_handlerRegistry);
            _runner = new SkillGraphRunner(_handlerRegistry);
            _runtimeServices = new ClientSkillRuntimeServices();
        }

        public async FTask<SkillGraphRunResult> CastSkill(string skillName, SkillContext context)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                throw new ArgumentException("Skill name cannot be empty.", nameof(skillName));

            RuntimeSkillGraph graph;
            try
            {
                graph = await LoadSkillGraph(skillName);
            }
            catch (Exception exception)
            {
                return SkillGraphRunResult.Failure(
                    0,
                    0,
                    $"Failed to load skill graph '{skillName}': {exception.Message}",
                    exception);
            }

            if (graph == null)
            {
                return SkillGraphRunResult.Failure(
                    0,
                    0,
                    $"Failed to load skill graph '{skillName}'.");
            }

            SkillContext runtimeContext = context ?? new SkillContext();
            if (runtimeContext.Runtime == null)
                runtimeContext.Runtime = _runtimeServices;

            return await _runner.Run(graph, runtimeContext);
        }

        private async FTask<RuntimeSkillGraph> LoadSkillGraph(string skillName)
        {
            TextAsset textAsset = await LoadSkillGraphAsset(skillName);
            if (textAsset != null)
            {
                try
                {
                    return textAsset.text.Deserialize<RuntimeSkillGraph>();
                }
                finally
                {
                    GameModule.Resource.UnloadAsset(textAsset);
                }
            }

            string fallbackPath = GetEditorFallbackPath(skillName);
            if (File.Exists(fallbackPath))
            {
                string json = File.ReadAllText(fallbackPath);
                return json.Deserialize<RuntimeSkillGraph>();
            }

            throw new FileNotFoundException($"Skill graph '{skillName}' was not found. Checked resource package and '{fallbackPath}'.");
        }

        private static string GetEditorFallbackPath(string skillName)
        {
            string fileName = skillName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? skillName
                : $"{skillName}.json";
            return Path.Combine(Application.dataPath, "AssetRaw", "Configs", "SkillGraphs", fileName);
        }

        private async FTask<TextAsset> LoadSkillGraphAsset(string skillName)
        {
            return await TryLoadTextAsset(GetSkillGraphLocation(skillName));
        }

        private static string GetSkillGraphLocation(string skillName)
        {
            string normalizedFileName = skillName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? skillName
                : $"{skillName}.json";
            return Path.GetFileNameWithoutExtension(normalizedFileName);
        }

        private static async FTask<TextAsset> TryLoadTextAsset(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || !GameModule.Resource.CheckLocationValid(location))
                return null;

            try
            {
                return await GameModule.Resource.LoadAssetAsync<TextAsset>(location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async FTask<GameObject> LoadSkillPrefabInstance(string prefabLocation)
        {
            GameObject instance = await TryLoadPrefabInstance(prefabLocation);
            if (instance != null)
                return instance;

            throw new FileNotFoundException($"Skill prefab '{prefabLocation}' was not found in the resource package.");
        }

        private static async FTask<GameObject> TryLoadPrefabInstance(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || !GameModule.Resource.CheckLocationValid(location))
                return null;

            try
            {
                return await GameModule.Resource.LoadGameObjectAsync(location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private sealed class ClientSkillRuntimeServices : ISkillRuntimeServices
        {
            public void Log(string message)
            {
                TEngine.Log.Info($"[SkillGraph] {message}");
            }

            public bool TryConsumeStamina(long playerId, int amount)
            {
                return false;
            }

            public async FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null)
            {
                Scene scene = GameClient.Instance.Scene;
                if (scene == null || scene.IsDisposed)
                    throw new InvalidOperationException("GameClient.Scene is not ready for skill graph delay execution.");

                return await FTask.UnityWait(scene, milliseconds, cancellationToken);
            }

            public async FTask<bool> PlayAnimationAsync(
                SkillContext context,
                string prefabLocation,
                float speed,
                FCancellationToken? cancellationToken = null)
            {
                if (cancellationToken != null && cancellationToken.IsCancel)
                    return false;

                GameObject instance = await LoadSkillPrefabInstance(prefabLocation);
                if (instance == null)
                    throw new InvalidOperationException($"Failed to instantiate skill prefab '{prefabLocation}'.");

                if (cancellationToken != null && cancellationToken.IsCancel)
                {
                    UnityEngine.Object.Destroy(instance);
                    return false;
                }

                SkillGraphAnimancerPlayer animancerPlayer = instance.GetComponentInChildren<SkillGraphAnimancerPlayer>(true);
                if (animancerPlayer == null)
                {
                    UnityEngine.Object.Destroy(instance);
                    throw new InvalidOperationException(
                        $"Skill prefab '{prefabLocation}' is missing {nameof(SkillGraphAnimancerPlayer)}.");
                }

                animancerPlayer.Play(speed);
                await FTask.CompletedTask;
                return cancellationToken == null || !cancellationToken.IsCancel;
            }
        }
    }
}
