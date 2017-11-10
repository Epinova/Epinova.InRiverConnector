using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    internal static class Extensions
    {
        internal static bool IsItem(this StructureEntity structureEntity)
        {
            return structureEntity.Type == "Item";
        }

        internal static bool IsChannelNode(this StructureEntity structureEntity)
        {
            return structureEntity.Type == "ChannelNode";
        }

        internal static bool SourceEntityTypeIsChannelNode(this LinkType linkType)
        {
            return linkType.SourceEntityTypeId == "ChannelNode";
        }
    }
}