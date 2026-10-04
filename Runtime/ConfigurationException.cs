using System;

namespace OpenUGD
{
    /// <summary>
    /// Configuration could not be read: a JSON document that is malformed, a value that cannot be converted
    /// to the member it binds to, a settings type that cannot be created (no public parameterless
    /// constructor, abstract, or a <c>UnityEngine.Object</c>), or an object graph too deep to flatten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message names what failed — the character offset in a JSON document, the key, value and target
    /// type of a conversion — and a conversion failure carries the parse exception as its
    /// <see cref="Exception.InnerException"/>.
    /// </para>
    /// <para>
    /// <b>It is not the only exception this package throws.</b> A bad argument is still an
    /// <see cref="ArgumentNullException"/> or <see cref="ArgumentException"/>, and a write to a
    /// <see cref="ConfigurationManager"/> whose context has been built is an
    /// <see cref="InvalidOperationException"/>: that is a misuse of the API, not unreadable configuration.
    /// </para>
    /// </remarks>
    public sealed class ConfigurationException : Exception
    {
        /// <summary>
        /// Creates a failure with no underlying exception.
        /// </summary>
        /// <param name="message">What could not be read, and where.</param>
        public ConfigurationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Creates a failure that wraps the exception a conversion threw.
        /// </summary>
        /// <param name="message">What could not be read, and where.</param>
        /// <param name="innerException">The original exception, kept rather than swallowed.</param>
        public ConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
