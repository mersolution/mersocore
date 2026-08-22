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
}
