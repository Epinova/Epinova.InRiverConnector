using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter.Helpers
{
    public interface IPimFieldAdapter
    {
        IEnumerable<string> CultureInfosToStringArray(CultureInfo[] cultureInfo);
        string FieldIsUseInCompare(FieldType fieldType);
        bool FieldTypeIsMultiLanguage(FieldType fieldType);
        string GetAllowSearch(FieldType fieldType);
        List<XElement> GetCVLValues(Field field);
        string GetDisplayName(Entity entity, int maxLength);
        string GetEndDate(Entity entity);
        string GetFieldValue(Entity entity, string fieldName, CultureInfo ci);
        string GetFlatFieldData(Field field);
        string GetStartDate(Entity entity);
    }
}