using System.Globalization;
using GameConfig.badminton;

ShuttlecockShotTest firstRecord = ServerConfigSystem.Instance.Tables.TbShuttlecockShotTest.DataList.First();

Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Id={firstRecord.Id},Name={firstRecord.Name},HorizontalSpeed={firstRecord.HorizontalSpeed},LaunchAngle={firstRecord.LaunchAngle},Drag={firstRecord.Drag},SpeedBits={BitConverter.SingleToInt32Bits(firstRecord.HorizontalSpeed)},AngleBits={BitConverter.SingleToInt32Bits(firstRecord.LaunchAngle)},DragBits={BitConverter.SingleToInt32Bits(firstRecord.Drag)}"));
