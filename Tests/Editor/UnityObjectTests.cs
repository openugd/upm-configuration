using NUnit.Framework;
using UnityEngine;

namespace OpenUGD.Tests
{
    /// <summary>
    /// Unity's own members, with a real <see cref="ScriptableObject"/>: AddObject and Bind see its own public
    /// members, not the engine's (<c>name</c>, <c>hideFlags</c>), and Get never creates a <see cref="Object"/>
    /// by reflection.
    /// </summary>
    [TestFixture]
    [Category("RequiresUnity")]
    public class UnityObjectTests
    {
        private VolumeSettings _asset;

        [SetUp]
        public void CreateAsset()
        {
            _asset = ScriptableObject.CreateInstance<VolumeSettings>();
            _asset.name = "Audio";
            _asset.hideFlags = HideFlags.DontSave;
        }

        [TearDown]
        public void DestroyAsset()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        [Test]
        public void AddObjectWalksTheAssetsOwnMembersOnly()
        {
            _asset.Volume = 0.5f;

            var configuration = new ConfigurationManager().AddObject(_asset, "Audio");
            var keys = ((IConfiguration)configuration).GetSection("Audio");

            Assert.AreEqual("0.5", configuration["Audio:Volume"]);
            Assert.AreEqual("true", configuration["Audio:Muted"]);
            Assert.IsNull(configuration["Audio:name"]);
            Assert.IsNull(configuration["Audio:hideFlags"]);
            CollectionAssert.AreEquivalent(new[] { "Volume", "Muted" }, System.Linq.Enumerable.Select(keys, p => p.Key));
        }

        [Test]
        public void BindFillsTheAssetsOwnMembersAndLeavesTheEnginesAlone()
        {
            var configuration = new ConfigurationManager();
            configuration["Audio:Volume"] = "0.25";
            configuration["Audio:name"] = "renamed";
            configuration["Audio:hideFlags"] = "0";

            ((IConfiguration)configuration).Bind("Audio", _asset);

            Assert.AreEqual(0.25f, _asset.Volume);
            Assert.AreEqual("Audio", _asset.name);
            Assert.AreEqual(HideFlags.DontSave, _asset.hideFlags);
        }

        [Test]
        public void GetRefusesToCreateAUnityObject()
        {
            var configuration = new ConfigurationManager();
            configuration["Audio:Volume"] = "0.25";

            var error = Assert.Throws<ConfigurationException>(
                () => ((IConfiguration)configuration).Get<VolumeSettings>("Audio"));

            StringAssert.Contains("UnityEngine.Object", error.Message);
            StringAssert.Contains("ScriptableObject.CreateInstance", error.Message);
        }

        [Test]
        public void UnityValueTypesBindAndRoundTrip()
        {
            var original = new SpawnPoint { Position = new Vector3(1, 2, 3), Tint = new Color(0.5f, 0.25f, 1f, 1f) };

            var configuration = new ConfigurationManager().AddObject(original, "Spawn");
            configuration["Spawn:Position:y"] = "5";
            var copy = ((IConfiguration)configuration).Get<SpawnPoint>("Spawn");

            Assert.AreEqual(new Vector3(1, 5, 3), copy.Position);
            Assert.AreEqual(original.Tint, copy.Tint);
        }

        [Test]
        public void AnAnimationCurveIsNeverCloned()
        {
            var falloff = new Falloff();
            var curve = falloff.Curve;

            var configuration = new ConfigurationManager().AddJson("{\"F\":{\"Curve\":{\"preWrapMode\":\"Loop\"}}}");
            ((IConfiguration)configuration).Bind("F", falloff);

            Assert.AreSame(curve, falloff.Curve);
            Assert.AreEqual(0.5f, falloff.Curve.Evaluate(0.5f), 1e-5f);
        }

        public sealed class Falloff
        {
            public AnimationCurve Curve = AnimationCurve.Linear(0, 0, 1, 1);
        }

        public sealed class SpawnPoint
        {
            public Vector3 Position;
            public Color Tint = Color.white;
        }

        public sealed class VolumeSettings : ScriptableObject
        {
            public float Volume = 1f;
            public bool Muted { get; set; } = true;
        }
    }
}
