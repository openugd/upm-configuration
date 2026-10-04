using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OpenUGD.Tests
{
    /// <summary>
    /// <see cref="ConfigurationExtensions.Get{T}"/> and <see cref="ConfigurationExtensions.Bind"/> beyond flat
    /// classes: scalars (CX-17), structs (CX-18), collections and the AddObject round trip (CX-19), and the
    /// members of Unity's own types (CX-27).
    /// </summary>
    [TestFixture]
    public class BindingTests
    {
        private static IConfiguration From(params string[] pairs)
        {
            var manager = new ConfigurationManager();
            for (var i = 0; i < pairs.Length; i += 2) manager[pairs[i]] = pairs[i + 1];
            return manager;
        }

        // ===== CX-17: Get<T> of a scalar =====

        [Test]
        public void GetReadsAScalarFromItsKey()
        {
            var configuration = From("Save:Slot", "3", "Save:Name", "hero", "Save:Auto", "true",
                "Save:Every", "00:05:00", "Save:Level", "warning", "Save:Ratio", "0.25");

            Assert.AreEqual(3, configuration.Get<int>("Save:Slot"));
            Assert.AreEqual("hero", configuration.Get<string>("Save:Name"));
            Assert.IsTrue(configuration.Get<bool>("Save:Auto"));
            Assert.AreEqual(TimeSpan.FromMinutes(5), configuration.Get<TimeSpan>("Save:Every"));
            Assert.AreEqual(Level.Warning, configuration.Get<Level>("Save:Level"));
            Assert.AreEqual(0.25m, configuration.Get<decimal>("Save:Ratio"));
            Assert.AreEqual(3, configuration.Get<int?>("Save:Slot"));
        }

        [Test]
        public void GetOfAnAbsentScalarIsTheDefault()
        {
            var configuration = From();

            Assert.AreEqual(0, configuration.Get<int>("Save:Slot"));
            Assert.IsNull(configuration.Get<string>("Save:Name"));
            Assert.IsNull(configuration.Get<int?>("Save:Slot"));
        }

        [Test]
        public void GetOfAScalarNeedsAKey()
        {
            var error = Assert.Throws<ArgumentException>(() => From("a", "1").Get<int>());

            StringAssert.Contains("System.Int32", error.Message);
        }

        [Test]
        public void GetOfAnUnreadableScalarNamesTheKey()
        {
            var error = Assert.Throws<ConfigurationException>(() => From("Save:Slot", "three").Get<int>("Save:Slot"));

            StringAssert.Contains("Save:Slot", error.Message);
            StringAssert.Contains("three", error.Message);
        }

        [Test]
        public void GetOfATypeWithoutAParameterlessConstructorStillSaysWhy()
        {
            var error = Assert.Throws<ConfigurationException>(() => From("x:Value", "1").Get<NoDefault>("x"));

            StringAssert.Contains(typeof(NoDefault).FullName.Replace('+', '.'), error.Message.Replace('+', '.'));
            StringAssert.Contains("parameterless constructor", error.Message);
        }

        // ===== CX-18: structs =====

        [Test]
        public void BindFillsAStructMemberFromNestedKeys()
        {
            var window = From("Window:Size:X", "1280", "Window:Size:Y", "720").Get<WindowOptions>("Window");

            Assert.AreEqual(1280, window.Size.X);
            Assert.AreEqual(720, window.Size.Y);
        }

        [Test]
        public void AStructMemberKeepsTheFieldsNoKeyNames()
        {
            var window = new WindowOptions { Size = new Size { X = 640, Y = 480 } };

            From("Window:Size:Y", "720").Bind("Window", window);

            Assert.AreEqual(640, window.Size.X, "An unconfigured struct field keeps its value.");
            Assert.AreEqual(720, window.Size.Y);
        }

        [Test]
        public void ANullableStructMemberIsCreatedWhenItHasKeys()
        {
            var window = From("Window:Origin:X", "5").Get<WindowOptions>("Window");

            Assert.IsTrue(window.Origin.HasValue);
            Assert.AreEqual(5, window.Origin.Value.X);
            Assert.IsNull(From().Get<WindowOptions>("Window").Origin, "No key under it leaves it null.");
        }

        [Test]
        public void GetOfAStructBindsIt()
        {
            var size = From("Size:X", "3", "Size:Y", "4").Get<Size>("Size");

            Assert.AreEqual(3, size.X);
            Assert.AreEqual(4, size.Y);
        }

        [Test]
        public void ANestedClassIsBoundIntoACopyOfTheInstanceTheMemberHolds()
        {
            var options = new ServerOptions { Retry = new RetryOptions { Count = 9, DelaySeconds = 7 } };
            var held = options.Retry;

            From("Server:Retry:Count", "2").Bind("Server", options);

            Assert.AreNotSame(held, options.Retry);
            Assert.AreEqual(2, options.Retry.Count);
            Assert.AreEqual(7, options.Retry.DelaySeconds, "An unconfigured nested member keeps its value.");
            Assert.AreEqual(9, held.Count, "The held instance itself is not changed.");
        }

        // ===== CX-19: collections =====

        [Test]
        public void JsonArraysBindIntoListsArraysAndReadOnlyLists()
        {
            var configuration = new ConfigurationManager().AddJson(
                "{\"Lobby\":{\"Regions\":[\"eu\",\"us\"],\"Ports\":[7777,7778],\"Weights\":[0.5,1.5],\"Tags\":[\"a\",\"b\",\"a\"]}}");

            var lobby = ((IConfiguration)configuration).Get<LobbyOptions>("Lobby");

            CollectionAssert.AreEqual(new[] { "eu", "us" }, lobby.Regions);
            CollectionAssert.AreEqual(new[] { 7777, 7778 }, lobby.Ports);
            CollectionAssert.AreEqual(new[] { 0.5, 1.5 }, lobby.Weights);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, lobby.Tags);
        }

        [Test]
        public void AListOfSettingsObjectsIsBoundElementByElement()
        {
            var configuration = new ConfigurationManager().AddJson(
                "{\"Servers\":[{\"Url\":\"https://eu\",\"Retry\":{\"Count\":2}},{\"Url\":\"https://us\"}]}");

            var servers = ((IConfiguration)configuration).Get<List<ServerOptions>>("Servers");

            Assert.AreEqual(2, servers.Count);
            Assert.AreEqual("https://eu", servers[0].Url);
            Assert.AreEqual(2, servers[0].Retry.Count);
            Assert.AreEqual("https://us", servers[1].Url);
            Assert.IsNull(servers[1].Retry);
        }

        [Test]
        public void NestedCollectionsBind()
        {
            var configuration = new ConfigurationManager().AddJson("{\"Grid\":[[1,2],[3]]}");

            var grid = ((IConfiguration)configuration).Get<int[][]>("Grid");

            Assert.AreEqual(2, grid.Length);
            CollectionAssert.AreEqual(new[] { 1, 2 }, grid[0]);
            CollectionAssert.AreEqual(new[] { 3 }, grid[1]);
        }

        [Test]
        public void AConfiguredListReplacesTheDefaultAndAnUnconfiguredOneStays()
        {
            var lobby = new LobbyOptions();
            From("Lobby:Regions:0", "asia").Bind("Lobby", lobby);

            CollectionAssert.AreEqual(new[] { "asia" }, lobby.Regions, "The configured list is the whole list.");
            CollectionAssert.AreEqual(new[] { 1 }, lobby.Ports, "No indexed key leaves the default alone.");
        }

        [Test]
        public void ElementsFollowTheNumericOrderOfTheIndicesAndSkipAMissingOne()
        {
            var manager = new ConfigurationManager();
            for (var i = 0; i < 12; i++) manager["Lobby:Ports:" + i] = (8000 + i).ToString();
            manager["Lobby:Ports:4"] = null; // masked
            manager["Lobby:Ports:x"] = "not an index";

            var ports = ((IConfiguration)manager).Get<LobbyOptions>("Lobby").Ports;

            CollectionAssert.AreEqual(Enumerable.Range(0, 12).Where(i => i != 4).Select(i => 8000 + i), ports);
        }

        [Test]
        public void AnAbsentCollectionReadsAsEmpty()
        {
            CollectionAssert.IsEmpty(From().Get<List<string>>("Regions"));
            CollectionAssert.IsEmpty(From().Get<string[]>("Regions"));
        }

        [Test]
        public void AddObjectAndBindRoundTripCollectionsAndStructs()
        {
            var original = new LobbyOptions {
                Regions = new List<string> { "eu", "us" },
                Ports = new[] { 1, 2, 3 },
                Weights = new List<double> { 0.5 },
                Tags = new HashSet<string> { "ranked" },
                Servers = new List<ServerOptions> {
                    new ServerOptions { Url = "https://eu", Retry = new RetryOptions { Count = 4, DelaySeconds = 2 } },
                },
                Area = new Size { X = 10, Y = 20 },
            };

            var copy = ((IConfiguration)new ConfigurationManager().AddObject(original, "Lobby")).Get<LobbyOptions>("Lobby");

            CollectionAssert.AreEqual(original.Regions, copy.Regions);
            CollectionAssert.AreEqual(original.Ports, copy.Ports);
            CollectionAssert.AreEqual(original.Weights, copy.Weights);
            CollectionAssert.AreEquivalent(original.Tags, copy.Tags);
            Assert.AreEqual(1, copy.Servers.Count);
            Assert.AreEqual("https://eu", copy.Servers[0].Url);
            Assert.AreEqual(4, copy.Servers[0].Retry.Count);
            Assert.AreEqual(2, copy.Servers[0].Retry.DelaySeconds);
            Assert.AreEqual(original.Area, copy.Area);
        }

        [Test]
        public void AKeyOnACollectionMemberItselfFailsLoudly()
        {
            var error = Assert.Throws<ConfigurationException>(() => From("Lobby:Regions", "eu,us").Get<LobbyOptions>("Lobby"));

            StringAssert.Contains("Lobby:Regions", error.Message);
        }

        [Test]
        public void ASharedDefaultInstanceIsNotChangedByBinding()
        {
            var configuration = From("A:Retry:Count", "9");

            var a = configuration.Get<SharingServer>("A");
            var b = configuration.Get<SharingServer>("B");

            Assert.AreEqual(9, a.Retry.Count);
            Assert.AreEqual(1, a.Retry.DelaySeconds, "The copy keeps the held instance's other values.");
            Assert.AreEqual(3, b.Retry.Count, "Another section still starts from the untouched default.");
            Assert.AreEqual(3, SharingServer.DefaultRetry.Count);
            Assert.AreSame(SharingServer.DefaultRetry, b.Retry, "With no key under it the member is left alone.");
        }

        [Test]
        public void TwoMembersHoldingOneInstanceAreBoundSeparately()
        {
            var pair = From("P:First:Count", "5", "P:Second:Count", "7").Get<RetryPair>("P");

            Assert.AreEqual(5, pair.First.Count);
            Assert.AreEqual(7, pair.Second.Count);
        }

        [Test]
        public void AnObjectThatOwnsAResourceIsBoundInPlaceNeverCopied()
        {
            var holder = new ResourceHolder();
            var curve = holder.Curve;
            var finalizable = holder.Finalizable;

            From("H:Curve:preWrapMode", "2", "H:Finalizable:Count", "4").Bind("H", holder);

            Assert.AreSame(curve, holder.Curve, "A Unity class with a native pointer must never be cloned.");
            Assert.AreSame(finalizable, holder.Finalizable, "Nor any class with a finalizer.");
            Assert.AreEqual(4, holder.Finalizable.Count, "It is still bound, in place.");
        }

        [Test]
        public void AMethodNamedFinalizeIsNotAFinalizer()
        {
            var holder = new HelperHolder();
            var held = holder.Options;

            From("H:Options:Count", "5").Bind("H", holder);

            Assert.AreNotSame(held, holder.Options, "Finalize(bool) is a helper, so the object is copied as usual.");
            Assert.AreEqual(5, holder.Options.Count);
            Assert.AreEqual(1, held.Count);
        }

        [Test]
        public void AFinalizeDeclaredWithNewIsNotAFinalizer()
        {
            var holder = new NewFinalizeHolder();
            var plain = holder.Plain;

            From("H:Plain:Count", "5", "H:Shared:Count", "6", "H:Derived:Count", "7").Bind("H", holder);

            Assert.AreNotSame(plain, holder.Plain, "public new void Finalize() overrides nothing, so it is copied.");
            Assert.AreEqual(1, plain.Count);
            Assert.AreEqual(6, holder.Shared.Count);
            Assert.AreEqual(1, NewFinalizeHolder.StaticShared.Count, "A static default with private Finalize() stays.");
            Assert.AreSame(holder.DerivedHeld, holder.Derived, "A real finalizer in a base class still binds in place.");
            Assert.AreEqual(7, holder.Derived.Count);
        }

        [Test]
        public void ANegativeZeroRoundTripsWithItsSign()
        {
            var copy = ((IConfiguration)new ConfigurationManager().AddObject(new Precise { Speed = -0f, Ratio = -0.0 }, "P"))
                .Get<Precise>("P");

            Assert.IsTrue(float.IsNegativeInfinity(1f / copy.Speed), "-0f keeps its sign.");
            Assert.IsTrue(double.IsNegativeInfinity(1.0 / copy.Ratio), "-0.0 keeps its sign.");
        }

        [Test]
        public void WhatTheMemberHoldsDecidesNotHowItIsDeclared()
        {
            var holder = new LooseHolder();
            var text = holder.Extra;

            From("S:Extra:x", "1", "S:Settings:Count", "6").Bind("S", holder);

            Assert.AreSame(text, holder.Extra, "A string held in an object member is left alone.");
            Assert.AreEqual(6, ((RetryOptions)holder.Settings).Count, "A settings object held there is bound.");
        }

        [Test]
        public void FloatsAndDoublesRoundTripThroughAddObjectExactly()
        {
            var original = new Precise { Speed = 0.3f * 3, Exact = 1.0000001f, Ratio = 0.1 + 0.2, Third = 1.0 / 3 };

            var copy = ((IConfiguration)new ConfigurationManager().AddObject(original, "P")).Get<Precise>("P");

            Assert.AreEqual(original.Speed, copy.Speed);
            Assert.AreEqual(original.Exact, copy.Exact);
            Assert.AreEqual(original.Ratio, copy.Ratio);
            Assert.AreEqual(original.Third, copy.Third);
        }

        [Test]
        public void IndexedKeysWithNoReadableElementLeaveTheMemberAlone()
        {
            var holder = new AbstractHolder();
            var held = holder.Items;

            From("H:Items:0:Name", "x").Bind("H", holder);

            Assert.AreSame(held, holder.Items, "An interface element type cannot be created: nothing is wiped.");
            Assert.AreEqual(1, holder.Items.Count);
        }

        [Test]
        public void DatesRoundTripThroughAddObjectWithFractionsAndKind()
        {
            var original = new Schedule {
                At = new DateTime(2026, 10, 3, 12, 0, 0, 123, DateTimeKind.Utc),
                Offset = new DateTimeOffset(2026, 10, 3, 12, 0, 0, 456, TimeSpan.FromHours(1)),
            };

            var copy = ((IConfiguration)new ConfigurationManager().AddObject(original, "S")).Get<Schedule>("S");

            Assert.AreEqual(original.At, copy.At);
            Assert.AreEqual(DateTimeKind.Utc, copy.At.Kind);
            Assert.AreEqual(original.Offset, copy.Offset);
            Assert.AreEqual(original.Offset.Offset, copy.Offset.Offset);
        }

        // ===== CX-27: members Unity's own types declare =====

        [Test]
        public void MembersDeclaredByUnityTypesAreNeitherWalkedNorBound()
        {
            var asset = new GameSettings { name = "asset name", hideFlags = 61, Volume = 0.7f };

            var configuration = new ConfigurationManager().AddObject(asset, "Game");

            Assert.AreEqual("0.7", configuration["Game:Volume"]);
            Assert.IsNull(configuration["Game:name"], "name is engine state, not a setting.");
            Assert.IsNull(configuration["Game:hideFlags"]);

            configuration["Game:name"] = "renamed";
            configuration["Game:Volume"] = "0.2";
            ((IConfiguration)configuration).Bind("Game", asset);

            Assert.AreEqual("asset name", asset.name);
            Assert.AreEqual(0.2f, asset.Volume);
        }

        [Test]
        public void UnityValueTypesBindAndRoundTripLikeAnyStruct()
        {
            var original = new Spawn { Position = new UnityEngine.ConfigurationTestDoubles.VectorLike { x = 1, y = 2, z = 3 } };

            var configuration = new ConfigurationManager().AddObject(original, "S");
            Assert.AreEqual("1", configuration["S:Position:x"]);

            configuration["S:Position:y"] = "5";
            var copy = ((IConfiguration)configuration).Get<Spawn>("S");

            Assert.AreEqual(1f, copy.Position.x);
            Assert.AreEqual(5f, copy.Position.y);
            Assert.AreEqual(3f, copy.Position.z);
        }

        // ===== fixtures =====

        public enum Level { Info, Warning }

        public struct Size
        {
            public int X;
            public int Y { get; set; }
        }

        public sealed class WindowOptions
        {
            public Size Size;
            public Size? Origin { get; set; }
        }

        public sealed class RetryOptions
        {
            public int Count { get; set; } = 3;
            public int DelaySeconds { get; set; } = 1;
        }

        public sealed class ServerOptions
        {
            public string Url { get; set; }
            public RetryOptions Retry { get; set; }
        }

        public sealed class LobbyOptions
        {
            public List<string> Regions { get; set; } = new List<string> { "eu" };
            public int[] Ports { get; set; } = { 1 };
            public IReadOnlyList<double> Weights { get; set; } = new double[0];
            public HashSet<string> Tags { get; set; } = new HashSet<string>();
            public List<ServerOptions> Servers { get; set; } = new List<ServerOptions>();
            public Size Area;
        }

        public sealed class NoDefault
        {
            public NoDefault(int value) { Value = value; }
            public int Value { get; set; }
        }

        public sealed class GameSettings : UnityEngine.ConfigurationTestDoubles.EngineObject
        {
            public float Volume = 1f;
        }

        public sealed class SharingServer
        {
            public static readonly RetryOptions DefaultRetry = new RetryOptions();
            public RetryOptions Retry { get; set; } = DefaultRetry;
        }

        public sealed class RetryPair
        {
            public RetryOptions First;
            public RetryOptions Second;

            public RetryPair() { First = Second = new RetryOptions(); }
        }

        public interface INamed
        {
            string Name { get; set; }
        }

        public sealed class Named : INamed
        {
            public string Name { get; set; }
        }

        public sealed class AbstractHolder
        {
            public List<INamed> Items { get; set; } = new List<INamed> { new Named { Name = "keep" } };
        }

        public sealed class Schedule
        {
            public DateTime At { get; set; }
            public DateTimeOffset Offset { get; set; }
        }

        public sealed class Finalizable
        {
            ~Finalizable() { }
            public int Count { get; set; }
            public void Finalize<T>() { } // next to the destructor, a by-name lookup would be ambiguous
        }

        public sealed class ResourceHolder
        {
            public UnityEngine.ConfigurationTestDoubles.NativeCurve Curve = new UnityEngine.ConfigurationTestDoubles.NativeCurve();
            public Finalizable Finalizable = new Finalizable();
        }

        public sealed class WithHelper
        {
            public int Count { get; set; } = 1;
#pragma warning disable 465 // a method named Finalize on purpose: it is not a finalizer
            private void Finalize(bool flush) { }
#pragma warning restore 465
        }

#pragma warning disable 465, 109 // methods named Finalize on purpose: none of them is a finalizer
        public sealed class NewFinalize
        {
            public int Count { get; set; } = 1;
            public new void Finalize() { }
            public void Finalize<T>() { }
        }

        public sealed class PrivateFinalize
        {
            public int Count { get; set; } = 1;
            private void Finalize() { }
        }

        public class DestructorBase
        {
            ~DestructorBase() { }
            public int Count { get; set; } = 1;
        }

        public sealed class HidesTheFinalizer : DestructorBase
        {
            public new void Finalize() { }
        }
#pragma warning restore 465, 109

        public sealed class NewFinalizeHolder
        {
            public static readonly PrivateFinalize StaticShared = new PrivateFinalize();
            public NewFinalize Plain = new NewFinalize();
            public PrivateFinalize Shared = StaticShared;
            public HidesTheFinalizer Derived = new HidesTheFinalizer();
            public readonly HidesTheFinalizer DerivedHeld;

            public NewFinalizeHolder() { DerivedHeld = Derived; }
        }

        public sealed class HelperHolder
        {
            public WithHelper Options = new WithHelper();
        }

        public sealed class LooseHolder
        {
            public object Extra = "abcdefgh";
            public object Settings = new RetryOptions();
        }

        public sealed class Precise
        {
            public float Speed;
            public float Exact;
            public double Ratio;
            public double Third;
        }

        public sealed class Spawn
        {
            public UnityEngine.ConfigurationTestDoubles.VectorLike Position;
        }
    }
}

namespace UnityEngine.ConfigurationTestDoubles
{
    /// Stands in for UnityEngine.Object in the engine-free run: what matters is the namespace that declares
    /// these members, which is how the binder recognises engine state.
    public class EngineObject
    {
        public string name { get; set; }
        public int hideFlags { get; set; }
    }

    /// Stands in for AnimationCurve: a Unity class that is not a UnityEngine.Object and owns a native pointer.
    public class NativeCurve
    {
        private IntPtr _ptr = new IntPtr(42);
        public int preWrapMode { get; set; }
        public IntPtr Pointer => _ptr;
    }

    /// Stands in for Vector3: a Unity value type is data, and binds like any struct.
    public struct VectorLike
    {
        public float x;
        public float y;
        public float z;
    }
}
