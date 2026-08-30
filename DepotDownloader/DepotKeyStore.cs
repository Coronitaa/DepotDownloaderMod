// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Concurrent;
using System.Linq;

namespace DepotDownloader
{
    static class DepotKeyStore
    {
        private static ConcurrentDictionary<uint, byte[]> depotKeysCache = new ConcurrentDictionary<uint, byte[]>();

        public static void AddAll(string[] values)
        {
            foreach (string value in values)
            {
                string[] split = value.Split(';');

                if (split.Length != 2)
                {
                    throw new FormatException($"Invalid depot key line: {value}");
                }

                depotKeysCache[uint.Parse(split[0])] = StringToByteArray(split[1]);
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static bool ContainsKey(uint depotId)
        {
            return depotKeysCache.ContainsKey(depotId);
        }

        public static byte[] Get(uint depotId)
        {
            depotKeysCache.TryGetValue(depotId, out var key);
            return key;
        }

        public static void Add(uint depotId, byte[] key)
        {
            depotKeysCache[depotId] = key;
        }


    }
}
