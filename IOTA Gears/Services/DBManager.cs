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

namespace IOTA_Gears.Services
{
    public interface IDBManager
    {
        
    }
    
    public class DBManager : IDBManager, IDisposable
    {
        public SqliteConnection DBConnection { get; private set; }
        private ILogger<DBManager> Logger { get; set; }

        public DBManager(ILogger<DBManager> logger)
        {            
            Logger = logger;
            DBConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Program.DBLayerDataSource() }.ConnectionString);
            Logger.LogInformation("DB LAYER ready... Using file: {DBConnection.DataSource}", DBConnection.DataSource);            
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
        
        public async Task AddPartialCacheEntriesAsync(string call, IEnumerable<object> results, Func<object, string> identDelegate)
        {
            if (identDelegate==null)
            {
                throw new ArgumentNullException("identDelegate parameter can't be null.");
            }
            
            DBConnection.Open();

            var cnt = 0;
            using (var tr = DBConnection.BeginTransaction())
            {                
                foreach (var i in _AddPartialCacheOutputEntriesSQL(call, results, identDelegate))
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
        public async Task AddPartialCacheEntryAsync(string call, string ident, object result)
        {
            var outputcmd = _AddPartialCacheOutputEntrySQL(call, ident, result);

            DBConnection.Open();
            using (var tr = DBConnection.BeginTransaction())
            {
                outputcmd.Transaction = tr;
                await outputcmd.ExecuteNonQueryAsync();
                tr.Commit();
            }           
            
            DBConnection.Close();
            Logger.LogInformation("Partial cache used (ADD) for an individual element. {result.GetType()} object was saved for the caller {call}.", result.GetType(), call.Substring(0, 50));
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

        private SqliteCommand _AddPartialCacheOutputEntrySQL(string call, string ident, object result)
        {
            if (string.IsNullOrWhiteSpace(ident))
            {
                throw new ArgumentNullException("Ident parameter can't be empty.");
            }

            var cmd = "DELETE FROM [partial_cache_out] WHERE [call]=@call and [ident]=@ident;" + Environment.NewLine;
            cmd += "INSERT INTO [partial_cache_out] ([timestamp], [call], [ident], [result]) VALUES (strftime('%s','now'), @call, @ident, @result)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json_result = DBSerializer.SerializeToJson(result);
            
            var identVal = ident;
            c.Parameters.AddRange(
                new List<SqliteParameter>()
                {
                    new SqliteParameter("@call",call),
                    new SqliteParameter("@ident",identVal),
                    new SqliteParameter("@result",json_result)                    
                }
            );
            return c;
        }
        private IEnumerable<SqliteCommand> _AddPartialCacheOutputEntriesSQL(string call, IEnumerable<object> results, Func<object,string> identDelegate)
        {
            if (identDelegate==null)
            {
                throw new ArgumentNullException("identDelegate parameter can't be null.");
            }

            var commands = new List<SqliteCommand>();
            string identVal = "";
            foreach (var i in results)
            {
                identVal = identDelegate(i);
                commands.Add(
                    _AddPartialCacheOutputEntrySQL(
                        call: call,
                        ident: identVal,
                        result: i)
                    );
            }
            return commands;
        }
        
        private SqliteCommand _GetPartialCacheOutputEntrySQL(string call)
        {
            var cmd = "SELECT * FROM [partial_cache_out] WHERE [call]=@call ORDER BY datetime([timestamp]) DESC";
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

        #endregion

        public void Dispose()
        {
            this.DBConnection.Dispose();
        }

    }
}
