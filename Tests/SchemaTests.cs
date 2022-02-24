using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Namotion.Reflection;
using NJsonSchema;
using Xunit;

namespace Tests
{
    enum ProductType
    {
        Dairy,
        Fruit,
    }

    abstract partial record Product( [JsonDiscriminator] ProductType kind );

    record Dairy() : Product(ProductType.Dairy)
    {
        public DateTime BestBefore { get; set; }
    }

    record Fruit() : Product(ProductType.Fruit)
    {
        public int NumCalories { get; set; }
    }

    public class SchemaTests
    {
        [Fact]
        public void TestDiscriminatorResolution()
        {
            var typeAttributes = typeof(Product).GetCustomAttributes(false).OfType<Attribute>();

            dynamic jsonConverterAttribute = typeAttributes.FirstAssignableToTypeNameOrDefault(nameof(JsonConverterAttribute), TypeNameStyle.Name);

            if (jsonConverterAttribute != null)
            {
                var converterType = (Type)jsonConverterAttribute.ConverterType;
                if (converterType != null && (
                    converterType.IsAssignableToTypeName(nameof(NJsonSchema.Converters.JsonInheritanceConverter), TypeNameStyle.Name) || // Newtonsoft's converter
                    converterType.IsAssignableToTypeName(nameof(NJsonSchema.Converters.JsonInheritanceConverter) + "`1", TypeNameStyle.Name) // System.Text.Json's converter
                    ))
                {

                }
            }

            //// Act
            var schema = JsonSchema.FromType<Product>();
            var data = schema.ToJson();

            //// Assert
            Assert.NotNull(data);
        }
    }
}