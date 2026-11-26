namespace CryoUtils.Services.Recording
{
    public sealed class AudioDeviceInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsDefault { get; init; }
    }
}
