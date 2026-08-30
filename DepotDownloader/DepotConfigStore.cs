// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ProtoBuf;

namespace DepotDownloader
{
    [ProtoContract]
    class DepotConfigStore
    {
        [ProtoMember(1)]
        public Dictionary<uint, ulong> InstalledManifestIDs { get; private set; }

        string FileName;

        DepotConfigStore()
        {
            InstalledManifestIDs = [];
        }

        static bool Loaded
        {
            get { return Instance != null; }
        }

        public static DepotConfigStore Instance;

        public static void LoadFromFile(string filename)
        {
            if (Loaded && Instance.FileName == filename)
                return;

            if (File.Exists(filename))
            {
                try
                {
                    using var fs = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                    Instance = Serializer.Deserialize<DepotConfigStore>(ds) ?? new DepotConfigStore();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to load depot config: {0}", ex.Message);
                    Instance = new DepotConfigStore();
                }
            }
            else
            {
                Instance = new DepotConfigStore();
            }

            Instance.FileName = filename;
        }

        public static void Save()
        {
            if (!Loaded || string.IsNullOrWhiteSpace(Instance.FileName))
                return;

            try
            {
                var dir = Path.GetDirectoryName(Instance.FileName);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var fs = File.Open(Instance.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
                using var ds = new DeflateStream(fs, CompressionMode.Compress);
                Serializer.Serialize(ds, Instance);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to save depot config: {0}", ex.Message);
            }
        }
    }
}
