namespace Everest60Rgb.Protocol
{
    /// <summary>
    /// Everest 60 Lighting Protocol Modes.
    /// The application runs exclusively in <see cref="Custom"/> mode for status indications.
    /// </summary>
    public enum EffectMode : byte
    {
        Custom = 0x07,
        Off    = 0x09
    }
}
