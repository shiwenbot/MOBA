using System;
using GameShared.Badminton.Config;
using UnityEditor;
using UnityEngine;

public static class ShuttlecockConfigValidationEditor
{
    [MenuItem("TEngine/Badminton/Validate Shuttlecock Config", priority = 210)]
    private static void ValidateFromMenu()
    {
        ValidateAndLog();
    }

    public static void ValidateFromCommandLine()
    {
        ValidateAndLog();
        EditorApplication.Exit(0);
    }

    private static void ValidateAndLog()
    {
        var testShot = ConfigSystem.Instance.Tables.TbShuttlecockShotTest[1];
        if (testShot == null || testShot.Name != "TestClear")
        {
            throw new InvalidOperationException("TbShuttlecockShotTest[1] 校验失败。");
        }

        var clear = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.Clear);
        var drop = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.Drop);
        var smash = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.Smash);
        var drive = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.Drive);
        var netShot = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.NetShot);
        var lift = ShuttlecockShotConfigProvider.Get(ShuttlecockShotType.Lift);

        ValidateShot(clear, ShuttlecockShotType.Clear, "Clear", 18f, 55f);
        ValidateShot(drop, ShuttlecockShotType.Drop, "Drop", 8f, 35f);
        ValidateShot(smash, ShuttlecockShotType.Smash, "Smash", 22f, 15f);
        ValidateShot(drive, ShuttlecockShotType.Drive, "Drive", 16f, 8f);
        ValidateShot(netShot, ShuttlecockShotType.NetShot, "NetShot", 5f, 20f);
        ValidateShot(lift, ShuttlecockShotType.Lift, "Lift", 12f, 65f);

        Debug.Log(
            $"[Badminton] Luban config validation passed. test={testShot.Name}, formalCount={ShuttlecockShotConfigProvider.Table.DataList.Count}");
    }

    private static void ValidateShot(
        GameConfig.badminton.ShuttlecockShot shot,
        ShuttlecockShotType expectedType,
        string expectedName,
        float expectedHorizontalSpeed,
        float expectedLaunchAngle)
    {
        if (shot == null)
        {
            throw new InvalidOperationException($"球路配置缺失: {expectedType}");
        }

        if (shot.Id != (int)expectedType)
        {
            throw new InvalidOperationException($"球路 Id 不匹配: expected={(int)expectedType}, actual={shot.Id}");
        }

        if (!string.Equals(shot.Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"球路名称不匹配: expected={expectedName}, actual={shot.Name}");
        }

        if (!Mathf.Approximately(shot.HorizontalSpeed, expectedHorizontalSpeed) ||
            !Mathf.Approximately(shot.LaunchAngle, expectedLaunchAngle))
        {
            throw new InvalidOperationException(
                $"球路参数不匹配: type={expectedType}, speed={shot.HorizontalSpeed}, angle={shot.LaunchAngle}");
        }
    }
}
