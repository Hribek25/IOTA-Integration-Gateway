using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using IOTAGears.EntityModels;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data.HashFunction.xxHash;

namespace IOTAGears.Services
{
    public interface IDbStorageManager
    {
        DbConnection DBConnection { get; }
        Logger<DbStorageManager> Logger { get; }
        IConfiguration Configuration { get; }
        DbLayerProvider DbProvider { get; }
        //IxxHash HashFunction { get; }
    }
    
    public class DbStorageManager : IDbStorageManager, IDisposable
    {
        public DbConnection DBConnection { get; }
        public Logger<DbStorageManager> Logger { get;  }
        public IConfiguration Configuration { get; }
        public DbLayerProvider DbProvider { get; }
        //public IxxHash HashFunction { get; }

        private bool disposed = false;
        private readonly int AbuseTimeInterval = 60; // interval (number of seconds) to check an abuse usage
        private readonly int AbuseCount = 3; // max number of requests from the same IP within the AbuseTimeInterval

        public DbStorageManager(ILogger<DbStorageManager> logger, IConfiguration conf)
        {
            Configuration = conf;
            Logger = (Logger<DbStorageManager>)logger;
            this.DbProvider = conf.GetValue<DbLayerProvider>("DBLayerProvider");
            var DbConnStr = conf.GetValue<string>("SqlDbConnStr");
            string connStr;
            //HashFunction = hashprovider;

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
            Logger.LogInformation("DB Storage initiated and ready... Using {DbProvider.ToString()}, source: {DBConnection.DataSource}", DbProvider.ToString(), DBConnection.DataSource);
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
                            Input = (TaskEntryInput)JsonSerializer.DeserializeFromJson((string)reader["input"]),
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

            var json_result = JsonSerializer.SerializeToJson(input);

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

            var json_result = JsonSerializer.SerializeToJson(result);

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
