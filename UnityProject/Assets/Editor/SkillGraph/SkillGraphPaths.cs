using System.IO;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.SkillGraph
{
    internal static class SkillGraphPaths
    {
        public const string GraphDataDirectory = "Assets/Editor/SkillGraph/Data";
        public const string RuntimeGraphExportDirectory = "Assets/AssetRaw/Configs/SkillGraphs";

        public static void EnsureGraphDataDirectory()
        {
            string absoluteDirectory = GetAbsoluteGraphDataDirectory();
            if (Directory.Exists(absoluteDirectory))
                return;

            Directory.CreateDirectory(absoluteDirectory);
            AssetDatabase.Refresh();
        }

        public static void EnsureRuntimeGraphExportDirectory()
        {
            string absoluteDirectory = GetAbsoluteRuntimeGraphExportDirectory();
            if (Directory.Exists(absoluteDirectory))
                return;

            Directory.CreateDirectory(absoluteDirectory);
            AssetDatabase.Refresh();
        }

        public static string GetAbsoluteGraphDataDirectory() =>
            Path.GetFullPath(Path.Combine(GetProjectRoot(), GraphDataDirectory));

        public static string GetAbsoluteRuntimeGraphExportDirectory() =>
            Path.GetFullPath(Path.Combine(GetProjectRoot(), RuntimeGraphExportDirectory));

        public static string ToAbsolutePath(string projectRelativePath) =>
            Path.GetFullPath(Path.Combine(GetProjectRoot(), NormalizePath(projectRelativePath)));

        public static string ToProjectRelativePath(string absolutePath) =>
            NormalizePath(FileUtil.GetProjectRelativePath(absolutePath));

        public static string NormalizePath(string path) => string.IsNullOrEmpty(path)
            ? string.Empty
            : path.Replace('\\', '/');

        private static string GetProjectRoot()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot?.FullName ?? string.Empty;
        }
    }
}
