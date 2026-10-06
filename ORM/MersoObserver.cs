using System;
using System.Collections.Generic;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// MersoObserver - Model gözlemcisi base class
    /// Events'e alternatif, ayrı sınıfta tanımlanır
    /// </summary>
    public abstract class MersoObserver<T> where T : class
    {
        /// <summary>
        /// Model oluşturulmadan önce
        /// </summary>
        public virtual bool Creating(T model) => true;

        /// <summary>
        /// Model oluşturulduktan sonra
        /// </summary>
        public virtual void Created(T model) { }

        /// <summary>
        /// Model güncellenmeden önce
        /// </summary>
        public virtual bool Updating(T model) => true;

        /// <summary>
        /// Model güncellendikten sonra
        /// </summary>
        public virtual void Updated(T model) { }

        /// <summary>
        /// Model silinmeden önce
        /// </summary>
        public virtual bool Deleting(T model) => true;

        /// <summary>
        /// Model silindikten sonra
        /// </summary>
        public virtual void Deleted(T model) { }

        /// <summary>
        /// Model kaydedilmeden önce (create veya update)
        /// </summary>
        public virtual bool Saving(T model) => true;

        /// <summary>
        /// Model kaydedildikten sonra
        /// </summary>
        public virtual void Saved(T model) { }

        /// <summary>
        /// Model geri yüklenmeden önce
        /// </summary>
        public virtual bool Restoring(T model) => true;

        /// <summary>
        /// Model geri yüklendikten sonra
        /// </summary>
        public virtual void Restored(T model) { }
    }

    /// <summary>
    /// Observer Manager - Observer'ları yönet. Model Save/Delete/Restore işlemleri kayıtlı observer'ları tetikler.
    /// </summary>
    public static class ObserverManager
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<Type, List<object>> _observers = new Dictionary<Type, List<object>>();

        /// <summary>
        /// Observer kaydet
        /// </summary>
        public static void Register<T>(MersoObserver<T> observer) where T : class
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            lock (_lock)
            {
                var type = typeof(T);
                if (!_observers.TryGetValue(type, out var list))
                    _observers[type] = list = new List<object>();

                if (!list.Contains(observer))
                    list.Add(observer);
            }
        }

        /// <summary>
        /// Observer kaldır
        /// </summary>
        public static void Unregister<T>(MersoObserver<T> observer) where T : class
        {
            lock (_lock)
            {
                if (_observers.TryGetValue(typeof(T), out var list))
                    list.Remove(observer);
            }
        }

        /// <summary>
        /// Tüm observer'ları temizle
        /// </summary>
        public static void Clear<T>() where T : class
        {
            lock (_lock)
            {
                if (_observers.TryGetValue(typeof(T), out var list))
                    list.Clear();
            }
        }

        /// <summary>
        /// Tüm observer'ları temizle
        /// </summary>
        public static void ClearAll()
        {
            lock (_lock)
            {
                _observers.Clear();
            }
        }

        /// <summary>
        /// Observer'ları al (kopya liste — tetikleme sırasında kayıt değişebilir)
        /// </summary>
        internal static List<MersoObserver<T>> GetObservers<T>() where T : class
        {
            lock (_lock)
            {
                if (!_observers.TryGetValue(typeof(T), out var list) || list.Count == 0)
                    return new List<MersoObserver<T>>();

                return list.Cast<MersoObserver<T>>();
            }
        }

        /// <summary>
        /// Creating event'i tetikle
        /// </summary>
        internal static bool FireCreating<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                if (!observer.Creating(model))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Created event'i tetikle
        /// </summary>
        internal static void FireCreated<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                observer.Created(model);
            }
        }

        /// <summary>
        /// Updating event'i tetikle
        /// </summary>
        internal static bool FireUpdating<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                if (!observer.Updating(model))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Updated event'i tetikle
        /// </summary>
        internal static void FireUpdated<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                observer.Updated(model);
            }
        }

        /// <summary>
        /// Deleting event'i tetikle
        /// </summary>
        internal static bool FireDeleting<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                if (!observer.Deleting(model))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Deleted event'i tetikle
        /// </summary>
        internal static void FireDeleted<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                observer.Deleted(model);
            }
        }

        /// <summary>
        /// Saving event'i tetikle
        /// </summary>
        internal static bool FireSaving<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                if (!observer.Saving(model))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Saved event'i tetikle
        /// </summary>
        internal static void FireSaved<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                observer.Saved(model);
            }
        }

        /// <summary>
        /// Restoring event'i tetikle
        /// </summary>
        internal static bool FireRestoring<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                if (!observer.Restoring(model))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Restored event'i tetikle
        /// </summary>
        internal static void FireRestored<T>(T model) where T : class
        {
            foreach (var observer in GetObservers<T>())
            {
                observer.Restored(model);
            }
        }
    }

    /// <summary>
    /// LINQ extension for casting
    /// </summary>
    internal static class LinqExtensions
    {
        public static List<TResult> Cast<TResult>(this List<object> source)
        {
            var result = new List<TResult>();
            foreach (var item in source)
            {
                result.Add((TResult)item);
            }
            return result;
        }
    }
}
