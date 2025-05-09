using Shiftless.Common.Serialization;
using Shiftless.SexyAudioFormat;

namespace Shiftless.SexyAudioFormat.Serialization
{
    public static partial class Ssaf
    {
        public const int HEADER_SIZE = 4;

        public static AudioBuffer Decode(string path, int threads = 1)
        {
            ByteReader reader = new(path);

            string str = reader.NextString(HEADER_SIZE);
            if (str != "SSAF")
                throw new FileLoadException("Audio file was not of valid SSAF format!");

            // First read all the data n shi
            uint sampleRate = reader.NextUInt32();
            BitDepth bitDepth = (BitDepth)reader.Next();

            int bytesPerSample = (int)bitDepth / 8;

            uint sampleCount = reader.NextUInt32();
            ushort channelCount = reader.NextUInt16();

            byte[] data = new byte[sampleCount * channelCount * bytesPerSample];

            // Now go thru the channels
            for (int c = 0; c < channelCount; c++)
            {
                // First do the channel block
                if(reader.NextString(HEADER_SIZE) != "chan")
                    throw new FileLoadException("Audio file was not of valid SSAF format!");

                uint encodedSamples = reader.NextUInt32();

                // Now the zero run block
                if(reader.NextString(HEADER_SIZE) != "zre ")
                    throw new FileLoadException("Audio file was not of valid SSAF format!");

                uint zreHeaderSize = reader.NextUInt32();
                Queue<(uint offset, uint length)> zrBlockQueue = new();

                for(int i = 0; i < zreHeaderSize; i++)
                {
                    uint offset = reader.NextUInt32();
                    uint length = reader.NextUInt32();

                    zrBlockQueue.Enqueue((offset, length));
                }

                // Aaand the rice block
                if (reader.NextString(HEADER_SIZE) != "rice")
                    throw new FileLoadException("Audio file was not of valid SSAF format!");
                uint riceBlockSize = reader.NextUInt32();

                Queue<int> riceOrderQueue = new();
                for (int i = 0; i < riceBlockSize; i++)
                    riceOrderQueue.Enqueue(reader.NextInt32());

                // Now here we get to the data block
                if (reader.NextString(HEADER_SIZE) != "data")
                    throw new FileLoadException("Audio file was not of valid SSAF format!");

                // Now get the bits so we can de rice
                BitReader bits = new(reader.ReadUntil("chan", false, true));

                int[] zreSamples = new int[encodedSamples];
                for (int block = 0; block < riceBlockSize; block++)
                {
                    int k = riceOrderQueue.Dequeue();
                    uint curStartSample = (uint)block * RICE_BLOCK_SIZE;
                    uint samplesTilEnd = encodedSamples - curStartSample;
                    uint blockSamples = Math.Min(RICE_BLOCK_SIZE, samplesTilEnd);

                    for (int i = 0; i < blockSamples; i++)
                    {
                        int curSample = (int)curStartSample + i;

                        int quotient = 0;
                        while (bits.Read()) // reads 1s until 0
                            quotient++;

                        int remainder = k > 0 ? (int)bits.Read(k) : 0;

                        int value = (quotient << k) | remainder;
                        int decoded = (value >> 1) ^ -(value & 1);

                        zreSamples[curSample] = decoded;
                    }
                }

                // Okay now we do the de zero run
                (uint nextBlock, uint blockLength) = zrBlockQueue.Count > 0 ? zrBlockQueue.Dequeue() : (uint.MaxValue, 0);

                int[] samples = new int[sampleCount];

                int j = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    if (i == nextBlock)
                    {
                        i += (int)blockLength - 1;

                        (nextBlock, blockLength) = zrBlockQueue.Count > 0 ? zrBlockQueue.Dequeue() : (uint.MaxValue, 0);
                        continue;
                    }

                    samples[i] = zreSamples[j++];
                }

                // And now decode the predictive encoding to actual sample data
                for (int i = 2; i < samples.Length; i++)
                {
                    short prediction = (short)(2 * samples[i - 1] - samples[i - 2]);
                    samples[i] = (short)(prediction + samples[i]);
                }

                // Now unpack it into the ting
                for(int i = 0; i < sampleCount; i++)
                {
                    byte[] bytes = ByteConverter.GetBytes(samples[i]);

                    for(int b = 0; b < bytesPerSample; b++)
                        data[i * channelCount * bytesPerSample + c * bytesPerSample + b] = bytes[b];
                }
            }

            return new(sampleRate, bitDepth, channelCount, data);
        }

        /*
        public static short[] Decode(string path)
        {
            // First get the file into a reader
            ByteReader reader = new(File.ReadAllBytes(path));

            // First do the main header
            if (reader.NextHeader() != "SSAF")
                throw new FileLoadException("Audio file was not of valid SSAF format!");

            uint totalSamples = reader.NextUInt32();
            uint encodedSamples = reader.NextUInt32();

            // Do the zero run header
            if (reader.NextHeader() != "zre ")
                throw new FileLoadException("Audio file was not of valid SSAF format!");

            uint zreBlockSize = reader.NextUInt32();

            Queue<(uint offset, uint length)> zrBlockQueue = new();
            for (int i = 0; i < zreBlockSize; i++)
            {
                uint offset = reader.NextUInt32();
                uint length = reader.NextUInt32();

                zrBlockQueue.Enqueue((offset, length));
            }

            // Now do the rice block size
            if (reader.NextHeader() != "rice")
                throw new FileLoadException("Audio file was not of valid SSAF format!");
            uint riceBlockSize = reader.NextUInt32();

            Queue<int> riceOrderQueue = new();
            for (int i = 0; i < riceBlockSize; i++)
                riceOrderQueue.Enqueue(reader.NextInt32());

            // Now here we get to the data block
            if (reader.NextHeader() != "data")
                throw new FileLoadException("Audio file was not of valid SSAF format!");

            // Now get the bits so we can de rice
            BitReader bits = new(reader.Remaining());

            short[] zreSamples = new short[encodedSamples];
            for (int block = 0; block < riceBlockSize; block++)
            {
                int k = riceOrderQueue.Dequeue();
                uint curStartSample = (uint)block * RICE_BLOCK_SIZE;
                uint samplesTilEnd = encodedSamples - curStartSample;
                uint blockSamples = Math.Min(RICE_BLOCK_SIZE, samplesTilEnd);

                for(int i = 0; i < blockSamples; i++)
                {
                    int curSample = (int)curStartSample + i;

                    int quotient = 0;
                    while (bits.Read()) // reads 1s until 0
                        quotient++;

                    int remainder = k > 0 ? (int)bits.Read(k) : 0;

                    int value = (quotient << k) | remainder;
                    short decoded = (short)((value >> 1) ^ -(value & 1));

                    zreSamples[curSample] = decoded;
                }
            }

            // Okay now we do the de zero run
            short[] samples = new short[totalSamples];

            (uint nextBlock, uint blockLength) = zrBlockQueue.Dequeue();

            int j = 0;
            for(int i = 0; i < totalSamples; i++)
            {
                if(i == nextBlock)
                {
                    i += (int)blockLength - 1;

                    (nextBlock, blockLength) = zrBlockQueue.Count > 0 ? zrBlockQueue.Dequeue() : (uint.MaxValue, 0);
                    continue;
                }

                samples[i] = zreSamples[j++];
            }

            // And now decode the predictive encoding to actual sample data
            for(int i = 2; i < samples.Length; i++)
            {
                short prediction = (short)(2 * samples[i - 1] - samples[i - 2]);
                samples[i] = (short)(prediction + samples[i]);
            }

            return samples;
        }
        */
    }
}
