using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace Tests
{
    public enum ShapeType
    {
        Circle,
        Rectangle,
    }

    // Multi-word discriminator name, so naming policies produce distinct names
    public abstract partial record Shape([JsonDiscriminator] ShapeType ShapeType);

    public record Circle(double Radius) : Shape(ShapeType.Circle);

    public record Rectangle(double Width, double Height) : Shape(ShapeType.Rectangle);

    public enum VehicleKind
    {
        Car,
        Truck,
    }

    public abstract partial record Vehicle([property: JsonPropertyName("vehicle_kind")][JsonDiscriminator] VehicleKind Kind);

    public record Car(int Seats) : Vehicle(VehicleKind.Car);

    public record Truck(double Payload) : Vehicle(VehicleKind.Truck);

    public class ReadTests
    {
        static readonly Shape[] Shapes =
        {
            new Circle(Radius: 1.5),
            new Rectangle(Width: 2, Height: 3),
        };

        public static IEnumerable<object?[]> NamingPolicies => new[]
        {
            new object?[] { null, "ShapeType" },
            new object?[] { JsonNamingPolicy.CamelCase, "shapeType" },
            new object?[] { JsonNamingPolicy.SnakeCaseLower, "shape_type" },
            new object?[] { JsonNamingPolicy.SnakeCaseUpper, "SHAPE_TYPE" },
            new object?[] { JsonNamingPolicy.KebabCaseLower, "shape-type" },
        };

        [Theory]
        [MemberData(nameof(NamingPolicies))]
        public void TestNamingPolicy(JsonNamingPolicy? policy, string expectedName)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = policy };

            var serialized = JsonSerializer.Serialize(Shapes, options);
            Assert.Contains($"\"{expectedName}\":", serialized);

            var deserialized = JsonSerializer.Deserialize<Shape[]>(serialized, options);
            Assert.Equal(Shapes, deserialized);
        }

        [Fact]
        public void TestCaseInsensitive()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var deserialized = JsonSerializer.Deserialize<Shape[]>(
                """[{"SHAPETYPE":0,"Radius":1.5},{"shapetype":1,"Width":2,"Height":3}]""", options);

            Assert.Equal(Shapes, deserialized);
        }

        [Fact]
        public void TestCaseSensitiveByDefault()
        {
            // Without PropertyNameCaseInsensitive the discriminator isn't found, which yields null
            var deserialized = JsonSerializer.Deserialize<Shape[]>("""[{"shapeType":0,"Radius":1.5}]""");

            Assert.Equal(new Shape?[] { null }, deserialized);
        }

        [Fact]
        public void TestWebDefaults()
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var serialized   = JsonSerializer.Serialize(Shapes, options);
            var deserialized = JsonSerializer.Deserialize<Shape[]>(serialized, options);

            Assert.Equal(Shapes, deserialized);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestJsonPropertyName(bool camelCase)
        {
            // An explicit [JsonPropertyName] is used as-is, regardless of naming policy
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null
            };

            Vehicle[] vehicles = { new Car(Seats: 4), new Truck(Payload: 1.5) };

            var serialized = JsonSerializer.Serialize(vehicles, options);
            Assert.Contains("\"vehicle_kind\":", serialized);

            var deserialized = JsonSerializer.Deserialize<Vehicle[]>(serialized, options);
            Assert.Equal(vehicles, deserialized);

            Assert.Equal("vehicle_kind", new VehicleConverter().DiscriminatorName);
        }

        [Theory]
        [InlineData("""{"ShapeType":1,"Width":2,"Height":3}""")]
        [InlineData("""{"Width":2,"ShapeType":1,"Height":3}""")]
        [InlineData("""{"Width":2,"Height":3,"ShapeType":1}""")]
        // Discriminator-named properties in nested values must not be matched
        [InlineData("""{"Meta":{"ShapeType":0,"Items":[{"ShapeType":0}]},"Width":2,"ShapeType":1,"Height":3}""")]
        public void TestDiscriminatorPosition(string json)
        {
            var deserialized = JsonSerializer.Deserialize<Shape>(json);

            Assert.Equal(new Rectangle(Width: 2, Height: 3), deserialized);
        }

        [Theory]
        [InlineData("""{"Radius":1.5}""")]
        [InlineData("""{"ShapeType":99,"Radius":1.5}""")]
        public void TestMissingOrUnknownDiscriminator(string json)
        {
            // The unrecognized object must be consumed entirely so the next element is read correctly
            var deserialized = JsonSerializer.Deserialize<Shape[]>($$"""[{{json}},{"ShapeType":0,"Radius":2}]""");

            Assert.Equal(new Shape?[] { null, new Circle(Radius: 2) }, deserialized);
        }

        [Fact]
        public void TestNotAnObject()
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Shape[]>("""["Circle"]"""));
        }

        [Fact]
        public async Task TestDeserializeFromStream()
        {
            // A tiny buffer means the converter is called with a reader that isn't on its final block
            var options = new JsonSerializerOptions { DefaultBufferSize = 1 };
            var items   = new List<Shape>();

            for (var i = 0; i < 100; ++i)
                items.Add(i % 2 == 0 ? new Circle(Radius: i) : new Rectangle(Width: i, Height: i * 2));

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items, options)));

            var deserialized = await JsonSerializer.DeserializeAsync<List<Shape>>(stream, options);

            Assert.Equal(items, deserialized);
        }

        [Fact]
        public void TestPropertyOrderMatchesSerializer()
        {
            // The first property System.Text.Json writes agrees with what WIVUUJSONPOLY007 predicts
            AssertFirstProperty("Kind", new Parrot("Polly", Words: 12));       // [JsonPropertyOrder(-1)]
            AssertFirstProperty("AreaType", new GeographyTract());             // discriminator overridden by subtype
            AssertFirstProperty("Species", new Dog("Fido"));                   // nested discriminator, no derived properties
            AssertFirstProperty("NumLegs", new Insect());                      // warned: 'NumLegs' is written before it
            AssertFirstProperty("Radius", new Circle(Radius: 1));              // warned: 'Radius' is written before it

            static void AssertFirstProperty<T>(string expected, T value)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    Assert.Equal(expected, property.Name);
                    return;
                }

                Assert.Fail("No properties were written");
            }
        }
    }
}
