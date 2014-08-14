// The MIT License (MIT)
// 
// Copyright (c) 2014 James White of Gbase.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using ServiceStack;

namespace Gbase.NLog.JsonTarget
{
    [Target("JsonPost")]
    public sealed class JsonPostTarget : Target
    {
        /// <summary>
        ///     A simple utility for posting json
        /// </summary>
        private JsonPoster _poster;

        #region Initialization & Cleanup

        public JsonPostTarget()
        {
            Fields = new List<LogField>();
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            _poster = new JsonPoster();
        }

        protected override void CloseTarget()
        {
            try
            {
                _poster.Dispose();
                _poster = null;
            }
            catch (Exception)
            {
                Trace.WriteLine("Exception disposing of JsonPoster while closing JsonPostTarget");
            }

            base.CloseTarget();
        }

        #endregion

        [ArrayParameter(typeof (LogField), "field")]
        public IList<LogField> Fields { get; private set; }

        [RequiredParameter]
        public Layout Url { get; set; }

        protected override void Write(AsyncLogEventInfo info)
        {
            try
            {
                var uri = new Uri(Url.Render(info.LogEvent));
                var json = BuildJsonEvent(info.LogEvent);

#if DEBUG
                Debug.WriteLine("Sending: " + json);
#endif

                _poster.Post(uri, json);

                info.Continuation(null);
            }
            catch (Exception ex)
            {
                info.Continuation(ex);
            }
        }

        /// <summary>
        ///     Flush any pending log messages asynchronously (in case of asynchronous targets).
        /// </summary>
        /// <param name="asyncContinuation">The asynchronous continuation.</param>
        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            var flushTask = Task.Factory.StartNew(pst =>
            {
                var poster = ((JsonPoster)pst);

                while (poster.ActivePosts > 0)
                {
                    Debug.WriteLine("JsonPostTarget waiting for {0} posts to complete", poster.ActivePosts);
                    Thread.Sleep(1);
                }

            }, _poster);

            flushTask.ContinueWith(task => asyncContinuation(task.Exception));
        }

        private string BuildJsonEvent(LogEventInfo logEvent)
        {
            var doc = new Dictionary<string, object>(Fields.Count);

            foreach (var field in Fields)
            {
                if (field.Layout != null)
                {
                    doc[field.Name] = field.Layout.Render(logEvent);
                }
                else
                {
                    doc[field.Name] = GetEventPropertyValue(logEvent, field.Name);
                }
            }

            return ToJson(doc);
        }

        protected override void Write(LogEventInfo logEvent)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     This is the only place we use the json serializer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        private static string ToJson<T>(T value)
        {
            return value.ToJson();
        }

        #region Field Lookups

        public static object GetEventPropertyValue(LogEventInfo logEvent, string propertyName)
        {
            // First try standard properties
            switch (propertyName.ToLower())
            {
                case "exception":
                    return logEvent.Exception;
                case "stacktrace":
                    return logEvent.StackTrace;
                case "level":
                    return logEvent.Level;
                case "loggername":
                    return logEvent.LoggerName;
                case "sequenceid":
                    return logEvent.SequenceID;
                case "properties":
                    return ConvertToNameAndValue(logEvent.Properties);
                case "message":
                    return logEvent.Message;
                case "timestamp":
                    return logEvent.TimeStamp;
                case "hasstacktrace":
                    return logEvent.HasStackTrace;
                case "userstackframe":
                    return logEvent.UserStackFrame;
                case "userstackframenumber":
                    return logEvent.UserStackFrameNumber;
            }

            // Then try the properties dictionary 
            object result;

            if (logEvent.Properties.TryGetValue(propertyName, out result))
                return result;

            // Finally try public properties through reflection (uhg)
            var property = typeof (LogEventInfo).GetProperty(
                propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (property != null)
                return property.GetValue(logEvent, BindingFlags.Public, null, null, null);


            throw new NLogConfigurationException("Property '" + propertyName + "' not present in log event");
        }

        private static IDictionary<string, object> ConvertToNameAndValue(
            IEnumerable<KeyValuePair<object, object>> properties)
        {
            var doc = new Dictionary<string, object>();

            foreach (var entry in properties)
            {
                doc[entry.Key.ToString()] = entry.Value;
            }

            return doc;
        }

        #endregion
    }
}