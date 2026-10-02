using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace OpenUGD.Tests
{
    [TestFixture]
    public class ConfigurationTests
    {
        private List<Lifetime.Definition> _definitions;
        private List<Context> _contexts;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<Lifetime.Definition>();
            _contexts = new List<Context>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _contexts.Count - 1; i >= 0; i--)
            {
                try
                {
                    _contexts[i].Dispose();
                }
                catch (Exception)
                {
                }
            }

            for (var i = _definitions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _definitions[i].Terminate();
                }
                catch (Exception)
                {
                }
            }

            _contexts.Clear();
            _definitions.Clear();
        }

        private Lifetime NewLifetime(string id)
        {
            var definition = Lifetime.Eternal.DefineNested(id);
            _definitions.Add(definition);
            return definition.Lifetime;
        }

        private static T RunSync<T>(Func<Task<T>> start, int timeoutMilliseconds = 15000)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                var task = start();
                try
                {
                    if (!task.Wait(timeoutMilliseconds))
                    {
                        Assert.Fail("Operation did not complete within " + timeoutMilliseconds + " ms.");
                    }
                }
                catch (AggregateException)
                {
                }

                return task.GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private Context Build(ContextBuilder builder)
        {
            var context = RunSync(() => builder.BuildAsync());
            Assert.IsNotNull(context, "BuildAsync must never hand back a null Context.");
            _contexts.Add(context);
            return context;
        }

        private ContextBuilder NewBuilder(string id = "root") =>
            Context.CreateBuilder(lifetime: NewLifetime(id));

        // ===== the map =====

        [Test]
        public void TheConfigurationIndexerReturnsNullForAnAbsentKey()
        {
            var configuration = new ConfigurationManager();

            Assert.IsNull(configuration["nothing:here"]);
        }

        [Test]
        public void AnExplicitSetOverridesAnyProvider()
        {
            var configuration = new ConfigurationManager();
            configuration["db:host"] = "explicit";
            configuration.AddDictionary(new Dictionary<string, string> {
                { "db:host", "from-provider" },
                { "db:port", "5432" },
            });

            Assert.AreEqual("explicit", configuration["db:host"]);
            Assert.AreEqual("5432", configuration["db:port"]);
        }

        [Test]
        public void TryGetReportsPresenceInsteadOfReturningNull()
        {
            var manager = new ConfigurationManager();
            manager["db:host"] = "localhost";
            IConfiguration configuration = manager;

            string value;
            Assert.IsTrue(configuration.TryGet("db:host", out value));
            Assert.AreEqual("localhost", value);

            string missing;
            Assert.IsFalse(configuration.TryGet("db:nope", out missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void GetSectionStripsThePrefix()
        {
            var configuration = new ConfigurationManager();
            configuration["db:host"] = "localhost";
            configuration["db:port"] = "5432";
            configuration["other:host"] = "elsewhere";

            var section = ((IConfiguration)configuration).GetSection("db");

            Assert.AreEqual("localhost", section["host"]);
            Assert.AreEqual("5432", section["port"]);
            Assert.IsNull(section["other:host"]);
        }

        [Test]
        public void GetBindsASectionToANewTypedObject()
        {
            var configuration = new ConfigurationManager();
            configuration["db:host"] = "localhost";
            configuration["db:port"] = "5432";

            var options = ((IConfiguration)configuration).Get<DbOptions>("db");

            Assert.AreEqual("localhost", options.Host);
            Assert.AreEqual(5432, options.Port);
        }

        [Test]
        public void BindFillsAnExistingObject()
        {
            var configuration = new ConfigurationManager();
            configuration["db:host"] = "localhost";

            var options = new DbOptions { Port = 1234 };
            ((IConfiguration)configuration).Bind("db", options);

            Assert.AreEqual("localhost", options.Host);
            Assert.AreEqual(1234, options.Port, "A key that is absent must leave the existing value alone.");
        }

        [Test]
        public void ConfigurationEnumeratesItsKeyValuePairs()
        {
            var configuration = new ConfigurationManager();
            configuration["a"] = "1";
            configuration["b"] = "2";

            var pairs = ((IConfiguration)configuration).ToDictionary(p => p.Key, p => p.Value);

            Assert.AreEqual("1", pairs["a"]);
            Assert.AreEqual("2", pairs["b"]);
        }

        [Test]
        public void AddJsonFlattensNestedObjectsAndArrays()
        {
            var configuration = new ConfigurationManager();
            configuration.AddJson("{\"audio\":{\"volume\":0.8},\"servers\":[{\"host\":\"eu\"}]}");

            Assert.AreEqual("0.8", configuration["Audio:Volume"]);
            Assert.AreEqual("eu", configuration["servers:0:host"]);
        }

        [Test]
        public void AddObjectFlattensAnExistingSettingsObject()
        {
            var configuration = new ConfigurationManager();
            configuration.AddObject(new DbOptions { Host = "localhost", Port = 5432 }, "db");

            Assert.AreEqual("localhost", configuration["db:host"]);
            Assert.AreEqual("5432", configuration["db:port"]);
        }

        // ===== failures =====

        [Test]
        public void AConfigurationValueThatCannotBeConvertedNamesTheKeyAndTheValue()
        {
            var configuration = new ConfigurationManager();
            configuration["db:port"] = "not-a-number";

            var error = Assert.Throws<ConfigurationException>(
                () => ((IConfiguration)configuration).Get<DbOptions>("db"));

            StringAssert.Contains("db:Port", error.Message);
            StringAssert.Contains("not-a-number", error.Message);
            Assert.IsNotNull(error.InnerException, "The parse failure must be kept as the inner exception.");
        }

        [Test]
        public void MalformedJsonIsReportedWithTheOffset()
        {
            var configuration = new ConfigurationManager();

            var error = Assert.Throws<ConfigurationException>(() => configuration.AddJson("{\"a\": }"));

            StringAssert.Contains("malformed", error.Message);
            StringAssert.Contains("character 6", error.Message);
        }

        // ===== AddConfiguration: the container integration =====

        [Test]
        public void ConfigurationIsFullyReadableDuringRegistrationSoRegistrationsCanBranchOnIt()
        {
            var builder = NewBuilder();
            var configuration = builder.AddConfiguration();
            configuration["feature:tooltips"] = "on";

            if (configuration["feature:tooltips"] == "on")
            {
                builder.Services.Add<TooltipsOn>().As<IFeature>();
            }
            else
            {
                builder.Services.Add<TooltipsOff>().As<IFeature>();
            }

            var context = Build(builder);

            Assert.IsInstanceOf<TooltipsOn>(context.Resolve<IFeature>());
        }

        [Test]
        public void AddConfigurationRegistersTheReturnedManagerAsIConfiguration()
        {
            var builder = NewBuilder();
            var manager = builder.AddConfiguration();
            manager["greeting"] = "hello";

            var context = Build(builder);

            var configuration = context.Resolve<IConfiguration>();
            Assert.AreSame(manager, configuration);
            Assert.AreEqual("hello", configuration["greeting"]);
        }

        [Test]
        public void AServiceCanTakeIConfigurationAsAConstructorParameter()
        {
            var builder = NewBuilder();
            builder.AddConfiguration()["greeting"] = "hello";
            builder.Services.Add<ConfiguredService>();

            var context = Build(builder);

            Assert.AreEqual("hello", context.Resolve<ConfiguredService>().Greeting);
        }

        [Test]
        public void WithoutAddConfigurationIConfigurationIsAnOrdinaryMissingDependency()
        {
            var builder = NewBuilder();
            builder.Services.Add<ConfiguredService>();

            var error = Assert.Throws<ContextException>(() => RunSync(() => builder.BuildAsync()));

            StringAssert.Contains(typeof(IConfiguration).FullName, error.Message);
        }

        [Test]
        public void AChildInheritsItsParentsConfigurationAndCanOverrideOneKey()
        {
            var parentBuilder = NewBuilder("parent");
            var parentConfiguration = parentBuilder.AddConfiguration();
            parentConfiguration["db:host"] = "live";
            parentConfiguration["db:port"] = "5432";
            var parent = Build(parentBuilder);

            var childBuilder = Context.CreateBuilder(lifetime: NewLifetime("child"), parent: parent);
            childBuilder.AddConfiguration()["db:host"] = "local";
            var child = Build(childBuilder);

            Assert.AreEqual("local", child.Resolve<IConfiguration>()["db:host"]);
            Assert.AreEqual("5432", child.Resolve<IConfiguration>()["db:port"]);
            Assert.AreEqual("live", parent.Resolve<IConfiguration>()["db:host"]);
        }

        [Test]
        public void AChildThatDoesNotAddConfigurationResolvesItsParents()
        {
            var parentBuilder = NewBuilder("parent");
            var parentConfiguration = parentBuilder.AddConfiguration();
            parentConfiguration["db:host"] = "live";
            var parent = Build(parentBuilder);

            var child = Build(Context.CreateBuilder(lifetime: NewLifetime("child"), parent: parent));

            Assert.AreSame(parentConfiguration, child.Resolve<IConfiguration>());
        }

        [Test]
        public void CallingAddConfigurationTwiceOnOneBuilderIsADuplicateRegistration()
        {
            var builder = NewBuilder();
            builder.AddConfiguration();
            builder.AddConfiguration();

            var error = Assert.Throws<ContextException>(() => RunSync(() => builder.BuildAsync()));

            StringAssert.Contains("registered twice", error.Message);
            StringAssert.Contains(typeof(IConfiguration).FullName, error.Message);
        }

        // ===== fixtures =====

        public interface IFeature { }

        public sealed class TooltipsOn : IFeature { }
        public sealed class TooltipsOff : IFeature { }

        public sealed class ConfiguredService
        {
            public ConfiguredService(IConfiguration configuration) { Greeting = configuration["greeting"]; }
            public string Greeting { get; }
        }

        public sealed class DbOptions
        {
            public string Host { get; set; }
            public int Port { get; set; }
        }
    }
}
