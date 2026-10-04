using System;
using System.Runtime.CompilerServices;

namespace OpenUGD
{
    /// <summary>
    /// The one place configuration meets the container: an opt-in registration on a
    /// <see cref="ContextBuilder"/>. Nothing registers an <see cref="IConfiguration"/> automatically: a context
    /// has one only if its builder, or the builder of one of its ancestors, calls this method or registers its
    /// own <see cref="IConfiguration"/>.
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
        /// <b>Writable until the build, read-only from it.</b> The returned manager is the registered one, so
        /// what you write before <see cref="ContextBuilder.BuildAsync"/> is what services see. The build
        /// freezes it when it constructs the <see cref="IConfiguration"/> registration, before any service
        /// that obtains it as <see cref="IConfiguration"/> and before the boot phases: from then on a write —
        /// through the manager you hold, or through an <see cref="IConfiguration"/> a service casts back to
        /// <see cref="ConfigurationManager"/> — throws <see cref="InvalidOperationException"/>. Hand the
        /// manager to services only as <see cref="IConfiguration"/>: registered again under its own type, or
        /// captured by an earlier registration's factory, it can be written until the freeze. Register your
        /// own <see cref="IConfiguration"/> implementation instead for values that change at run time.
        /// </para>
        /// <para>
        /// <b>Inheritance from a parent context.</b> When the parent resolves an <see cref="IConfiguration"/>,
        /// the new manager reads through to it for every key it does not hold itself, at lookup time, so a
        /// value set here — through the indexer or a provider — overrides the inherited one, a key set here to
        /// <c>null</c> hides it, and the parent's configuration is never changed. Nothing is copied: a parent
        /// whose <see cref="IConfiguration"/> is a live implementation stays visible as it changes. A parent
        /// that has already ended contributes nothing (the child's build is cancelled anyway). A child builder
        /// that does not call this method needs nothing: it resolves its parent's
        /// <see cref="IConfiguration"/> like any other inherited service.
        /// </para>
        /// <para>
        /// <b>Once per builder.</b> A second call registers a second <see cref="IConfiguration"/>, which
        /// <see cref="ContextBuilder.BuildAsync"/> rejects as a duplicate registration naming both call
        /// sites.
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

            object inherited = null;
            var parent = builder.Parent;
            if (parent != null && !parent.Lifetime.IsTerminated)
            {
                // A parent that has ended has nothing to inherit, and the build of this builder is cancelled
                // anyway, as for any builder whose parent is gone; registering must not throw.
                try
                {
                    parent.TryResolve(typeof(IConfiguration), out inherited);
                }
                catch (ObjectDisposedException)
                {
                    inherited = null;
                }
            }

            var configuration = new ConfigurationManager((IConfiguration)inherited);

            // A factory, not AddInstance: it runs during BuildAsync, before any service that obtains
            // IConfiguration, which is the moment the manager stops accepting writes.
            builder.Services.Add<IConfiguration>(context =>
            {
                configuration.Freeze();
                return configuration;
            }, file, line);
            return configuration;
        }
    }
}
