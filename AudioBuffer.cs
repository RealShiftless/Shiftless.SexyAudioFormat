using Shiftless.Common.Mathematics;
using Shiftless.Common.Serialization;
using System.Runtime.InteropServices;

namespace Shiftless.SexyAudioFormat
{
    public sealed class AudioBuffer(uint sampleRate, BitDepth bitDepth, ushort channels, byte[] data)
    {
        // Values
        public readonly uint SampleRate = sampleRate;
        public readonly BitDepth BitDepth = bitDepth;
        public readonly ushort Channels = channels;

        private readonly byte[] _data = data;


        // Properties
        public byte BytesPerSample => (byte)((int)BitDepth / 8);

        public uint Samples => (uint)(_data.Length / Channels / BytesPerSample);

        public int MinValue => -(int)Math.Pow(2, (int)BitDepth - 1);
        public int MaxValue => (int)Math.Pow(2, (int)BitDepth - 1) - 1;

        public ReadOnlySpan<byte> Data => _data;


        // Indexer
        public int this[uint sample, ushort channel] => GetSample(sample, channel);


        // Func
        public void ApplyDitherFilter()
        {
            const short DITHER_THRESH = 4;

            for (ushort channel = 0; channel < Channels; channel++)
            {
                if (GetSample(0, channel) < DITHER_THRESH && GetSample(1, channel) < DITHER_THRESH)
                    SetSample(0, 0, channel);

                for (uint sample = 1; sample < Samples - 1; sample++)
                {
                    if (GetSample(sample, channel) == 0)
                        continue;

                    int a = Math.Abs(GetSample(sample - 1, channel));
                    int b = Math.Abs(GetSample(sample, channel));
                    int c = Math.Abs(GetSample(sample + 1, channel));

                    if (Math.Max(Math.Max(a, b), c) < DITHER_THRESH)
                        SetSample(sample, channel, 0);
                }
            }
        }


        // Getter setters
        public uint GetIndex(uint sample, ushort channel)
        {
            if (sample >= Samples)
                throw new ArgumentOutOfRangeException(nameof(sample));

            if (channel >= Channels)
                throw new ArgumentOutOfRangeException(nameof(channel));

            return (uint)(sample * BytesPerSample * Channels + channel * BytesPerSample);
        }

        public void SetSample(uint sample, ushort channel, int value) => SetSample(GetIndex(sample, channel), value);
        public void SetSample(uint index, int value)
        {
            if (value <= MinValue && value >= MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));

            for (int i = 0; i < BytesPerSample; i++)
                _data[index + i] = (byte)(value >> i * 8 & 0xFF);
        }

        public float GetSampleNormalized(uint sample, ushort channel) => GetSampleNormalized(GetIndex(sample, channel));
        public float GetSampleNormalized(uint index) => MHelp.Map(GetSample(index), MinValue, MaxValue, -1, 1);

        public int GetSample(uint sample, ushort channel) => GetSample(GetIndex(sample, channel));
        public int GetSample(uint index) => BytesPerSample switch
        {
            1 => (sbyte)_data[index],
            2 => ByteConverter.ToInt16(_data, (int)index),
            3 => ByteConverter.ToInt24(_data, (int)index),
            4 => ByteConverter.ToInt32(_data, (int)index),
            _ => throw new Exception($"Invallid bit depth {(int)BitDepth}")
        };


        public int[] GetSamples()
        {
            int[] samples = new int[Samples * Channels];

            for (ushort c = 0; c < Channels; c++)
            {
                for (uint i = 0; i < Samples; i++)
                {
                    uint index = i * Channels + c;
                    samples[index] = GetSample(i, c);
                }
            }

            return samples;
        }

        public int[] GetSamples(ushort channel)
        {
            int[] samples = new int[Samples];

            for (uint i = 0; i < Samples; i++)
                samples[i] = GetSample(i, channel);

            return samples;
        }

        public float[] GetSamplesNormalized()
        {
            float[] samples = new float[Samples * Channels];

            for (ushort c = 0; c < Channels; c++)
            {
                for (uint i = 0; i < Samples; i++)
                {
                    uint index = i * Channels + c;
                    samples[index] = GetSampleNormalized(i, c);
                }
            }

            return samples;
        }

        public GCHandle Pin() => GCHandle.Alloc(_data, GCHandleType.Pinned);
    }
}
