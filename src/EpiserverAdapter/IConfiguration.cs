using System.Collections.Generic;
using System.Globalization;
using Epinova.InRiverConnector.EpiserverAdapter.Enums;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public interface IConfiguration
    {
        CVLDataMode ActiveCVLDataMode { get; }
        LinkType[] AssociationLinkTypes { get; }
        int BatchSize { get; }
        string[] BundleEntityTypes { get; }
        string ChannelDefaultCurrency { get; set; }
        CultureInfo ChannelDefaultLanguage { get; set; }
        string ChannelDefaultWeightBase { get; set; }
        int ChannelId { get; }
        string ChannelIdPrefix { get; set; }
        string[] DynamicPackageEntityTypes { get; }
        EndpointCollection Endpoints { get; set; }
        string EpiApiKey { get; }
        Dictionary<string, string> EpiCodeMapping { get; }
        string EpiEndpoint { get; }
        HashSet<string> EPiFieldsIninRiver { get; }
        Dictionary<string, string> EpiNameMapping { get; }
        int EpiRestTimeout { get; }
        List<EntityType> ExportEnabledEntityTypes { get; }
        bool ForceIncludeLinkedContent { get; }
        string HttpPostUrl { get; }
        string Id { get; }
        bool ItemsToSkus { get; }
        Dictionary<CultureInfo, CultureInfo> LanguageMapping { get; }
        List<LinkType> LinkTypes { get; set; }
        string[] PackageEntityTypes { get; }
        string PublicationsRootPath { get; }
        string[] ResourceConfigurations { get; }
        Dictionary<string, string> ResourceConfiugurationExtensions { get; }
        string ResourcesRootPath { get; }
        bool UseThreeLevelsInCommerce { get; }
    }
}
