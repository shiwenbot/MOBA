using System;
using Luban;
using GameConfig;
using TEngine;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 配置加载器。
/// </summary>
public class ConfigSystem
{
    private static ConfigSystem _instance;

    public static ConfigSystem Instance => _instance ??= new ConfigSystem();

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
    
    private IResourceModule _resourceModule;

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
        TextAsset textAsset = null;
        if (_resourceModule == null)
        {
            _resourceModule = ModuleSystem.GetModule<IResourceModule>();
        }

        if (_resourceModule != null)
        {
            try
            {
                textAsset = _resourceModule.LoadAsset<TextAsset>(file);
            }
#if UNITY_EDITOR
            catch (Exception)
            {
                textAsset = null;
            }
#else
            catch
            {
                throw;
            }
#endif
        }

#if UNITY_EDITOR
        textAsset ??= TryLoadEditorTextAsset(file);
#endif
        if (textAsset == null)
        {
            throw new InvalidOperationException($"Config bytes not found: {file}");
        }

        byte[] bytes = textAsset.bytes;
        return new ByteBuf(bytes);
    }

#if UNITY_EDITOR
    private static TextAsset TryLoadEditorTextAsset(string file)
    {
        string assetPath = $"Assets/AssetRaw/Configs/bytes/{file}.bytes";
        return AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
    }
#endif
}
