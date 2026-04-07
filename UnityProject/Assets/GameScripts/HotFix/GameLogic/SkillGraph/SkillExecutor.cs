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

        public async FTask CastSkill(string skillName, SkillContext context)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                throw new ArgumentException("Skill name cannot be empty.", nameof(skillName));

            RuntimeSkillGraph graph = await LoadSkillGraph(skillName);
            if (graph == null)
                throw new InvalidOperationException($"Failed to load skill graph '{skillName}'.");

            SkillContext runtimeContext = context ?? new SkillContext();
            if (runtimeContext.Runtime == null)
                runtimeContext.Runtime = _runtimeServices;

            await _runner.Run(graph, runtimeContext);
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
            foreach (string location in GetCandidateLocations(skillName))
            {
                TextAsset textAsset = await TryLoadTextAsset(location);
                if (textAsset != null)
                    return textAsset;
            }

            return null;
        }

        private static string[] GetCandidateLocations(string skillName)
        {
            string normalizedFileName = skillName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? skillName
                : $"{skillName}.json";
            string baseName = Path.GetFileNameWithoutExtension(normalizedFileName);

            return new[]
            {
                baseName,
                normalizedFileName,
                $"SkillGraphs/{baseName}",
                $"SkillGraphs/{normalizedFileName}",
                $"Configs/SkillGraphs/{baseName}",
                $"Configs/SkillGraphs/{normalizedFileName}",
                $"Assets/AssetRaw/Configs/SkillGraphs/{normalizedFileName}"
            };
        }

        private static async FTask<TextAsset> TryLoadTextAsset(string location)
        {
            try
            {
                return await GameModule.Resource.LoadAssetAsync<TextAsset>(location);
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

            public async FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null)
            {
                Scene scene = GameClient.Instance.Scene;
                if (scene == null || scene.IsDisposed)
                    throw new InvalidOperationException("GameClient.Scene is not ready for skill graph delay execution.");

                return await FTask.UnityWait(scene, milliseconds, cancellationToken);
            }
        }
    }
}
