﻿
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.Sql;
using System.Data.SqlClient;
using System.IO;

namespace DataBaseUtilities
{
	class DBDev
	{
		// SAMPLE PARAMETERS:
		// "Data Source=BRIAN-PC\SQLEXPRESS;Integrated Security=SSPI;"  DBMaint E:\Utilities\DBDev

		// These MUST be provided in the command line parameters: 1, 2, 3.
		static string gConnectionString = null;
		static string gDBName = null;
		static string gProjectDir = null;

		// These may be overridden by command line parameters.
		// Table names should be standardized either plural or singular. 
		// I'm going with singular because sometimes tables will only 
		// ever hold one record (e.g. CompanyInfo) and it would be 
		// silly to name that table plural. But you can always do a 
		// "save it to the Employee table". So SINGULAR.

		static Boolean gDoDeltas = true;
		static Boolean gDoStoredProcedures = true;
		static Boolean gDoWait = true;
		static string gDeltaTableName = "_Delta";
		static string gDBDir = "DB";
		static string gDeltaDir = "Delta";
		static string gProcDir = "Proc";
		static SqlConnection gConnection = null;

		// TODO: Currently this utility is targeted for Microsoft SQL Server. Other servers 
		// should be included with a server=MSSQL as default.  (MySQL to start with).

		// TODO: Somehow work in variable/multiple schemas in to the processes.

		static void Main(string[] args)
		{
			Console.WriteLine("DBDev: Data Base Development utility.");
			Console.WriteLine();
			if (ParseArgs(args))
			{
				try
				{
					VerifyDirs();
					using (gConnection = new SqlConnection(gConnectionString))
					{
						gConnection.Open();
						CreateDB();
						ProcessDeltas();
						UpdateStoredProcs();
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("Exception in Main: " + ex.Message);
				}
			}
			if (gDoWait)
			{
				Console.WriteLine("Press any key to exit.");
				Console.ReadKey(true);
			}
		}

		static void ProcessDeltas()
		{
			if (gDoDeltas)
			{
				DeltaTableInit();

				List<string> scriptsRun = DeltaTableGetAll();
				List<string> fileNames = Directory
					.GetFiles(Path.Combine(gProjectDir, gDBDir, gDeltaDir), "*.sql")
					.OrderBy(e => e)
					.ToList();
				foreach (string fPath in fileNames)
				{
					// fName at this point is a full path and I want to use just the file name itself.
					String fName = Path.GetFileName(fPath);
					if (!scriptsRun.Contains(fName))
					{
						if (RunScript(fPath))
						{

							// Only if the script ran successfully store the name in the table.
							// The idea being if it failed you need to fix it and re-run it.
							DeltaTableAdd(fName);
						}
					}
				}
			}
		}

		static void UpdateStoredProcs()
		{
			if (gDoStoredProcedures)
			{
				List<string> fileNames = Directory.GetFiles(Path.Combine(gProjectDir, gDBDir, gProcDir), "*.sql").OrderBy(e => e).ToList();
				foreach (string fName in fileNames)
				{
					RunScript(fName);
				}
			}
		}

		/// <summary>
		/// This is a very usfull all-purpose method to run a script file as an SQL command.
		/// </summary>
		/// <param name="fName">Full path to the script file (e.g. c:\MyProject\DB\Proc\GetRec.sql)</param>
		/// <returns>success/failure</returns>
		static bool RunScript(string fName)
		{
			try
			{
				Console.WriteLine(string.Format("Running SQL script: {0}.", fName));

				StringBuilder sb = new StringBuilder();
				using (System.IO.StreamReader sr = new StreamReader(fName))
				{
					SqlCommand cmd;
					String line;

					// Implement GO syntax al'la SQL Management Studio; read the script 
					// line-by-line, executing every time you hit a "GO" line and once
					// at the end.
					while ((line = sr.ReadLine()) != null)
					{
						line = line.Trim();

						// Strip out comments as we go.
						int idx = line.IndexOf("-- ");
						if (idx >= 0)
						{
							line = line.Substring(0, idx);
						}
						if ((line.ToUpper().CompareTo("GO") == 0)
						|| (line.ToUpper().CompareTo("GO;") == 0))
						{
							if (sb.Length > 0)
							{
								cmd = new SqlCommand(sb.ToString(), gConnection);
								cmd.ExecuteNonQuery();
								sb.Clear();
							}
						}
						else
						{
							sb.AppendLine(line);
						}
					}
					if (sb.Length > 0)
					{
						cmd = new SqlCommand(sb.ToString(), gConnection);
						cmd.ExecuteNonQuery();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception in RunScript: " + ex.Message);
				return false;
			}
			return true;
		}

		static void DeltaTableInit()
		{
			try
			{

				// Create the table if it doesn't exist.
				string sql = @"
					IF OBJECT_ID('{0}', 'U') IS NULL
					BEGIN 
						CREATE TABLE {0} ([Name] NVARCHAR(1024));
					END;";
				SqlCommand cmd = new SqlCommand(string.Format(sql, gDeltaTableName), gConnection);
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception in DeltaTableInit: " + ex.Message);
			}
		}

		static List<string> DeltaTableGetAll()
		{
			List<string> deltas = new List<string>();
			string sql = @"
				SELECT * FROM {0};";

			SqlCommand cmd = new SqlCommand(string.Format(sql, gDeltaTableName), gConnection);
			SqlDataReader reader = cmd.ExecuteReader();
			while (reader.Read())
			{
				deltas.Add(reader[0].ToString());
			}
			reader.Close();
			return deltas;
		}

		static void DeltaTableAdd(string fName)
		{
			try
			{
				string sql = @"
					INSERT INTO [dbo].[{0}] 
						([Name])
					VALUES 
						('{1}')";
				SqlCommand cmd = new SqlCommand(string.Format(sql, gDeltaTableName, fName), gConnection);
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception in DeltaTableAdd: " + ex.Message);
			}
		}

		static Boolean ParseArgs(string[] args)
		{
			string errMsg = @"
USAGE: DBDev <connection-string> <dbName> <ProjectDir> 

Processes SQL script files in <ProjectDir><DBDir><DeltasDir> and <ProjectDir><DBDir><ProcsDir>:

OPTIONS:
[NoWait]                Don't wait for keystroke after run is finished.
[DeltaTable=_Delta]     Name of table to store the already run delta scripts.
|DBDir=DB]              The sub-dir under the ProjectDir where the scripts.
[DeltaDir=Delta]        The sub-dir under the DBDir for the delta scripts.
[ProcDir=Proc]          The sub-dir under the DBDir for the procedure scripts.
[DeltasOnly|ProcsOnly]  Either process just deltas or stored procedure scripts.
";

/*
0         1         2         3         4         5         6         7         8
012345678901234567890123456789012345678901234567890123456789012345678901234567890
*/

			if (args.Length < 3)
			{
				Console.Write(errMsg);
				return false;
			}
			else
			{

				// Dump the input params.
				foreach (string s in args)
				{
					Console.WriteLine(string.Format("ARG:{0}", s));
				}
				Console.WriteLine("");

				gConnectionString = args[0];
				gDBName = args[1];
				gProjectDir = args[2];
				gDoWait = !args.Contains("nowait", StringComparer.CurrentCultureIgnoreCase);
				gDoDeltas = !args.Contains("ProcsOnly", StringComparer.CurrentCultureIgnoreCase);
				gDoStoredProcedures = !args.Contains("DeltasOnly", StringComparer.CurrentCultureIgnoreCase);
				OverrideValue(ref gDBDir, "DBDir", args);
				OverrideValue(ref gProcDir, "ProcDir", args);
				OverrideValue(ref gDeltaDir, "DeltaDir", args);
				OverrideValue(ref gDeltaTableName, "DeltaTable", args);
			}
			return true;
		}

		static void OverrideValue(ref string gRef, string key, string[] args)
		{
			foreach (string arg in args)
			{
				if (arg.StartsWith(key + "=", StringComparison.CurrentCultureIgnoreCase))
				{
					gRef = arg.Substring(arg.IndexOf("=") + 1);
				}
			}
		}

		static void CreateDB()
		{
			try
			{

				// Create it if it doesn't exist.
				string sql = @"
					IF db_id('{0}') IS NULL
					BEGIN
						USE master;
						CREATE DATABASE {0}; 
					END";

				SqlCommand cmd = new SqlCommand(string.Format(sql, gDBName), gConnection);
				cmd.ExecuteNonQuery();

				// All subsequent commands will apply to the selected DB.
				cmd.CommandText = string.Format("USE {0};", gDBName);
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Exception in CreateDB:" + ex.Message);
			}
		}

		static void VerifyDirs()
		{
			string dir;
			if (!Directory.Exists(gProjectDir))
			{
				throw new Exception("Project directory [" + gProjectDir + "] does not exist.");
			}
			dir = Path.Combine(gProjectDir, gDBDir);
			if (!Directory.Exists(dir))
			{
				throw new Exception("Database root directory [" + dir + "] does not exist.");
			}
			dir = Path.Combine(gProjectDir, gDBDir, gDeltaDir);
			if (!Directory.Exists(dir))
			{
				throw new Exception("Deltas directory [" + dir + "] does not exist.");
			}
			dir = Path.Combine(gProjectDir, gDBDir, gProcDir);
			if (!Directory.Exists(Path.Combine(gProjectDir, gDBDir, gProcDir)))
			{
				throw new Exception("Procedure directory [" + dir + "] does not exist.");
			}
		}
	}
}

