using System;
using System.Runtime.CompilerServices;

namespace OpenUGD
{
    /// <summary>
    /// The one place configuration meets the container: an opt-in registration on a
    /// <see cref="ContextBuilder"/>. A context whose builder never calls it has no configuration at all.
    /// </summary>
    public static class ContextBuilderConfigurationExtensions
    {
        /// <summary>
        /// Creates a <see cref="ConfigurationManager"/>, registers it in the context being built as
        /// <see cref="IConfiguration"/>, and returns it, so the composition root can fill it and branch on
        /// its values while it is still registering services.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Inheritance from a parent context.</b> When the parent resolves an <see cref="IConfiguration"/>,
        /// every pair it enumerates is copied into the new manager's provider layer, so a value set through
        /// the indexer here overrides the inherited one, and the parent's configuration is not changed. The
        /// copy is taken now, once. A child builder that does not call this method needs nothing: it
        /// resolves its parent's <see cref="IConfiguration"/> like any other inherited service.
        /// </para>
        /// <para>
        /// <b>Once per builder.</b> A second call registers a second <see cref="IConfiguration"/>, which
        /// <see cref="ContextBuilder.BuildAsync"/> rejects as a duplicate registration naming both call
        /// sites.
        /// </para>
        /// <para>
        /// <b>The returned manager is the registered one.</b> What you write before the build is what services
        /// see. What you write after it is visible to them too, because nothing freezes the manager.
        /// </para>
        /// </remarks>
        /// <param name="builder">The builder of the context that should hold the configuration.</param>
        /// <param name="file">[compiler-supplied] The call site's file, for diagnostics. Do not pass
        /// it.</param>
        /// <param name="line">[compiler-supplied] The call site's line. Do not pass it.</param>
        /// <returns>The manager registered as <see cref="IConfiguration"/>, to be filled by the caller.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The builder has already built its context.</exception>
        public static ConfigurationManager AddConfiguration(this ContextBuilder builder,
            [CallerFilePath] string file = null, [CallerLineNumber] int line = 0)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            var configuration = new ConfigurationManager();

            object inherited;
            var parent = builder.Parent;
            if (parent != null && parent.TryResolve(typeof(IConfiguration), out inherited))
            {
                foreach (var pair in (IConfiguration)inherited)
                {
                    configuration.SetProviderValue(pair.Key, pair.Value);
                }
            }

            builder.Services.AddInstance<IConfiguration>(configuration, file, line);
            return configuration;
        }
    }
}
