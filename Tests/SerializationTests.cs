using System.Text.Json;
using System.Text.Json.Serialization;
using Wivuu.JsonPolymorphism;
using Xunit;

namespace Tests
{
    enum AnimalType
    {
        Insect,
        Mammal,
        Reptile,
    }

    abstract partial record Animal( [JsonDiscriminator] AnimalType type, string Name );

    record Insect(int NumLegs = 6, int NumEyes=4) : Animal(AnimalType.Insect, "Insectoid");
    record Mammal(int NumNipples = 2) : Animal(AnimalType.Mammal, "Mammalian");
    record Reptile(bool ColdBlooded = true) : Animal(AnimalType.Reptile, "Reptilian");

    public class SerializationTests
    {
        [Fact]
        public void TestEnumSerialization()
        {
            JsonSerializerOptions options = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IgnoreNullValues     = true
            };

            options.Converters.Add(new JsonStringEnumConverter());
            
            Animal[] animals = 
            {
                new Insect(NumLegs: 8, NumEyes: 6),
                new Mammal(NumNipples: 6),
                new Reptile(ColdBlooded: false),
            };

            var serialized          = JsonSerializer.Serialize(animals, options);
            var animalsDeserialized = JsonSerializer.Deserialize<Animal[]>(serialized, options);
            
            if (animalsDeserialized is null)
                throw new System.Exception("Unable to deserialize");
                
            Assert.Equal(animals.Length, animalsDeserialized.Length);

            for (var i = 0; i < animals.Length; ++i)
                Assert.Equal(animals[i], animalsDeserialized[i]);
        }
    }
}