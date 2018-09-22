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
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace IOTAGears.Services
{
    public interface IDBManager
    {
         DbConnection DBConnection { get; }         
    }
    
    public class DBManager : IDBManager, IDisposable
    {
        public DbConnection DBConnection { get; private set; }
        private Logger<DBManager> Logger { get; set; }
        private IConfiguration Configuration { get; set; }
        private DbLayerProvider DbProvider { get; }

        private bool disposed = false;
        private readonly int AbuseTimeInterval = 60; // interval (number of seconds) to check an abuse usage
        private readonly int AbuseCount = 3; // max number of requests from the same IP within the AbuseTimeInterval

        public DBManager(ILogger<DBManager> logger, IConfiguration conf)
        {
            Configuration = conf;
            Logger = (Logger<DBManager>)logger;
            this.DbProvider = conf.GetValue<DbLayerProvider>("DBLayerProvider");
            var DbConnStr = conf.GetValue<string>("SqlDbConnStr");
            string connStr;

            if (DbProvider==DbLayerProvider.Sqlite)
            {
                connStr = "Data Source=" + Program.SqliteDbLayerDataSource();
            }
            else
            {
                connStr = DbConnStr;
            }

            DbConnection DbConn; // Common DbConnection interface
            if (DbProvider == DbLayerProvider.Sqlite)
            {
                DbConn = new SqliteConnection(connStr);
            }
            else
            {
                DbConn = new MySqlConnection(connStr);
            }            

            DBConnection = DbConn;
            Logger.LogInformation("DB storage initiated and ready... Using source: {DBConnection.DataSource}", DBConnection.DataSource);
        }

        public async Task<JsonResult> GetCacheEntryAsync(string request, string contentType, int LifeSpan)
        {
            DbCommand c = _GetCacheEntrySQL(request.ToUpperInvariant(), contentType, LifeSpan);
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
            DbCommand c = _AddCacheEntrySQL(request.ToUpperInvariant(), result, contentType);

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
                foreach (var i in _AddPartialCacheOutputEntriesSQL(call.ToUpperInvariant(), results, identDelegate, EntityTimestampDelegate))
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
            var outputcmd = _AddPartialCacheOutputEntrySQL(call.ToUpperInvariant(), ident, timestamp, result);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Partial cache used (ADD) for an individual element. {result.GetType()} object was saved for the caller {call}.", result.GetType(), call.Substring(0, 50));
        }
        public async Task<Int64> AddTaskEntryAsync(string task, object input, string ip, string globaluid)
        {
            var outputcmd = _AddTaskEntrySQL(task.ToUpperInvariant(), input, ip, globaluid);
            var numberoftaskscmd = _GetNumberOfTasksInPipelineSQL();
            var abusecheckcmd = _GetNumberOfTasksFromSameIpSQL(ip);
            
            DBConnection.Open();
            var checkcount = (Int64)(await abusecheckcmd.ExecuteScalarAsync());
            // Logger.LogInformation("Task pipeline (ADD) for an individual element. Abuse check returned {checkcount}", checkcount);

            Int64 res=-1;
            if (checkcount<=AbuseCount) //no abuse usage - let's add it to the pipe line
            {
                await outputcmd.ExecuteNonQueryAsync();
                res = (Int64)(await numberoftaskscmd.ExecuteScalarAsync()); // at least 0
            }
                        
            DBConnection.Close();

            Logger.LogInformation("Task pipeline (ADD) for an individual element. {input.GetType()} object was saved for the task {task}.", input.GetType(), task);
            return res; // returning -1 in case of abuse usage
        }

        public async Task<List<object>> GetPartialCacheEntriesAsync(string call)
        {
            var outputcmd = _GetPartialCacheOutputEntrySQL(call.ToUpperInvariant());

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
            var outputcmd = _GetPartialCacheOutputEntrySQL(call.ToUpperInvariant());

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
        
        public async Task UpdateTaskEntryInPipeline(string globalid, int performed, object result)
        {
            var outputcmd = _UpdateTaskEntrySQL(globalid, performed, result);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Task pipeline (UPDATE) for an individual element. Rowid {guid} was updated with status {performed}.", globalid, performed);
        }

        #region SQLHelpers
        private DbCommand _AddCacheEntrySQL(string request, JsonResult result, string contentType)
        {
            var cmd = "DELETE FROM `cache` WHERE `query`=@query and `response_type` like @response_type;" + Environment.NewLine; // delete old entries from cache for the given query type/content type

            if (DbProvider==DbLayerProvider.Sqlite)
            {
                cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";
            }
            else
            {
                cmd += "INSERT INTO `cache` (`timestamp`,`query`, `response_type`, `response`) VALUES (UNIX_TIMESTAMP(), @query, @response_type, @response)";
            }            

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;
            var json = DBSerializer.SerializeToJson(result.Value);

            if (DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                        new DbParameter[]
                        {
                        new SqliteParameter("@query",request),
                        new SqliteParameter("@response_type",contentType),
                        new SqliteParameter("@response", json)
                        });
            }
            else
            {
                c.Parameters.AddRange(
                        new DbParameter[]
                        {
                        new MySqlParameter("@query",request),
                        new MySqlParameter("@response_type",contentType),
                        new MySqlParameter("@response", json)
                        });
            }
            return c;
        }

        private DbCommand _GetCacheEntrySQL(string request, string contentType, int LifeSpan)
        {
            string cmd;
            if (DbProvider==DbLayerProvider.Sqlite)
            {
                cmd = "SELECT * FROM [cache] WHERE (cast(strftime('%s','now') as bigint)-cast([timestamp] as bigint))<=@span and [query]=@query and [response_type] like @contenttype";
            }
            else
            {
                cmd = "SELECT * FROM `cache` WHERE (UNIX_TIMESTAMP() - `timestamp`) <= @span and `query` = @query and `response_type` like @contenttype";
            }
            
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@span",LifeSpan),
                        new SqliteParameter("@contenttype",contentType + "%"),
                        new SqliteParameter("@query",request)
                    });
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@span",LifeSpan),
                        new MySqlParameter("@contenttype",contentType + "%"),
                        new MySqlParameter("@query",request)
                    });
            }            
            return c;
        }

        private DbCommand _GetTasksInPipelineSQL(int limit=1)
        {
            var cmd = "SELECT * FROM `task_pipeline` WHERE performed=0 ORDER BY timestamp ASC LIMIT @limit";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@limit", limit)
                    });
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@limit", limit)
                    });
            }
            return c;
        }
        
        private DbCommand _GetNumberOfTasksInPipelineSQL()
        {
            var cmd = "SELECT COUNT(*) FROM `task_pipeline` WHERE performed=0;";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;            
            return c;
        }
        
        private DbCommand _GetNumberOfTasksFromSameIpSQL(string ip)
        {
            string cmd;
            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                cmd = "SELECT COUNT(*) FROM [task_pipeline] WHERE ip=@ip and [timestamp]>=(cast(strftime('%s','now') as bigint)-@abuseinterval);";
            }
            else
            {
                cmd = "SELECT COUNT(*) FROM `task_pipeline` WHERE ip=@ip and `timestamp`>=(UNIX_TIMESTAMP()-@abuseinterval);";
            }
                        
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@ip", ip),
                        new SqliteParameter("@abuseinterval", AbuseTimeInterval)
                    });
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@ip", ip),
                        new MySqlParameter("@abuseinterval", AbuseTimeInterval)
                    });
            }

            return c;
        }

        private DbCommand _AddPartialCacheOutputEntrySQL(string call, string ident, long timestamp, object result)
        {
            if (string.IsNullOrWhiteSpace(ident))
            {
                throw new ArgumentNullException(paramName: nameof(ident));
            }

            var cmd = "DELETE FROM `partial_cache` WHERE `call`=@call and `ident`=@ident;" + Environment.NewLine;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                cmd += "INSERT INTO [partial_cache] ([timestamp], [call], [ident], [EntityTimestamp], [result]) VALUES (strftime('%s','now'), @call, @ident, @txtimestamp, @result)";
            }
            else
            {
                cmd += "INSERT INTO `partial_cache` (`timestamp`, `call`, `ident`, `EntityTimestamp`, `result`) VALUES (UNIX_TIMESTAMP(), @call, @ident, @txtimestamp, @result)";
            }            

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result);

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@call",call),
                        new SqliteParameter("@ident",ident),
                        new SqliteParameter("@result",json_result),
                        new SqliteParameter("@txtimestamp",timestamp)
                    } );
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@call",call),
                        new MySqlParameter("@ident",ident),
                        new MySqlParameter("@result",json_result),
                        new MySqlParameter("@txtimestamp",timestamp)
                    });
            }            
            
            return c;
        }

        private IEnumerable<DbCommand> _AddPartialCacheOutputEntriesSQL(string call, IEnumerable<object> results, Func<object,string> identDelegate, Func<object, long> EntityTimestampDelegate = null)
        {
            if (identDelegate==null)
            {
                throw new ArgumentNullException(paramName: nameof(identDelegate));
            }

            var commands = new List<DbCommand>();

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
        
        private DbCommand _GetPartialCacheOutputEntrySQL(string call)
        {
            var cmd = "SELECT * FROM `partial_cache` WHERE `call`=@call ORDER BY `timestamp` DESC";
            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@call",call)
                    } );
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@call",call)
                    });
            }
            
            return c;
        }

        private DbCommand _AddTaskEntrySQL(string task, object input, string ip, string guid)
        {
            string cmd;

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                cmd = "INSERT INTO [task_pipeline] ([timestamp], [task], [input], [performed], [ip], [guid]) VALUES (strftime('%s','now'), @task, @input, 0, @ip, @guid);";
            }
            else
            {
                cmd = "INSERT INTO `task_pipeline` (`timestamp`, `task`, `input`, `performed`, `ip`, `guid`) VALUES (UNIX_TIMESTAMP(), @task, @input, 0, @ip, @guid);";
            }            

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(input);

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@task", task),
                        new SqliteParameter("@input", json_result),
                        new SqliteParameter("@ip", ip),
                        new SqliteParameter("@guid", guid)
                    });
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@task", task),
                        new MySqlParameter("@input", json_result),
                        new MySqlParameter("@ip", ip),
                        new MySqlParameter("@guid", guid)
                    });
            }            
            return c;
        }

        private DbCommand _UpdateTaskEntrySQL(string guid, int performed, object result)
        {
            string cmd;
            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                cmd = "UPDATE [task_pipeline] SET [performed_when]=strftime('%s','now'), [result]=@result, [performed]=@performed WHERE guid=@guid;";
            }
            else
            {
                cmd = "UPDATE `task_pipeline` SET `performed_when`=UNIX_TIMESTAMP(), `result`=@result, `performed`=@performed WHERE guid=@guid;";
            }

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result);

            if (this.DbProvider == DbLayerProvider.Sqlite)
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new SqliteParameter("@performed", performed),
                        new SqliteParameter("@result", json_result),
                        new SqliteParameter("@guid", guid)
                    });
            }
            else
            {
                c.Parameters.AddRange(
                    new DbParameter[]
                    {
                        new MySqlParameter("@performed", performed),
                        new MySqlParameter("@result", json_result),
                        new MySqlParameter("@guid", guid)
                    });
            }
            
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
                    this.DBConnection?.Dispose();
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
