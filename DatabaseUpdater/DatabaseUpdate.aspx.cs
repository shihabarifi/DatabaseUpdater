using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace DatabaseUpdater
{
    public partial class DatabaseUpdate : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LoadScriptVersions();
                DisplayCurrentVersion();
            }
        }
        private void LoadScriptVersions()
        {
            try
            {
                string scriptsPath = Server.MapPath("~/Scripts");
                if (Directory.Exists(scriptsPath))
                {
                    string[] sqlFiles = Directory.GetFiles(scriptsPath, "*.sql");
                    List<string> versions = sqlFiles
                        .Select(f => Path.GetFileNameWithoutExtension(f))
                        .OrderBy(v => v)
                        .ToList();

                    ddlScriptVersions.Items.Clear();
                    foreach (string version in versions)
                    {
                        ddlScriptVersions.Items.Add(version);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowMessage("حدث خطأ أثناء تحميل قائمة التحديثات: " + ex.Message, false);
            }
        }

        private string GetCurrentVersion()
        {
            string currentVersion = ""; // القيمة الافتراضية
            using (SqlConnection conn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT CurrentVersion FROM DatabaseVersion", conn))
                {
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        currentVersion = result.ToString();
                    }
                }
            }
            return currentVersion;
        }

        private void DisplayCurrentVersion()
        {
            lblCurrentVersion.Text = "الإصدار الحالي: " + GetCurrentVersion();
        }

        private void UpdateDatabaseVersion(string newVersion, SqlConnection conn, SqlTransaction transaction)
        {
            using (SqlCommand cmd = new SqlCommand(
                "UPDATE DatabaseVersion SET CurrentVersion = @Version, LastUpdateDate = GETDATE()", conn,transaction))
            {
                cmd.Parameters.AddWithValue("@Version", newVersion);
                cmd.ExecuteNonQuery();
            }
        }
        private class ScriptHistory
        {
            public string Version { get; set; }
            public bool IsApplied { get; set; }
            public DateTime AppliedDate { get; set; }
        }

        private void EnsureVersioningTables(SqlConnection conn)
        {
            // إنشاء جدول لتتبع التحديثات
            string createVersionTableSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DatabaseVersion')
                BEGIN
                    CREATE TABLE DatabaseVersion
                    (
                        CurrentVersion VARCHAR(10) NOT NULL,
                        LastUpdateDate DATETIME NOT NULL
                    )
                    
                    INSERT INTO DatabaseVersion (CurrentVersion, LastUpdateDate)
                    VALUES ('v1001', GETDATE())
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScriptHistory')
                BEGIN
                    CREATE TABLE ScriptHistory
                    (
                        ScriptVersion VARCHAR(10) NOT NULL,
                        AppliedDate DATETIME NOT NULL,
                        IsSuccess BIT NOT NULL,
                        ErrorMessage NVARCHAR(MAX) NULL
                    )
                END";

            using (SqlCommand cmd = new SqlCommand(createVersionTableSql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        protected void btnUpdate_Click(object sender, EventArgs e)
        {
            if (ddlScriptVersions.SelectedIndex == -1)
            {
                ShowMessage("الرجاء اختيار إصدار التحديث", false);
                return;
            }

            try
            {
                string currentVersion = GetCurrentVersion();
                string targetVersion = ddlScriptVersions.SelectedValue;
                List<string> updateScripts = GetRequiredUpdates(currentVersion, targetVersion);

                if (updateScripts.Count == 0)
                {
                    ShowMessage("لا توجد تحديثات مطلوبة أو الإصدار المحدد أقل من الإصدار الحالي", false);
                    return;
                }
                var backupManager = new DatabaseBackupManager("Data Source=OZARK;Initial Catalog=MaalDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;");
                string backupPath = null;

                using (SqlConnection conn = new SqlConnection(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString))
                {
                    conn.Open();
                    EnsureVersioningTables(conn);

                    List<ScriptHistory> appliedScripts = new List<ScriptHistory>();
                    bool hasError = false;
                    string errorMessage = "";

                    try
                    {
                        // إنشاء نسخة احتياطية قبل بدء التحديث
                        backupPath = backupManager.CreateBackup($"Before_Update_{currentVersion}_to_{targetVersion}");
                        foreach (string scriptVersion in updateScripts)
                        {
                            using (SqlTransaction transaction = conn.BeginTransaction())
                            {
                                try
                                {

                                    // تنفيذ السكربت
                                    ExecuteUpdateScript(scriptVersion, conn, transaction);

                                    // تحديث سجل التحديثات
                                    LogScriptExecution(scriptVersion, true, null, conn, transaction);

                                    // تحديث الإصدار الحالي
                                    UpdateDatabaseVersion(scriptVersion, conn, transaction);

                                    transaction.Commit();
                                    appliedScripts.Add(new ScriptHistory
                                    {
                                        Version = scriptVersion,
                                        IsApplied = true,
                                        AppliedDate = DateTime.Now
                                    });
                                }
                                catch (Exception ex)
                                {
                                    transaction.Commit();
                                    //hasError = true;
                                    //LogScriptExecution(scriptVersion, false, errorMessage, conn, null);
                                    conn.Close();
                                    throw new Exception($"فشل في تنفيذ السكربت {scriptVersion}: {ex.Message}");
                                }
                            }
                        }

                        //if (hasError)
                        //{
                        //    // إذا حدث خطأ، نحاول التراجع عن التحديثات السابقة
                        //    if (!string.IsNullOrEmpty(backupPath))
                        //    {
                        //        try
                        //        {
                        //            backupManager.RestoreBackup(backupPath);
                        //            ShowMessage($"حدث خطأ أثناء التحديث وتم استعادة النسخة الاحتياطية. الخطأ:", false);
                        //        }
                        //        catch (Exception restoreEx)
                        //        {
                        //            ShowMessage($"حدث خطأ أثناء التحديث وفشلت عملية استعادة النسخة الاحتياطية. الخطأ: {restoreEx.Message}", false);
                        //        }
                        //    }
                        //    else
                        //    {
                        //        ShowMessage($"حدث خطأ أثناء التحديث: ", false);
                        //    }
                        //}
                        //else
                        //{
                        //    ShowMessage($"تم تنفيذ التحديثات بنجاح من {currentVersion} إلى {targetVersion}", true);
                        //}
                        ShowMessage($"تم تنفيذ التحديثات بنجاح من {currentVersion} إلى {targetVersion}", true);
                    }
                    catch (Exception ex)
                    {
                        conn.Close();
                        if (!string.IsNullOrEmpty(backupPath))
                        {
                            try
                            {
                                backupManager.RestoreBackup(backupPath);
                                ShowMessage($"حدث خطأ أثناء التحديث وتم استعادة النسخة الاحتياطية. الخطأ: {ex.Message}", false);
                            }
                            catch (Exception restoreEx)
                            {
                                ShowMessage($"حدث خطأ أثناء التحديث وفشلت عملية استعادة النسخة الاحتياطية. الخطأ: {restoreEx.Message}", false);
                            }
                        }
                        else
                        {
                            ShowMessage($"حدث خطأ أثناء التحديث: {ex.Message}", false);
                        }
                    }

                    DisplayCurrentVersion();
                }
            }
            catch (Exception ex)
            {
                ShowMessage("حدث خطأ أثناء تنفيذ التحديث: " + ex.Message, false);
            }
        }

        private void ExecuteUpdateScript(string scriptVersion, SqlConnection conn, SqlTransaction transaction)
        {
            string scriptPath = Server.MapPath($"~/Scripts/{scriptVersion}.sql");
            string script = File.ReadAllText(scriptPath);


            // تقسيم السكربت إلى أوامر منفصلة
            IEnumerable<string> commands = SplitSqlStatements(script);

            foreach (string command in commands)
            {
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (SqlCommand cmd = new SqlCommand(command, conn, transaction))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private IEnumerable<string> SplitSqlStatements(string script)
        {
            // تقسيم السكربت على GO statements
            return script.Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(statement => statement.Trim())
                        .Where(statement => !string.IsNullOrWhiteSpace(statement));
        }

        private void LogScriptExecution(string scriptVersion, bool isSuccess, string errorMessage,
            SqlConnection conn, SqlTransaction transaction)
        {
            string sql = @"
                INSERT INTO ScriptHistory (ScriptVersion, AppliedDate, IsSuccess, ErrorMessage)
                VALUES (@Version, GETDATE(), @IsSuccess, @ErrorMessage)";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@Version", scriptVersion);
                cmd.Parameters.AddWithValue("@IsSuccess", isSuccess);
                cmd.Parameters.AddWithValue("@ErrorMessage", (object)errorMessage ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        private class DatabaseBackupManager
        {
            private readonly string connectionString;
            private readonly string backupPath;

            public DatabaseBackupManager(string connectionString)
            {
                this.connectionString = connectionString;
                this.backupPath = Path.Combine("C:\\", "Backups");
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }
            }

            public string CreateBackup(string backupName)
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string dbName = connection.Database;
                    string backupFile = Path.Combine(backupPath, $"{backupName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");

                    string backupQuery = $@"
                BACKUP DATABASE [{dbName}] 
                TO DISK = @BackupPath
                WITH FORMAT, 
                     NAME = @BackupName,
                     DESCRIPTION = 'Auto backup before update'";

                    using (var command = new SqlCommand(backupQuery, connection))
                    {
                        command.Parameters.AddWithValue("@BackupPath", backupFile);
                        command.Parameters.AddWithValue("@BackupName", backupName);
                        command.ExecuteNonQuery();
                    }

                    return backupFile;
                }
            }

            public void RestoreBackup(string backupPath)
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    string dbName = connection.Database;

                    // إغلاق كافة الاتصالات النشطة
                    string killConnectionsQuery = $@"
                USE master;
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;";

                    string restoreQuery = $@"
                RESTORE DATABASE [{dbName}] 
                FROM DISK = @BackupPath
                WITH REPLACE;
                ALTER DATABASE [{dbName}] SET MULTI_USER;";

                    using (var command = new SqlCommand(killConnectionsQuery + restoreQuery, connection))
                    {
                        command.Parameters.AddWithValue("@BackupPath", backupPath);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        //private void RollbackUpdates(List<ScriptHistory> appliedScripts, SqlConnection conn)
        //{
        //    foreach (var script in appliedScripts.OrderByDescending(s => s.Version))
        //    {
        //        try
        //        {
        //            string rollbackScript = GetRollbackScript(script.Version);
        //            if (!string.IsNullOrEmpty(rollbackScript))
        //            {
        //                using (SqlTransaction transaction = conn.BeginTransaction())
        //                {
        //                    try
        //                    {
        //                        using (SqlCommand cmd = new SqlCommand(rollbackScript, conn, transaction))
        //                        {
        //                            cmd.ExecuteNonQuery();
        //                        }
        //                        transaction.Commit();
        //                    }
        //                    catch
        //                    {
        //                        transaction.Rollback();
        //                    }
        //                }
        //            }
        //        }
        //        catch
        //        {
        //            // تسجيل الخطأ في Log
        //        }
        //    }

        //    // إعادة الإصدار إلى آخر نسخة ناجحة
        //    if (appliedScripts.Any())
        //    {
        //        var lastSuccessfulVersion = GetLastSuccessfulVersion(conn);
        //        using (SqlTransaction transaction = conn.BeginTransaction())
        //        {
        //            try
        //            {
        //                UpdateDatabaseVersion(lastSuccessfulVersion, conn, transaction);
        //                transaction.Commit();
        //            }
        //            catch
        //            {
        //                transaction.Rollback();
        //            }
        //        }
        //    }
        //}

        //private string GetRollbackScript(string version)
        //{
        //    // البحث عن ملف التراجع
        //    string rollbackPath = Server.MapPath($"~/Scripts/Rollbacks/{version}_rollback.sql");
        //    if (File.Exists(rollbackPath))
        //    {
        //        return File.ReadAllText(rollbackPath);
        //    }
        //    return null;
        //}

        private string GetLastSuccessfulVersion(SqlConnection conn)
        {
            string sql = @"
                SELECT TOP 1 ScriptVersion 
                FROM ScriptHistory 
                WHERE IsSuccess = 1 
                ORDER BY AppliedDate DESC";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                var result = cmd.ExecuteScalar();
                return result?.ToString() ?? "v1001";
            }
        }
        private List<string> GetRequiredUpdates(string currentVersion, string targetVersion)
        {
            string scriptsPath = Server.MapPath("~/Scripts");
            List<string> allVersions = Directory.GetFiles(scriptsPath, "*.sql")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(v => v)
                .ToList();

            int currentIndex = allVersions.IndexOf(currentVersion);
            int targetIndex = allVersions.IndexOf(targetVersion);

            if (currentIndex == -1 || targetIndex == -1 || targetIndex <= currentIndex)
            {
                return new List<string>();
            }

            return allVersions.GetRange(currentIndex + 1, targetIndex - currentIndex);
        }

        private void ShowMessage(string message, bool success)
        {
            pnlMessage.Visible = true;
            pnlMessage.CssClass = $"message {(success ? "success" : "error")}";
            litMessage.Text = message;
        }
    }
}