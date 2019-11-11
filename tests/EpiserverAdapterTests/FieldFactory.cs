using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapterTests
{
    internal static class FieldFactory
    {
        public static Field CreateField(object value, string typeId)
        {
            return new Field
            {
                FieldType = new FieldType { Id = typeId },
                Data = value
            };
        }
    }
}
