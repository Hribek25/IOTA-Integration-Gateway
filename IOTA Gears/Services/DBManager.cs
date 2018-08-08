using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Threading.Tasks;

namespace IOTA_Gears.Services
{
    public interface IDBManager
    {
        
    }

    public class DBManager : IDBManager
    {
        public SqliteConnection DBConnection { get; private set; }
        private ILogger<DBManager> Logger { get; set; }

        public DBManager(SqliteConnection connection, ILogger<DBManager> logger)
        {
            Logger = logger;
            DBConnection = connection;                        
            Logger.LogInformation("DB LAYER ready... Using file: {DBConnection.DataSource}", DBConnection.DataSource);            
        }

        public async Task<JsonResult> GetCacheEntryAsync(string request, string contentType, int LifeSpan)
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
            await DBConnection.OpenAsync();

            JsonResult cacheEntry = null; // default return value
            using (var reader = await c.ExecuteReaderAsync())
            {
                if (reader.HasRows)
                {
                    await reader.ReadAsync(); // take the first one whatever it is
                    var jsdata = (string)reader["response"];
                    cacheEntry = new JsonResult(
                        Newtonsoft.Json.JsonConvert.DeserializeObject(
                            jsdata,
                            new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
                            )
                        );
                }
            }
            DBConnection.Close();
            if (cacheEntry != null)
            {
                Logger.LogInformation("Cache entry was loaded from cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
            }
            else
            {
                Logger.LogInformation("No cache entry found in cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
            }

            return cacheEntry;
        }

        //public JsonResult GetCacheEntry(string request, string contentType, int LifeSpan)
        //{
        //    var cmd = "SELECT * FROM [cache] WHERE (cast(strftime('%s','now') as bigint)-cast([timestamp] as bigint))<=@span and [query]=@query and [response_type] like @contenttype";
        //    var c = DBConnection.CreateCommand();
        //    c.CommandText = cmd;
        //    c.Parameters.AddRange(
        //        new List<SqliteParameter>()
        //        {
        //            new SqliteParameter("@span",LifeSpan),
        //            new SqliteParameter("@contenttype",contentType + "%"),
        //            new SqliteParameter("@query",request)
        //        }
        //    );            
        //    DBConnection.Open();
            
        //    JsonResult cacheEntry = null; // default return value
        //    using (var reader = c.ExecuteReader())
        //    {
        //        if (reader.HasRows)
        //        {
        //            reader.Read(); // take the first one whatever it is
        //            var jsdata = (string)reader["response"];
        //            cacheEntry = new JsonResult(
        //                Newtonsoft.Json.JsonConvert.DeserializeObject(
        //                    jsdata,
        //                    new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
        //                    )
        //                );
        //        }
        //    }
        //    DBConnection.Close();
        //    if (cacheEntry != null)
        //    {
        //        Logger.LogInformation("Cache entry was loaded from cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
        //    }
        //    else
        //    {
        //        Logger.LogInformation("No cache entry found in cache for Request: {request}, Lifespan = {LifeSpan}", request, LifeSpan);
        //    }
            
        //    return cacheEntry;
        //}

        //public void AddCacheEntry(string request, JsonResult result, string contentType)
        //{
        //    var cmd = "DELETE FROM [cache] WHERE [query]=@query and [response_type] like @response_type;" + Environment.NewLine; // delete old entries from cache for the given query type/content type
        //    cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";

        //    var c = DBConnection.CreateCommand();
        //    c.CommandText = cmd;

        //    var json = Newtonsoft.Json.JsonConvert.SerializeObject(
        //        result.Value,
        //        Newtonsoft.Json.Formatting.None,
        //        new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
        //        );

        //    c.Parameters.AddRange(
        //        new List<SqliteParameter>()
        //        {
        //            new SqliteParameter("@query",request),
        //            //new SqliteParameter("@span",LifeSpan),
        //            new SqliteParameter("@response_type",contentType),
        //            new SqliteParameter("@response", json)
        //            //new SqliteParameter("@data", SqliteType.Blob,binObj.Length ) { Value=binObj}
        //        }
        //    );

        //    DBConnection.Open();
        //    c.ExecuteNonQuery();
        //    DBConnection.Close();
        //    Logger.LogInformation("New entry to CACHE table for Request: {request}", request);
                        
        //    return ;
        //}

        public async Task AddCacheEntryAsync(string request, JsonResult result, string contentType)
        {
            var cmd = "DELETE FROM [cache] WHERE [query]=@query and [response_type] like @response_type;" + Environment.NewLine; // delete old entries from cache for the given query type/content type
            cmd += "INSERT INTO [cache] ([timestamp],[query], [response_type], [response]) VALUES (strftime('%s','now'),@query, @response_type, @response)";

            var c = DBConnection.CreateCommand();
            c.CommandText = cmd;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                result.Value,
                Newtonsoft.Json.Formatting.None,
                new JsonSerializerSettings() { PreserveReferencesHandling = PreserveReferencesHandling.All, TypeNameHandling = TypeNameHandling.All }
                );

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

            await DBConnection.OpenAsync();
            await c.ExecuteNonQueryAsync();
            DBConnection.Close();
            Logger.LogInformation("New entry to CACHE table for Request: {request}", request);
                        
        }


    }
}
