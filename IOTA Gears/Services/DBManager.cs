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
using System.Data.HashFunction;
using System.Linq;
using System.Data.HashFunction.xxHash;

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
        private IxxHash _xxHash { get; }

        private bool disposed = false;
        private readonly int AbuseTimeInterval = 60; // interval (number of seconds) to check an abuse usage
        private readonly int AbuseCount = 3; // max number of requests from the same IP within the AbuseTimeInterval

        public DBManager(ILogger<DBManager> logger, IConfiguration conf, IxxHash hashprovider)
        {
            Configuration = conf;
            Logger = (Logger<DBManager>)logger;
            this.DbProvider = conf.GetValue<DbLayerProvider>("DBLayerProvider");
            var DbConnStr = conf.GetValue<string>("SqlDbConnStr");
            string connStr;
            _xxHash = hashprovider;

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
            Logger.LogInformation("DB storage initiated and ready... Using {DbProvider.ToString()}, source: {DBConnection.DataSource}", DbProvider.ToString(), DBConnection.DataSource);
        }

        private string GetCacheSubDir(string subDir)
        {            
            var target = Path.Combine(Program.CacheBasePath(), subDir);
            if (Directory.Exists(target))
            {
                return target;
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception e)
                {
                    Logger.LogError("Can't create sub cache directory {target}. Error {e}", target, e.Message);
                    return null;
                }
                return target;
            }
        }
        private string GetElementCacheSubDir(string subDir)
        {
            var target = Path.Combine(Program.CacheElementsBasePath(), subDir);
            if (Directory.Exists(target))
            {
                return target;
            }
            else
            {
                try
                {
                    Directory.CreateDirectory(target);
                }
                catch (Exception e)
                {
                    Logger.LogError("Can't create sub cache directory {target}. Error {e}", target, e.Message);
                    return null;
                }
                return target;
            }
        }
        private string CacheEntryFingerPrint(string request, string contentType)
        {
            var cnttype = contentType.Split(";")[0].Replace(" ","", StringComparison.InvariantCulture); // this is workqround in case there is also encoding specified in content type
            var callerid = (request + cnttype).ToUpperInvariant();
            var hsh = this._xxHash.ComputeHash(callerid).AsHexString();
            return hsh;
        }
        private string CacheElementEntryFingerPrint(string callerId)
        {            
            var callerid = callerId.ToUpperInvariant();
            var hsh = this._xxHash.ComputeHash(callerid).AsHexString();
            return hsh;
        }

        public async Task<JsonResult> GetFSCacheEntryAsync(string request, string contentType, int LifeSpan)
        {
            var hashcallerid = CacheEntryFingerPrint(request, contentType);            
            var targetDir = GetCacheSubDir(hashcallerid.Substring(0,2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hashcallerid.Substring(2)); // target file incl full path 

            JsonResult cacheEntry = null; // default return value
            if (File.Exists(targetFile)) // there is cache entry
            {
                TimeSpan actualSpan = DateTime.UtcNow - File.GetLastWriteTimeUtc(targetFile);
                if (actualSpan.TotalSeconds <= LifeSpan) // cache entry is still quite fresh, so leveraging it
                {
                    var jsdata = await File.ReadAllTextAsync(targetFile);
                    var tmp = DBSerializer.DeserializeFromJson(jsdata);
                    cacheEntry = new JsonResult(tmp);
                }                 
            }            
            return cacheEntry;
        }
        public async Task AddFSCacheEntryAsync(string request, JsonResult result, string contentType)
        {
            var hashcallerid = CacheEntryFingerPrint(request, contentType);
            var targetDir = GetCacheSubDir(hashcallerid.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hashcallerid.Substring(2)); // target file incl full path 
            var json = DBSerializer.SerializeToJson(result.Value);
            await File.WriteAllTextAsync(targetFile, json);
        }
        public async Task<object> GetFSPartialCacheEntryAsync(string call)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            object OutputCacheEntry = null;

            if (File.Exists(targetFile)) // there is cache entry
            {
                var jsdata = await File.ReadAllTextAsync(targetFile);
                OutputCacheEntry = DBSerializer.DeserializeFromJson(jsdata);
            }

            Logger.LogInformation("Partial cache used (GET) for individual element. {OutputCacheEntry?.GetType()} was loaded for the caller: {call}.", OutputCacheEntry?.GetType(), call.Substring(0, 50));
            return OutputCacheEntry;
        }

        public async Task AddFSPartialCacheEntryAsync(string call, object result)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            var json = DBSerializer.SerializeToJson(result);
            await File.WriteAllTextAsync(targetFile, json);            

            Logger.LogInformation("Partial cache used (ADD) for an individual element. {result.GetType()} object was saved for the caller {call}.", result.GetType(), call.Substring(0, 50));
        }
        
        public async Task<Int64> AddDBTaskEntryAsync(string task, object input, string ip, string globaluid)
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
        public async Task<List<TaskEntry>> GetDBTaskEntryFromPipelineAsync(int limit = 1)
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
        public async Task UpdateDBTaskEntryInPipeline(string globalid, int performed, object result)
        {
            var outputcmd = _UpdateTaskEntrySQL(globalid, performed, result);

            DBConnection.Open();
            await outputcmd.ExecuteNonQueryAsync();
            DBConnection.Close();

            Logger.LogInformation("Task pipeline (UPDATE) for an individual element. Rowid {guid} was updated with status {performed}.", globalid, performed);
        }

        public async Task<Dictionary<string, object>> GetFSPartialCacheEntriesAsync(string call)
        {
            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            Dictionary<string, object> OutputCacheEntry = null;

            if (File.Exists(targetFile)) // there is cache entry
            {
                var jsdata = await File.ReadAllTextAsync(targetFile);
                OutputCacheEntry = (Dictionary<string, object>)DBSerializer.DeserializeFromJson(jsdata);
            }
            
            Logger.LogInformation("Partial cache used (GET) for multiple elements. {OutputCacheEntry.Count} records were loaded for the caller {call}.", OutputCacheEntry.Count, call.Substring(0, 50));
            return OutputCacheEntry.Count == 0 ? null : OutputCacheEntry;
        }
        public async Task AddFSPartialCacheEntriesAsync(string call, IEnumerable<object> results, Func<object, string> identDelegate)
        {
            if (identDelegate == null)
            {
                throw new ArgumentNullException(paramName: nameof(identDelegate));
            }

            var hshcall = CacheElementEntryFingerPrint(call);
            var targetDir = GetElementCacheSubDir(hshcall.Substring(0, 2)); // create and return target sub directory
            var targetFile = Path.Combine(targetDir, hshcall.Substring(2)); // target file incl full path 

            Dictionary<string, object> elements = (from i in results select i ).ToDictionary(a => identDelegate(a), b => b);
            
            var json = DBSerializer.SerializeToJson(elements);
            await File.WriteAllTextAsync(targetFile, json);
            
            Logger.LogInformation("Partial cache used (ADD) for multiple elements. {elements.Count} records were saved for the caller {call}.", elements.Count, call.Substring(0, 50));
        }

        #region SQLHelpers
        
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
