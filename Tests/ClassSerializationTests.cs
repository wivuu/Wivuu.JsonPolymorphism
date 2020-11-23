using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Xunit;
using System.Text.Json;

namespace Tests
{
    public enum GeographyAreaType
    {
        Area,
        County,
        Tract,
    }

    public abstract partial class GeographyArea
    {
        [JsonDiscriminator]
        public virtual GeographyAreaType AreaType { get; } = GeographyAreaType.Area;
        public string Prefix { get; init; } = "";
    }


    public class GeographyAreaArea
        : GeographyArea
    {
        public override GeographyAreaType AreaType => GeographyAreaType.Area;
    }

    public class GeographyCountyArea
        : GeographyArea
    {
        public override GeographyAreaType AreaType => GeographyAreaType.County;
    }


    public class GeographyTractArea
        : GeographyCountyArea
    {
        public override GeographyAreaType AreaType => GeographyAreaType.Tract;
    }


    public class ClassSerializationTests
    {
        [Fact]
        public void TestDeserializeCaseOrder()
        {
            var areas = new GeographyArea[] 
            {
                new GeographyCountyArea { Prefix = "551" },
                new GeographyAreaArea { Prefix = "552" },
                new GeographyTractArea { Prefix = "551442" },
            };

            var serialized = JsonSerializer.Serialize(areas);

            // Deserialized
            var deserialized = JsonSerializer.Deserialize<GeographyArea[]>(serialized);

            if (deserialized is null)
                throw new System.Exception("Unable to deserialize");

            Assert.Equal(areas.Length, deserialized.Length);

            for (var i = 0; i < areas.Length; ++i)
                Assert.Equal(areas[i].Prefix, deserialized[i].Prefix);
        }
    }
}
