using System.Globalization;
using System.IO;
using GameConfig;
using GameConfig.badminton;
using Luban;

string configPath = Path.Combine(
    "D:\\unity\\Tencent\\TEngine",
    "UnityProject",
    "Assets",
    "AssetRaw",
    "Configs",
    "bytes",
    "badminton_tbshuttlecockshottest.bytes");

Tables tables = new Tables(file =>
{
    if (!string.Equals(file, "badminton_tbshuttlecockshottest", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Unexpected config file request: {file}");
    }

    return new ByteBuf(File.ReadAllBytes(configPath));
});

ShuttlecockShotTest firstRecord = tables.TbShuttlecockShotTest.DataList.First();
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Id={firstRecord.Id},Name={firstRecord.Name},HorizontalSpeed={firstRecord.HorizontalSpeed},LaunchAngle={firstRecord.LaunchAngle},Drag={firstRecord.Drag},SpeedBits={BitConverter.SingleToInt32Bits(firstRecord.HorizontalSpeed)},AngleBits={BitConverter.SingleToInt32Bits(firstRecord.LaunchAngle)},DragBits={BitConverter.SingleToInt32Bits(firstRecord.Drag)}"));
