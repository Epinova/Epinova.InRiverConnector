using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public interface IPimFieldAdapter
    {
        bool FieldTypeIsMultiLanguage(FieldType fieldType);
        string GetAllowSearch(FieldType fieldType);
        IEnumerable<string> CultureInfosToStringArray(CultureInfo[] cultureInfo);
        string GetStartDateFromEntity(Entity entity);
        string GetEndDateFromEntity(Entity entity);
        string FieldIsUseInCompare(FieldType fieldType);
        string GetDisplayNameFromEntity(Entity entity, int maxLength);
        string GetFieldValue(Entity entity, string fieldName, CultureInfo ci);
        List<XElement> GetCVLValues(Field field);
        string GetFlatFieldData(Field field);
    }
}