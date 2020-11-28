using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml;

namespace EntityFrameworkCore.Jet.Data
{
    /// <summary>
    /// This class is a copy of the Provider Manifest for Jet of the JetEntityFrameworkProvider
    /// Is used only for accessing to XML resources
    /// </summary>
    public class JetProviderManifest
    {

        public static XmlReader GetProviderManifest()
        {
            return GetXmlResource("EntityFrameworkCore.Jet.Data.Resources.JetProviderServices.ProviderManifest.xml");
        }

        public XmlReader GetStoreSchemaMapping()
        {
            return GetXmlResource("EntityFrameworkCore.Jet.Data.Resources.JetProviderServices.StoreSchemaMapping.msl");
        }

        public static XmlReader GetStoreSchemaDescription()
        {
            return GetXmlResource("EntityFrameworkCore.Jet.Data.Resources.JetProviderServices.StoreSchemaDefinition.ssdl");
        }

        public static XmlReader GetXmlResource(string resourceName)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();
            Stream stream = executingAssembly.GetManifestResourceStream(resourceName);
            Debug.Assert(stream != null, "stream != null");
            return XmlReader.Create(stream);
        }


 
    }
}