using System.Collections.Generic;
using System.Globalization;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public interface IConfiguration
    {
        int EpiRestTimeout { get; }
        string EpiApiKey { get; }
        string EpiEndpoint { get; }
        EndpointCollection Endpoints { get; set; }
        string Id { get; }
        List<LinkType> LinkTypes { get; set; }
        int ChannelId { get; }
        string PublicationsRootPath { get; }
        List<EntityType> ExportEnabledEntityTypes { get; }
        string HttpPostUrl { get; }
        Dictionary<CultureInfo, CultureInfo> LanguageMapping { get; set; }
        Dictionary<string, string> EpiNameMapping { get; }
        string ResourcesRootPath { get; }
        bool UseThreeLevelsInCommerce { get; }
        CultureInfo ChannelDefaultLanguage { get; set; }
        string ChannelDefaultCurrency { get; set; }
        Dictionary<string, string> EpiCodeMapping { get; }
        string ChannelDefaultWeightBase { get; set; }
        string ChannelIdPrefix { get; set; }
        string[] ResourceConfigurations { get; }
        Dictionary<string, string> ResourceConfiugurationExtensions { get; }
        LinkType[] AssociationLinkTypes { get; }
        bool ItemsToSkus { get; }
        int BatchSize { get; }
        string[] BundleEntityTypes { get; }
        string[] PackageEntityTypes { get; }
        string[] DynamicPackageEntityTypes { get; }
        HashSet<string> EPiFieldsIninRiver { get; }
        CVLDataMode ActiveCVLDataMode { get; }
    }
}