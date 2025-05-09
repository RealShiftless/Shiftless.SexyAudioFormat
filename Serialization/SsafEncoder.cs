using Shiftless.Common.Serialization;
using Shiftless.SexyAudioFormat;

namespace Shiftless.SexyAudioFormat.Serialization
{
    public static partial class Ssaf
    {
        public static byte[] Encode(AudioBuffer buffer)
        {
            // First check if the file is actually a wav
            buffer.ApplyDitherFilter();

            // Create the writer
            ByteWriter writer = new();

            writer.Write("SSAF", null);
            writer.Write(buffer.SampleRate);
            writer.Write((byte)buffer.BitDepth);
            writer.Write(buffer.Samples);
            writer.Write(buffer.Channels);

            // Go over each channel seperately
            for (ushort c = 0; c < buffer.Channels; c++)
            {
                // Get the samples for this channel
                int[] samples = buffer.GetSamples(c);

                // Compress it
                samples = ApplyPredictiveEncoding(samples);
                ((uint offset, uint length)[] zreHeader, samples) = ApplyZeroRunEncoding(samples);
                (int[] riceHeader, byte[] ricedSamples) = ApplyRiceEncoding(samples);

                // Now write the data
                writer.Write("chan", null);
                writer.Write((uint)samples.Length);

                writer.Write("zre ", null);
                writer.Write((uint)zreHeader.Length);
                foreach ((uint offset, uint length) in zreHeader)
                {
                    writer.Write(offset);
                    writer.Write(length);
                }

                writer.Write("rice", null);
                writer.Write((uint)riceHeader.Length);
                writer.Write(riceHeader);

                writer.Write("data", null);
                writer.Write(ricedSamples);
            }

            return writer.ToArray();
        }

        private static int[] ApplyPredictiveEncoding(int[] samples)
        {
            int[] residuals = new int[samples.Length];

            for (int i = 0; i < 2; i++)
                residuals[i] = samples[i];

            for (int i = 2; i < samples.Length; i++)
            {
                int curSample = samples[i];

                int prediction = 2 * samples[i - 1] - samples[i - 2];

                int deltaSample = (short)(curSample - prediction);
                residuals[i] = deltaSample;
            }

            return residuals;
        }

        private static ((uint, uint)[] header, int[] samples) ApplyZeroRunEncoding(int[] samples)
        {
            const uint MIN_BLOCK_LENGTH = 10;

            // First make some structures to store the data
            List<(uint, uint)> header = [];
            List<int> compressedSamples = [];

            // Make some state tracking stuff
            uint startIndex = 0;
            uint curLength = 0;

            // Loop thruy all samples
            for(int i = 0; i < samples.Length; i++)
            {
                // So if its 0, collect it and continue
                if (Math.Abs(samples[i]) == 0)
                {
                    if (curLength == 0)
                        startIndex = (uint)i;

                    curLength++;
                    continue;
                }

                // If we get here it means we either have to store a block, print out the 0's and add the current sample
                else if (curLength >= MIN_BLOCK_LENGTH)
                {
                    header.Add((startIndex, curLength));
                }
                else if (curLength > 0)
                {
                    for (int j = 0; j < curLength; j++)
                        compressedSamples.Add(0);
                }

                compressedSamples.Add(samples[i]);
                curLength = 0;
            }

            // Now we need to check if we are currently in a 0 run block
            if (curLength > 0)
            {
                // If the block isn't long enough to store as a compressed block just add the 0's
                if (curLength < MIN_BLOCK_LENGTH)
                    for (int j = 0; j < curLength; j++)
                        compressedSamples.Add(0);

                // Otherwise we store a compressed block
                else
                    header.Add((startIndex, curLength));
            }

            return (header.ToArray(), compressedSamples.ToArray());
        }

        private static (int[] header, byte[] samples) ApplyRiceEncoding(int[] samples)
        {
            int blocks = (int)Math.Ceiling((float)samples.Length / RICE_BLOCK_SIZE);

            List<int> header = [];
            List<bool> bits = [];
            for (int i = 0; i < blocks; i++)
            {
                (int k, uint[] blockSamples) = GetBlock(samples, i * RICE_BLOCK_SIZE);
                // So first I zigzag encode the sample

                header.Add(k);
                foreach (ushort zigzagSample in blockSamples)
                {
                    // Calculate quotient and remainder
                    int quotient = zigzagSample >> k;
                    int remainder = zigzagSample & ((1 << k) - 1);

                    // Unary: quotient 1s followed by a 0
                    for (int q = 0; q < quotient; q++)
                        bits.Add(true); // 1s
                    bits.Add(false); // End of unary = 0

                    // Binary: remainder in k bits
                    for (int b = k - 1; b >= 0; b--)
                    {
                        bool bit = ((remainder >> b) & 1) != 0;
                        bits.Add(bit);
                    }
                }

            }

            int bytesLength = (int)Math.Ceiling(bits.Count / 8f);
            byte[] encodedSamples = new byte[bytesLength];

            for (int i = 0; i < bytesLength; i++)
            {
                byte value = 0;

                int j = 0;
                while (j < 8)
                {
                    if (bits[i * 8 + j])
                        value |= (byte)(1 << (7 - j));

                    j++;

                    if (i * 8 + j >= bits.Count)
                        break;
                }

                encodedSamples[i] = value;
            }

            return (header.ToArray(), encodedSamples.ToArray());
        }

        private static (int, uint[]) GetBlock(int[] samples, int offset)
        {
            int blockSize = Math.Min(RICE_BLOCK_SIZE, samples.Length - offset);

            uint[] zigzaggedSamples = new uint[blockSize];
            float sum = 0;
            for (int i = 0; i < blockSize; i++)
            {
                int sample = samples[offset + i];

                uint zigzag = (uint)((sample << 1) ^ (sample >> 15));
                zigzaggedSamples[i] = zigzag;

                sum += zigzag;
            }

            float avg = sum / blockSize;

            int k = (int)Math.Floor(Math.Log(avg + 1, 2));
            return (k, zigzaggedSamples);
        }
    }
}
