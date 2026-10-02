// System
using System;
using System.Globalization;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ContentHashWriterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EMPTY_SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        private const float  FRACTION     = 1234.5678f;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Complete_EmptyInput_ReturnsKnownSha256()
        {
            Assert.AreEqual(EMPTY_SHA256, Hash(_ => { }));
        }

        [Test]
        public void Complete_DifferentStringBoundaries_ReturnsDifferentHashes()
        {
            string first  = Hash(writer =>
            {
                writer.WriteString("ab");
                writer.WriteString("c");
            });
            string second = Hash(writer =>
            {
                writer.WriteString("a");
                writer.WriteString("bc");
            });

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void Complete_NullAndEmptyStrings_ReturnsDifferentHashes()
        {
            Assert.AreNotEqual(Hash(writer => writer.WriteString(null)), Hash(writer => writer.WriteString(string.Empty)));
        }

        [Test]
        public void Complete_DifferentCultures_ReturnsSameHash()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                string first = Hash(WriteSample);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.AreEqual(first, Hash(WriteSample));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.AreEqual(first, Hash(WriteSample));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void WriteSingle_DifferentIeeeBits_ReturnsDifferentHashes()
        {
            float negativeZero = BitConverter.Int32BitsToSingle(int.MinValue);
            Assert.AreNotEqual(Hash(writer => writer.WriteSingle(0f)), Hash(writer => writer.WriteSingle(negativeZero)));
        }

        [Test]
        public void Complete_AfterComplete_RejectsFurtherWrites()
        {
            using (var writer = new ContentHashWriter())
            {
                writer.Complete();

                Assert.Throws<InvalidOperationException>(() => writer.WriteString("late"));
                Assert.Throws<InvalidOperationException>(() => writer.Complete());
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static string Hash(Action<ContentHashWriter> write)
        {
            using (var writer = new ContentHashWriter())
            {
                write(writer);
                return writer.Complete();
            }
        }

        private static void WriteSample(ContentHashWriter writer)
        {
            writer.WriteString("신시 Iıİi");
            writer.WriteInt32(int.MinValue);
            writer.WriteUInt64(ulong.MaxValue);
            writer.WriteSingle(FRACTION);
            writer.WriteBoolean(true);
        }
    }
}
