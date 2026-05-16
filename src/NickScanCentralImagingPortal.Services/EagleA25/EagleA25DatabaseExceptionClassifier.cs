namespace NickScanCentralImagingPortal.Services.EagleA25
{
    public static class EagleA25DatabaseExceptionClassifier
    {
        public const string ScannerTablesMigrationId = "20260514193000_AddEagleA25ScannerTables";

        private const string PostgresUndefinedTableSqlState = "42P01";

        public static bool IsPostgresUndefinedTable(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
                if (string.Equals(sqlState, PostgresUndefinedTableSqlState, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (current.Message.Contains(PostgresUndefinedTableSqlState, StringComparison.OrdinalIgnoreCase)
                    && current.Message.Contains("relation", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (current.Message.Contains("relation", StringComparison.OrdinalIgnoreCase)
                    && current.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
