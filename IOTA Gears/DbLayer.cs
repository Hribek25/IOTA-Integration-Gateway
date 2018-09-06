using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace IOTA_Gears
{
    public class DbLayer
    {
        private static bool IsValidDBLayer(SqliteConnection connection)
        {
            var cmd = @"SELECT name FROM sqlite_master WHERE type='table'";

            var c = connection.CreateCommand();
            c.CommandText = cmd;

            try
            {
                connection.Open();
                using (var reader = c.ExecuteReader())
                {
                    if (!reader.HasRows)
                    {
                        connection.Close();
                        return false;
                    }

                    var tables = new List<string>();
                    while (reader.Read())
                    {
                        tables.Add((string)reader["name"]);
                    }

                    if (!tables.Contains("cache") || !tables.Contains("partial_cache") || !tables.Contains("task_pipeline"))
                    {
                        connection.Close();
                        return false;
                    }
                };
            }
            catch (Exception)
            {
                connection.Close();
                return false;
            }

            connection.Close();
            return true;
        }

        private static bool InitDB(SqliteConnection connection)
        {
            var initscript =
    @"DROP TABLE IF EXISTS [cache];
CREATE TABLE [cache] (  
 [timestamp] bigint  NOT NULL
, [query] text NOT NULL
, [response_type] text NOT NULL
, [response] text NOT NULL
);

DROP TABLE IF EXISTS [partial_cache];
CREATE TABLE [partial_cache] (
[timestamp] bigint NOT NULL
, [call] TEXT NOT NULL
, [ident] TEXT NOT NULL
, [EntityTimestamp] bigint
, [result] TEXT NOT NULL);


DROP TABLE IF EXISTS [task_pipeline];
CREATE TABLE [task_pipeline] (
  [timestamp] bigint NOT NULL
, [task] text NOT NULL
, [input] text NULL
, [result] text NULL
, [performed] numeric(53,0)  NOT NULL
, [performed_when] bigint NULL
, [ip] text NULL
, [guid] text NULL);

";
            var c = connection.CreateCommand();
            c.CommandText = initscript;
            var res = false;

            connection.Open();
            try
            {
                c.ExecuteNonQuery();
                res = true;
            }
            catch (Exception)
            {
                res = false;
            }
            finally
            {
                connection.Close();
            }

            Console.WriteLine($"DB CREATED from scratch... Location: {connection.DataSource}");
            return res;
        }

        public static bool IsDBLayerReady()
        {
            var res = false;
            var DataSource = Program.DBLayerDataSource();
            var dbconBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = DataSource
            };

            var initDB = false;
            if (!System.IO.File.Exists(DataSource)) // DB file does not exist
            {
                initDB = true;
                Console.WriteLine($"DB file not found! Location: {DataSource}");
            }            

            using (var DBCon = new SqliteConnection(dbconBuilder.ConnectionString))
            {
                var validDB = false;

                if (initDB == false)
                {
                    validDB = IsValidDBLayer(DBCon);
                }

                if (initDB || !validDB ) // some basic checks whether the DB is ok in terms of structure
                {
                    if (InitDB(DBCon)) // let's create new DB from scratch 
                    {
                        res = true;
                    }
                    else
                    {
                        res = false;
                    }
                }
                else
                {
                    res = true;
                }
                
            }            
            return res;
        }

    }
}
