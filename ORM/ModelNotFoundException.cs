using System;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Exception thrown when a model is not found
    /// </summary>
    public class ModelNotFoundException : Exception
    {
        public ModelNotFoundException() : base("Model not found")
        {
        }

        public ModelNotFoundException(string message) : base(message)
        {
        }

        public ModelNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown by Fill() / Create(dictionary) for a [Guarded] or not-[Fillable] key when
    /// <c>ModelBase.StrictMassAssignment</c> is true (otherwise such keys are skipped silently)
    /// </summary>
    public class MassAssignmentException : Exception
    {
        public MassAssignmentException(string message) : base(message)
        {
        }
    }
}
