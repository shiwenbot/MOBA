namespace GameShared.Badminton.Config
{
    public interface IShuttlecockShotConfigProvider
    {
        bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShotDefinition definition);
    }
}
