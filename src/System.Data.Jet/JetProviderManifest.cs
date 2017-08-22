using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml;

namespace System.Data.Jet
{
    /// <summary>
    /// This class is a copy of the Provider Manifest for Jet of the JetEntityFrameworkProvider
    /// Is used only for accessing to XML resources
    /// </summary>
    public class JetProviderManifest
    {

        public static XmlReader GetProviderManifest()
        {
            return GetXmlResource("System.Data.Jet.Resources.JetProviderServices.ProviderManifest.xml");
        }

        public XmlReader GetStoreSchemaMapping()
        {
            return GetXmlResource("System.Data.Jet.Resources.JetProviderServices.StoreSchemaMapping.msl");
        }

        public static XmlReader GetStoreSchemaDescription()
        {
            return GetXmlResource("System.Data.Jet.Resources.JetProviderServices.StoreSchemaDefinition.ssdl");
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