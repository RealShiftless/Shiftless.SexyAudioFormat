namespace Shiftless.SexyAudioFormat.Serialization
{
    public static partial class Ssaf
    {
        public const int RICE_BLOCK_SIZE = 4096;

        public static bool IsValidBitDepth(int value) => value switch
        {
            (int)BitDepth.B16 => true,
            _ => false
        };
    }
}
