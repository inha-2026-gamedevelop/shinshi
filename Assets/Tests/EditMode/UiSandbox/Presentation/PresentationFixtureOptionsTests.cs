using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;

namespace Shinshi.Tests
{
    public sealed class PresentationFixtureOptionsTests
    {
        private const string FIELD_NAME = "_slots";

        /****************************************
        *               Defaults
        ****************************************/

        [Test]
        public void Constructor_Default_CentersWithNoOffsetAndNoExtras()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions();

            Assert.AreEqual(EFixtureAnchor.Center, options.Anchor);
            Assert.AreEqual(Vector2.zero, options.Offset);
            Assert.AreEqual(0, options.ChildButtons.Count);
            Assert.IsFalse(options.IsSkipped(FIELD_NAME));
        }

        [Test]
        public void Constructor_WithAnchor_KeepsAnchor()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.BottomRight);

            Assert.AreEqual(EFixtureAnchor.BottomRight, options.Anchor);
        }

        /****************************************
        *              Array length
        ****************************************/

        [Test]
        public void GetLength_UnsetField_ReturnsDefaultLength()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions();

            Assert.AreEqual(PresentationFixtureOptions.DEFAULT_ARRAY_LENGTH, options.GetLength(FIELD_NAME));
        }

        [Test]
        public void GetLength_AfterWithLength_ReturnsGivenLengthOnlyForThatField()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithLength(FIELD_NAME, 8);

            Assert.AreEqual(8, options.GetLength(FIELD_NAME));
            Assert.AreEqual(PresentationFixtureOptions.DEFAULT_ARRAY_LENGTH, options.GetLength("_other"));
        }

        [Test]
        public void WithLength_Negative_ClampsToZero()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithLength(FIELD_NAME, -3);

            Assert.AreEqual(0, options.GetLength(FIELD_NAME));
        }

        [Test]
        public void WithLength_CalledTwice_LastValueWins()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithLength(FIELD_NAME, 2).WithLength(FIELD_NAME, 6);

            Assert.AreEqual(6, options.GetLength(FIELD_NAME));
        }

        /****************************************
        *                 Skip
        ****************************************/

        [Test]
        public void Skip_NamedField_MarksOnlyThatFieldSkipped()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().Skip("_intro");

            Assert.IsTrue(options.IsSkipped("_intro"));
            Assert.IsFalse(options.IsSkipped("_outro"));
        }

        /****************************************
        *             Child buttons
        ****************************************/

        [Test]
        public void WithChildButtons_Count_IsExposedByFieldName()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithChildButtons("_panel", 3);

            Assert.AreEqual(1, options.ChildButtons.Count);
            Assert.AreEqual(3, options.ChildButtons["_panel"]);
        }

        [Test]
        public void WithChildButtons_Negative_ClampsToZero()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithChildButtons("_panel", -1);

            Assert.AreEqual(0, options.ChildButtons["_panel"]);
        }

        /****************************************
        *             Offset / chain
        ****************************************/

        [Test]
        public void WithOffset_XY_StoresOffset()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions().WithOffset(12.0f, -30.0f);

            Assert.AreEqual(new Vector2(12.0f, -30.0f), options.Offset);
        }

        [Test]
        public void Builders_Chained_ReturnSameInstance()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions();

            Assert.AreSame(options, options.WithOffset(1.0f, 2.0f));
            Assert.AreSame(options, options.WithLength(FIELD_NAME, 1));
            Assert.AreSame(options, options.Skip(FIELD_NAME));
            Assert.AreSame(options, options.WithChildButtons(FIELD_NAME, 1));
        }

        [Test]
        public void DefaultArrayLength_Constant_IsFour()
        {
            Assert.AreEqual(4, PresentationFixtureOptions.DEFAULT_ARRAY_LENGTH);
        }
    }
}
