using System;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// MersoEvents - Model lifecycle events interface (mersolution style)
    /// </summary>
    public interface IMersoEvents
    {
        bool OnMersoCreating();
        void OnMersoCreated();
        bool OnMersoUpdating();
        void OnMersoUpdated();
        bool OnMersoDeleting();
        void OnMersoDeleted();
        bool OnMersoSaving();
        void OnMersoSaved();
        bool OnMersoRestoring();
        void OnMersoRestored();
    }

    /// <summary>
    /// MersoEvents base class - inherit this in your model for event support
    /// Override only the events you need
    /// </summary>
    public abstract class MersoEventsBase : IMersoEvents
    {
        /// <summary>
        /// Called before model is created (INSERT)
        /// Return false to cancel the operation
        /// </summary>
        public virtual bool OnMersoCreating() => true;

        /// <summary>
        /// Called after model is created (INSERT)
        /// </summary>
        public virtual void OnMersoCreated() { }

        /// <summary>
        /// Called before model is updated (UPDATE)
        /// Return false to cancel the operation
        /// </summary>
        public virtual bool OnMersoUpdating() => true;

        /// <summary>
        /// Called after model is updated (UPDATE)
        /// </summary>
        public virtual void OnMersoUpdated() { }

        /// <summary>
        /// Called before model is deleted (DELETE)
        /// Return false to cancel the operation
        /// </summary>
        public virtual bool OnMersoDeleting() => true;

        /// <summary>
        /// Called after model is deleted (DELETE)
        /// </summary>
        public virtual void OnMersoDeleted() { }

        /// <summary>
        /// Called before model is saved (INSERT or UPDATE)
        /// Return false to cancel the operation
        /// </summary>
        public virtual bool OnMersoSaving() => true;

        /// <summary>
        /// Called after model is saved (INSERT or UPDATE)
        /// </summary>
        public virtual void OnMersoSaved() { }

        /// <summary>
        /// Called before soft deleted model is restored
        /// Return false to cancel the operation
        /// </summary>
        public virtual bool OnMersoRestoring() => true;

        /// <summary>
        /// Called after soft deleted model is restored
        /// </summary>
        public virtual void OnMersoRestored() { }
    }

    /// <summary>
    /// MersoEvent types
    /// </summary>
    public enum MersoEventType
    {
        Creating,
        Created,
        Updating,
        Updated,
        Deleting,
        Deleted,
        Saving,
        Saved,
        Restoring,
        Restored
    }
}
