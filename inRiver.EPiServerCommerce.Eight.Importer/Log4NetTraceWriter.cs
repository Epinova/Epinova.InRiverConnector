namespace inRiver.EPiServerCommerce.Eight.Importer
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Web.Http.Tracing;

    using log4net;

    public sealed class Log4NetTraceWriter : ITraceWriter
    {
        private static readonly ILog StaticLog = LogManager.GetLogger(typeof(Log4NetTraceWriter));

        private static readonly Lazy<Dictionary<TraceLevel, Action<string>>> StaticLoggMap =
            new Lazy<Dictionary<TraceLevel, Action<string>>>(
                () =>
                new Dictionary<TraceLevel, Action<string>>
                    {
                        { TraceLevel.Info, StaticLog.Info },
                        { TraceLevel.Debug, StaticLog.Debug },
                        { TraceLevel.Error, StaticLog.Error },
                        { TraceLevel.Fatal, StaticLog.Fatal },
                        { TraceLevel.Warn, StaticLog.Warn }
                    });

        public bool IsEnabled(string category, TraceLevel level)
        {
            return true;
        }

        public void Trace(HttpRequestMessage request, string category, TraceLevel level, Action<TraceRecord> traceAction)
        {
            if (level == TraceLevel.Off)
            {
                return;
            }

            var record = new TraceRecord(request, category, level);
            traceAction(record);
            this.Log(record);
        }

        private void Log(TraceRecord record)
        {
            var message = new StringBuilder();

            if (record.Request != null)
            {
                if (record.Request.Method != null)
                {
                    message.Append(" ").Append(record.Request.Method.Method);
                }

                if (record.Request.RequestUri != null)
                {
                    message.Append(" ").Append(record.Request.RequestUri.AbsoluteUri);
                }
            }

            if (!string.IsNullOrWhiteSpace(record.Category))
            {
                message.Append(" ").Append(record.Category);
            }

            if (!string.IsNullOrWhiteSpace(record.Operator))
            {
                message.Append(" ").Append(record.Operator).Append(" ").Append(record.Operation);
            }

            if (!string.IsNullOrWhiteSpace(record.Message))
            {
                message.Append(" ").Append(record.Message);
            }

            if (record.Exception != null && !string.IsNullOrEmpty(record.Exception.GetBaseException().Message))
            {
                message.Append(" ").AppendLine(record.Exception.GetBaseException().Message);
            }

            StaticLoggMap.Value[record.Level](message.ToString());
        }
    }
}