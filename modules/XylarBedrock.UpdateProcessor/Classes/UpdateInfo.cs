using System;
using System.Xml.Linq;

using Xml = XylarBedrock.UpdateProcessor.Extensions.NetworkExtensions;

namespace XylarBedrock.UpdateProcessor.Classes
{
    public class UpdateInfo
    {
        public string serverId;
        public string updateId;
        public string packageMoniker;
        public int revisionNumber = 1;

        public string GetIdentityKey()
        {
            return $"{updateId}|{revisionNumber}";
        }

        public void addXmlInfo(string val)
        {
            var rooted_val = "<root>" + val + "</root>";
            XDocument xmlDocument = XDocument.Parse(rooted_val);
            if (xmlDocument == null) return;

            var identity = Xml.first_node(xmlDocument.Root, "UpdateIdentity");
            if (identity == null) return;
            var attr = Xml.first_attribute(identity, "UpdateID");
            if (attr != null) updateId = attr.Value;
            attr = Xml.first_attribute(identity, "RevisionNumber");
            if (attr != null && int.TryParse(attr.Value, out int parsedRevision)) revisionNumber = parsedRevision;

            var applicability = Xml.first_node(xmlDocument.Root, "ApplicabilityRules");
            var metadata = applicability != null ? Xml.first_node(applicability, "Metadata") : null;

            if (metadata != null)
            {
                var metadataPkgAppx = Xml.first_node(metadata, "AppxPackageMetadata");
                if (metadataPkgAppx != null)
                {
                    var metadataAppx = Xml.first_node(metadataPkgAppx, "AppxMetadata");
                    if (metadataAppx != null)
                    {
                        attr = Xml.first_attribute(metadataAppx, "PackageMoniker");
                        if (attr != null) packageMoniker = attr.Value;
                    }
                }
            }
        }
    }
}
