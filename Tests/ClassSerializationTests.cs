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
        Some,
        Silly,
        Type,
        Names,
    }

    public abstract partial class GeographyArea
    {
        [JsonDiscriminator]
        public virtual GeographyAreaType AreaType { get; init; } = GeographyAreaType.Area;
        public string Prefix { get; init; } = "";
    }

    public class GeographyAreaArea
        : GeographyArea
    {
        public override GeographyAreaType AreaType => GeographyAreaType.Area;
    }

    public class GeographyCounty
        : GeographyArea
    {
        public override GeographyAreaType AreaType => GeographyAreaType.County;
    }

    public class GeographyTract
        : GeographyCounty
    {
        public override GeographyAreaType AreaType => GeographyAreaType.Tract;
    }

    [JsonDiscriminatorFallback]
    public class GeographyOther
        : GeographyArea
    {
        public override GeographyAreaType AreaType { get; init; }
    }

    public class ClassSerializationTests
    {
        [Fact]
        public void TestDeserializeCaseOrder()
        {
            var areas = new GeographyArea[] 
            {
                new GeographyCounty { Prefix = "551" },
                new GeographyAreaArea { Prefix = "552" },
                new GeographyTract { Prefix = "551442" },
                new GeographyOther { Prefix = "551442", AreaType = GeographyAreaType.Some },
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
