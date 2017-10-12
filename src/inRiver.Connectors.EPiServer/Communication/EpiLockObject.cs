namespace inRiver.EPiServerCommerce.CommerceAdapter.Communication
{
    using System.Diagnostics.CodeAnalysis;

    public class EpiLockObject
    {
        private static EpiLockObject instance = new EpiLockObject();

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1409:RemoveUnnecessaryCode", Justification = "Reviewed. Suppression is OK here.")]
        static EpiLockObject()
        {
        }

        private EpiLockObject()
        {
        }

        public static EpiLockObject Instance
        {
            get
            {
                return instance ?? (instance = new EpiLockObject());
            }
        }
    }
}
