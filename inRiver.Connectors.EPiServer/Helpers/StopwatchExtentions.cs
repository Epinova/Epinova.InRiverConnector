namespace inRiver.EPiServerCommerce.CommerceAdapter.Helpers
{
    using System.Diagnostics;

    public static class StopwatchExtentions
    {
        internal static string GetElapsedTimeFormated(this Stopwatch stopwatch)
        {
            if (stopwatch.ElapsedMilliseconds < 1000)
            {
                return string.Format("{0} ms", stopwatch.ElapsedMilliseconds);
            }

            return stopwatch.Elapsed.ToString("hh\\:mm\\:ss");
        }
    }
}
