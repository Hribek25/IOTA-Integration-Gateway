using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using IOTAGears.EntityModels;

namespace IOTAGears.Services
{
    public interface IDBManager
    {
         SqliteConnection DBConnection { get; }         
    }
    
    public class DBManager : IDBManager, IDisposable
    {
        public SqliteConnection DBConnection { get; private set; }
        private ILogger<DBManager> Logger { get; set; }
        private bool disposed = false;

        public DBManager(ILogger<DBManager> logger)
        {            
            Logger = logger;
            DBConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Program.DBLayerDataSource() }.ConnectionString);
            Logger.LogInformation("DB storage initiated and ready... Using file: {DBConnection.DataSource}", DBConnection.DataSource);            
        }

        public async Task<JsonResult> GetCacheEntryAsync(string request, string contentType, int LifeSpan)
        {
            SqliteCommand c = _GetCacheEntrySQL(request, contentType, LifeSpan);
            DBConnection.Open();

            JsonResult cacheEntry = null; // default return value
            using (var reader = await c.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["response"];
                    var tmp = DBSerializer.DeserializeFromJson(jsdata);
                    cacheEntry = new JsonResult(tmp);
                }
            }
            DBConnection.Close();
            
            return cacheEntry;
        }
        public async Task AddCacheEntryAsync(string request, JsonResult result, string contentType)
        {
            SqliteCommand c = _AddCacheEntrySQL(request, result, contentType);

            DBConnection.Open();
            await c.ExecuteNonQueryAsync();
            DBConnection.Close();
            
        }
        
        public async Task AddPartialCacheEntriesAsync(string call, IEnumerable<object> results, Func<object, string> identDelegate, Func<object, long> EntityTimestampDelegate = null)
        {
            if (identDelegate==null)
            {
                throw new ArgumentNullException(paramName: nameof(identDelegate));
            }
            
            DBConnection.Open();

            var cnt = 0;
            using (var tr = DBConnection.BeginTransaction())
            {                
                foreach (var i in _AddPartialCacheOutputEntriesSQL(call, results, identDelegate, EntityTimestampDelegate))
                {
                    i.Transaction = tr;
                    await i.ExecuteNonQueryAsync();
                    cnt += 1;
                }
                tr.Commit();
            }            
            DBConnection.Close();
            Logger.LogInformation("Partial cache used (ADD) for multiple elements. {cnt} records were saved for the caller {call}.", cnt, call.Substring(0, 50));
        }
        public async Task AddPartialCacheEntryAsync(string call, string ident, long timestamp, object result)
        {
            var outputcmd = _AddPartialCacheOutputEntrySQL(call, ident, timestamp, result);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Partial cache used (ADD) for an individual element. {result.GetType()} object was saved for the caller {call}.", result.GetType(), call.Substring(0, 50));
        }
        public async Task AddTaskEntryAsync(string task, object input, string ip, string globaluid)
        {
            var outputcmd = _AddTaskEntrySQL(task, input, ip, globaluid);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Task pipeline (ADD) for an individual element. {input.GetType()} object was saved for the task {task}.", input.GetType(), task);
        }

        public async Task<List<object>> GetPartialCacheEntriesAsync(string call)
        {
            var outputcmd = _GetPartialCacheOutputEntrySQL(call);

            DBConnection.Open();

            var result = new List<object>();
            
            using (var reader = await outputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        var jsdata = (string)reader["result"];
                        var OutputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                        result.Add(OutputCacheEntry);
                    }
                }
            }

            DBConnection.Close();
            Logger.LogInformation("Partial cache used (GET) for multiple elements. {result.Count} records were loaded for the caller {call}.", result.Count, call.Substring(0, 50));

            return result.Count==0 ? null : result;
        }
        public async Task<object> GetPartialCacheEntryAsync(string call)
        {
            var outputcmd = _GetPartialCacheOutputEntrySQL(call);

            DBConnection.Open();

            object OutputCacheEntry = null;
            using (var reader = await outputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    reader.Read(); // take the first one whatever it is
                    var jsdata = (string)reader["result"];
                    OutputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
                }
            }
            
            DBConnection.Close();
            Logger.LogInformation("Partial cache used (GET) for individual element. {OutputCacheEntry?.GetType()} was loaded for the caller: {call}.", OutputCacheEntry?.GetType(), call.Substring(0, 50));

            return OutputCacheEntry;
        }
        public async Task<List<TaskEntry>> GetTaskEntryFromPipelineAsync(int limit = 1)
        {
            var outputcmd = _GetTasksInPipelineSQL(limit);

            DBConnection.Open();

            var output = new List<TaskEntry>();
            using (var reader = await outputcmd.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        var entry = new TaskEntry()
                        {
                            Rowid = (long)reader["rowid"],
                            Input = (TaskEntryInput)DBSerializer.DeserializeFromJson((string)reader["input"]),
                            Task = (string)reader["task"],
                            Timestamp = (long)reader["timestamp"],
                            GlobalId = (string)reader["guid"]
                        };
                        output.Add(entry);
                    }
                }
            }

            DBConnection.Close();
            Logger.LogInformation("Task pipeline (GET) for individual element. {output.count} elements were returned.", output.Count);
            return output;
        }
        
        public async Task UpdateTaskEntryInPipeline(long rowid, int performed, object result)
        {
            var outputcmd = _UpdateTaskEntrySQL(rowid, performed, result);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Task pipeline (UPDATE) for an individual element. Rowid {rowid} was updated with status {performed}.", rowid, performed);
        }

        #region SQLHelpers
        private SqliteCommand _AddCacheEntrySQL(string request, JsonResult result, string contentType)
        {
            var cmd = "DELETE FROM [cache] WHERE [query]=@query and [response_type] like @response_type;" + Environment.NewLine; // delete old entries from cache for the given query type/content type
            cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json = DBSerializer.SerializeToJson(result.Value);

            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@query",request),
                    //new SqliteParameter("@span",LifeSpan),
                    new SqliteParameter("@response_type",contentType),
                    new SqliteParameter("@response", json)
                    //new SqliteParameter("@data", SqliteType.Blob,binObj.Length ) { Value=binObj}
                }
            );
            return c;
        }

        private SqliteCommand _GetCacheEntrySQL(string request, string contentType, int LifeSpan)
        {
            var cmd = "SELECT * FROM [cache] WHERE (cast(strftime('%s','now') as bigint)-cast([timestamp] as bigint))<=@span and [query]=@query and [response_type] like @contenttype";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@span",LifeSpan),
                    new SqliteParameter("@contenttype",contentType + "%"),
                    new SqliteParameter("@query",request)
                }
            );
            return c;
        }

        private SqliteCommand _GetTasksInPipelineSQL(int limit=1)
        {
            var cmd = "SELECT rowid, * FROM [task_pipeline] WHERE performed=0 ORDER BY timestamp ASC LIMIT @limit";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@limit", limit)
                }
            );
            return c;
        }
        
        private SqliteCommand _AddPartialCacheOutputEntrySQL(string call, string ident, long timestamp, object result)
        {
            if (string.IsNullOrWhiteSpace(ident))
            {
                throw new ArgumentNullException(paramName: nameof(ident));
            }

            var cmd = "DELETE FROM [partial_cache] WHERE [call]=@call and [ident]=@ident;" + Environment.NewLine;
            cmd += "INSERT INTO [partial_cache] ([timestamp], [call], [ident], [EntityTimestamp], [result]) VALUES (strftime('%s','now'), @call, @ident, @txtimestamp, @result)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result);
                        
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call),
                    new SqliteParameter("@ident",ident),
                    new SqliteParameter("@result",json_result),
                    new SqliteParameter("@txtimestamp",timestamp)
                }
            );
            return c;
        }

        private IEnumerable<SqliteCommand> _AddPartialCacheOutputEntriesSQL(string call, IEnumerable<object> results, Func<object,string> identDelegate, Func<object, long> EntityTimestampDelegate = null)
        {
            if (identDelegate==null)
            {
                throw new ArgumentNullException(paramName: nameof(identDelegate));
            }

            var commands = new List<SqliteCommand>();
            string identVal = "";
            long EntityTimestamp = 0;
            foreach (var i in results)
            {
                identVal = identDelegate(i);
                EntityTimestamp = EntityTimestampDelegate==null ? 0 : EntityTimestampDelegate(i);

                commands.Add(
                    _AddPartialCacheOutputEntrySQL(
                        call: call,
                        ident: identVal,
                        timestamp: EntityTimestamp,
                        result: i)
                    );
            }
            return commands;
        }
        
        private SqliteCommand _GetPartialCacheOutputEntrySQL(string call)
        {
            var cmd = "SELECT * FROM [partial_cache] WHERE [call]=@call ORDER BY [timestamp] DESC";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call)                    
                }
            );
            return c;
        }

        private SqliteCommand _AddTaskEntrySQL(string task, object input, string ip, string guid)
        {
            var cmd = "INSERT INTO [task_pipeline] ([timestamp], [task], [input], [performed], [ip], [guid]) VALUES (strftime('%s','now'), @task, @input, 0, @ip, @guid);";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(input);

            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@task", task),
                    new SqliteParameter("@input", json_result),
                    new SqliteParameter("@ip", ip),
                    new SqliteParameter("@guid", guid)
                }
            );
            return c;
        }

        private SqliteCommand _UpdateTaskEntrySQL(long rowid, int performed, object result)
        {
            var cmd = "UPDATE [task_pipeline] SET [performed_when]=strftime('%s','now'), [result]=@result, [performed]=@performed WHERE rowid=@rowid;";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result);

            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@performed", performed),
                    new SqliteParameter("@result", json_result),
                    new SqliteParameter("@rowid", rowid)
                }
            );
            return c;
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    // Dispose managed resources.
                    this.DBConnection.Dispose();
                }

                // Call the appropriate methods to clean up
                // unmanaged resources here.
                // If disposing is false,
                // only the following code is executed.
                

                // Note disposing has been done.
                disposed = true;
            }
        }
    }
}
