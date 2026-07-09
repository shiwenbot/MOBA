using System.Globalization;
using System.IO;
using GameConfig;
using GameConfig.item;
using Luban;

string configPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "UnityProject",
    "Assets",
    "AssetRaw",
    "Configs",
    "bytes",
    "item_tbitem.bytes");

Tables tables = new Tables(file =>
{
    if (!string.Equals(file, "item_tbitem", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Unexpected config file request: {file}");
    }

    return new ByteBuf(File.ReadAllBytes(configPath));
});

Item firstRecord = tables.TbItem.DataList.First();
Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Id={firstRecord.Id},Name={firstRecord.Name},Price={firstRecord.Price}"));
