using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

// Polymorphic types declared outside of any namespace
public enum GlobalPetKind
{
    Hamster,
    Parrot,
}

public abstract partial record GlobalPet([property: JsonPropertyOrder(-1)][JsonDiscriminator] GlobalPetKind Kind, string Name);

public record Hamster(string Name) : GlobalPet(GlobalPetKind.Hamster, Name);

public record Parrot(string Name, int Words) : GlobalPet(GlobalPetKind.Parrot, Name);

namespace Tests
{
    public class GlobalNamespaceTests
    {
        [Fact]
        public void TestGlobalNamespace()
        {
            GlobalPet[] pets = { new Hamster("Nibbles"), new Parrot("Polly", Words: 12) };

            var serialized   = JsonSerializer.Serialize(pets);
            var deserialized = JsonSerializer.Deserialize<GlobalPet[]>(serialized);

            Assert.Equal(pets, deserialized);
            Assert.Equal(typeof(Parrot), GlobalPet.GetType(GlobalPetKind.Parrot));
        }
    }
}
