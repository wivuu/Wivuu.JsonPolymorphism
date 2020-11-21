using System.Collections.Generic;
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

    enum MammalSpecies
    {
        Dog,
        Cat,
        Monkey,
    }

    abstract partial record Animal( [JsonDiscriminator] AnimalType type, string Name );

    // Animals
    record Insect(int NumLegs = 6, int NumEyes=4) : Animal(AnimalType.Insect, "Insectoid");
    partial record Mammal([JsonDiscriminator] MammalSpecies Species, int NumNipples = 2, string Name = "Mammalian") : Animal(AnimalType.Mammal, Name);
    record Reptile(bool ColdBlooded = true) : Animal(AnimalType.Reptile, "Reptilian");

    // Mammals
    record Dog(string Name) : Mammal(MammalSpecies.Dog, NumNipples: 8, Name);
    record Cat() : Mammal(MammalSpecies.Cat, NumNipples: 8);
    record Monkey() : Mammal(MammalSpecies.Monkey, NumNipples: 2);

    public class SerializationTests
    {
        [Theory]
        [MemberData(nameof(Options))]
        public void TestSerialization(JsonSerializerOptions options)
        {
            Animal[] animals = 
            {
                new Insect(NumLegs: 8, NumEyes: 6),
                new Dog(Name: "Fido"),
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

        public static IEnumerable<object[]> Options
        {
            get
            {
                // TestEnumCamelCaseSerialization
                {
                    JsonSerializerOptions options = new()
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    options.Converters.Add(new JsonStringEnumConverter());

                    yield return new [] { options };
                }

                // TestEnumPascalSerialization
                {
                    JsonSerializerOptions options = new();
                    options.Converters.Add(new JsonStringEnumConverter());
                    
                    yield return new [] { options };
                }

                // Test Defaults
                {
                    yield return new [] { new JsonSerializerOptions() };
                }
            }
        }
    }
}