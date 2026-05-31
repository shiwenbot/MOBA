using Luban;
using GameConfig;
using System.IO;

/// <summary>
/// 配置加载器。
/// </summary>
public class ServerConfigSystem
{
    private static ServerConfigSystem _instance;

    public static ServerConfigSystem Instance => _instance ??= new ServerConfigSystem();

    private bool _init = false;

    private Tables _tables;

    public Tables Tables
    {
        get
        {
            if (!_init)
            {
                Load();
            }

            return _tables;
        }
    }

    /// <summary>
    /// 重新加载配置。
    /// </summary>
    public void Reload()
    {
        if (_init)
        {
            _tables.Reload();
        }
    }

    /// <summary>
    /// 加载配置。
    /// </summary>
    public void Load()
    {
        _tables = new Tables(LoadByteBuf);
        _init = true;
    }

    /// <summary>
    /// 加载二进制配置。
    /// </summary>
    /// <param name="file">FileName</param>
    /// <returns>ByteBuf</returns>
    private ByteBuf LoadByteBuf(string file)
    {
        // 在这里编写服务器加载配置的逻辑
        var configPath = GenerateConfigPath(file);
        var bytes = File.ReadAllBytes(configPath);
        return new ByteBuf(bytes);
    }

    /// <summary>
    /// 生成配置表存放路径。
    /// </summary>
    /// <param name="file">FileName</param>
    /// <returns>configPath</returns>
    private string GenerateConfigPath(string file)
    {
        string relativePath = Path.Combine("GameServer", "GameConfig", "Binary", $"{file}.bytes");
        foreach (string root in EnumerateSearchRoots())
        {
            string candidate = Path.Combine(root, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSearchRoots(AppContext.BaseDirectory, roots);
        AddSearchRoots(Directory.GetCurrentDirectory(), roots);
        return roots;
    }

    private static void AddSearchRoots(string path, ISet<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
        while (current != null)
        {
            roots.Add(current.FullName);
            current = current.Parent;
        }
    }
}
