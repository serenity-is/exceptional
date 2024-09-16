using System;
using System.Collections.Generic;

namespace StackExchange.Exceptional
{
    public static partial class Extensions
    {
        static T GetProperty<T>(object obj, string name)
        {
            var value = obj.GetType().GetProperty(name)?.GetValue(obj);
            if (value == null || !typeof(T).IsAssignableFrom(value.GetType()))
                return default;

            return (T)value;
        }

        /// <summary>
        /// Adds the default data handlers to a handlers collection.
        /// </summary>
        /// <param name="handlers">The dictionary to register these default handlers on.</param>
        public static Dictionary<string, Action<Error>> AddDefault(this Dictionary<string, Action<Error>> handlers)
        {
            static void sqlExceptionHandler(Error e, Exception ex)
            {
                if (ex.Data == null) return;
                var procedure = GetProperty<string>(ex, "Procedure");
                e.AddCommand(new Command("SQL Server Query", ex.Data.Contains("SQL") ? ex.Data["SQL"] as string : null)
                    .AddData("Server", GetProperty<string>(ex, "Server"))
                    .AddData("Number", GetProperty<int>(ex, "Number").ToString())
                    .AddData("LineNumber", GetProperty<int>(ex, "LineNumber").ToString())
                    .AddData(!string.IsNullOrEmpty(procedure), "Procedure", procedure)
                );
            }

            handlers?.AddHandler("Microsoft.Data.SqlClient.SqlException", sqlExceptionHandler);
            handlers?.AddHandler("System.Data.SqlClient.SqlException", sqlExceptionHandler);
            handlers?.AddHandler("StackRedis.CacheException", (e, ex) =>
            {
                var cmd = e.AddCommand(new Command("Redis"));
                foreach (string k in ex.Data.Keys)
                {
                    var val = ex.Data[k] as string;
                    if (k == "redis-command") cmd.CommandString = val;
                    if (k.StartsWith("Redis-")) cmd.AddData(k.Substring("Redis-".Length), val);
                }
            });

            return handlers;
        }

        /// <summary>
        /// Convenience method for adding a handler for an exception type.
        /// </summary>
        /// <typeparam name="T">The specific type of <see cref="Exception"/> to handle.</typeparam>
        /// <param name="handlers">The handlers collection to add to (usually Exceptional.Settings.DataHandlers)</param>
        /// <param name="handler">The handler action to use.</param>
        public static void AddHandler<T>(this Dictionary<string, Action<Error>> handlers, Action<Error, T> handler) where T : Exception
        {
            handlers[typeof(T).FullName] = e =>
            {
                // Note: we're cheating here and assuming nested exceptions won't be the same type and we're mirroring
                // the exception core code in iterating through nested exceptions. This isn't correct, but it's a design flaw
                // in the Action<Error>...we should have Action<Error,Exception> in the handlers dictionary so we can pass
                // the inner exception we're triggering on Error.ProcessHandlers. That's a breaking change for a 3.x release.
                var ex = e.Exception;
                while (ex != null)
                {
                    if (ex is T tex)
                    {
                        handler(e, tex);
                    }
                    ex = ex.InnerException;
                }
            };
        }

        /// <summary>
        /// Convenience method for adding a handler for an exception type. Note that the handler here doesn't have the exception
        /// (due to not having a reference to every exception on earth), so behavior is limited to Exception for things like .Data, etc.
        /// </summary>
        /// <param name="handlers">The handlers collection to add to (usually Exceptional.Settings.DataHandlers)</param>
        /// <param name="typeName">The full name of the type, e.g. "System.Data.SqlClient.SqlException"</param>
        /// <param name="handler">The handler action to use.</param>
        public static void AddHandler(this Dictionary<string, Action<Error>> handlers, string typeName, Action<Error, Exception> handler)
        {
            handlers[typeName] = e =>
            {
                // Note: cheating - see method above
                var ex = e.Exception;
                while (ex != null)
                {
                    if (ex.GetType().FullName == typeName)
                    {
                        handler(e, ex);
                    }
                    ex = ex.InnerException;
                }
            };
        }
    }
}
