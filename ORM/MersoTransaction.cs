using System;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// MersoTransaction - Transaction yönetimi için helper
    /// </summary>
    public static class MersoTransaction
    {
        /// <summary>
        /// Transaction içinde çalıştır
        /// </summary>
        public static T Run<T>(Func<T> action)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                db.BeginTransaction();
                try
                {
                    var result = action();
                    db.CommitTransaction();
                    return result;
                }
                catch
                {
                    db.RollbackTransaction();
                    throw;
                }
            }
        }

        /// <summary>
        /// Transaction içinde çalıştır (void)
        /// </summary>
        public static void Run(Action action)
        {
            using (var db = ModelBase.ConnectionFactory())
            {
                db.BeginTransaction();
                try
                {
                    action();
                    db.CommitTransaction();
                }
                catch
                {
                    db.RollbackTransaction();
                    throw;
                }
            }
        }

        /// <summary>
        /// Transaction içinde çalıştır, hata olursa false döndür
        /// </summary>
        public static bool TryRun(Action action)
        {
            try
            {
                Run(action);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
