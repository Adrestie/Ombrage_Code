namespace Ombrage.Abstractions
{
    public interface IModule
    {
        bool IsEnabled { get; }
        bool IsInCooldown { get; }
    }
}
