using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XylarBedrock.UpdateProcessor.Classes;
using XylarBedrock.UpdateProcessor.Extensions;
using XylarBedrock.UpdateProcessor.Interfaces;
using XylarBedrock.UpdateProcessor.Enums;

namespace XylarBedrock.UpdateProcessor.Databases
{
    public class VersionJsonDb : IVersionDb
    {
        public List<VersionInfoJson> list { get; private set; } = new List<VersionInfoJson>();

        private void SortVersions()
        {
            list.Sort();
            list.Reverse();
        }

        #region Read / Write

        public void ReadJson(string filePath, Dictionary<Guid, string> architectures = null)
        {
            using (var reader = File.OpenText(filePath))
            {
                var data = reader.ReadToEnd();
                PraseJson(data, architectures);
            }
        }

        public void WriteJson(string filePath)
        {
            Save(filePath);
        }

        public void PraseJson(string json, Dictionary<Guid, string> architectures)
        {
            JArray data = JArray.Parse(json);
            var lista = data.ToList();
            lista.Reverse();
            foreach (JArray o in lista)
            {
                string name = o[0].Value<string>();
                string uuid = o[1].Value<string>();
                int type = o[2].Value<int>();
                string arch = o.Count() >= 4 ? o[3].Value<string>() : VersionDbExtensions.FallbackArch;
                int revisionNumber = o.Count() >= 5 ? o[4].Value<int>() : 1;
                var v = new VersionInfoJson(name, uuid, (VersionType)type, arch, revisionNumber);

                if (arch == VersionDbExtensions.FallbackArch && architectures != null && architectures.ContainsKey(v.uuid))
                {
                    v = new VersionInfoJson(name, uuid, (VersionType)type, architectures[v.uuid], revisionNumber);
                }

                int sameVersionIndex = list.FindIndex(x =>
                    x.version == v.version &&
                    x.architecture == v.architecture &&
                    x.type == v.type);

                if (sameVersionIndex >= 0)
                {
                    if (list[sameVersionIndex].revisionNumber < v.revisionNumber)
                    {
                        list[sameVersionIndex] = v;
                    }
                    continue;
                }

                if (!list.Exists(x => x.GetIdentityKey() == v.GetIdentityKey()))
                {
                    list.Add(v);
                }
            }

            SortVersions();
        }

        #endregion

        #region IVersionDb Implements

        public void AddVersion(List<UpdateInfo> u, VersionType type)
        {
            if (u == null || u.Count == 0) return;

            foreach (var v in u)
            {
                string version = MinecraftVersion.ConvertVersion(v.packageMoniker, type).ToString();
                string arch = VersionDbExtensions.GetVersionArch(v.packageMoniker, type);
                var info = new VersionInfoJson(version, v.updateId, type, arch, v.revisionNumber);

                int existingIndex = list.FindIndex(x => x.uuid == info.uuid && x.architecture == info.architecture);
                if (existingIndex >= 0)
                {
                    if (list[existingIndex].revisionNumber < info.revisionNumber)
                    {
                        list[existingIndex] = info;
                    }
                    continue;
                }

                if (!list.Exists(x => x.version == info.version && x.architecture == info.architecture && x.type == info.type))
                {
                    list.Add(info);
                }
            }
        }

        public void Save(string filePath)
        {
            SortVersions();

            string outlist = string.Empty;
            foreach (var ver in list)
            {
                string entry = $"[\"{ver.version}\", \"{ver.uuid}\", {(int)ver.type}, \"{ver.architecture}\", {ver.revisionNumber}]";
                if (list.Count != list.IndexOf(ver) + 1) entry += ", " + Environment.NewLine;
                outlist += entry;
            }

            string output = $"[{outlist}]";
            File.WriteAllText(filePath, output);
        }

        public List<IVersionInfo> GetVersions()
        {
            return this.list.Cast<IVersionInfo>().ToList();
        }

        public void PraseRaw(string data, Dictionary<Guid, string> architectures)
        {
            PraseJson(data, architectures);
        }

        #endregion
    }
}
