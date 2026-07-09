using System.Globalization;
using GameConfig.item;

Item firstRecord = ServerConfigSystem.Instance.Tables.TbItem.DataList.First();

Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Id={firstRecord.Id},Name={firstRecord.Name},Price={firstRecord.Price}"));
