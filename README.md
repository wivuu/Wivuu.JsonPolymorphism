# JsonPolymorphism
[![Release](https://github.com/wivuu/Wivuu.JsonPolymorphism/actions/workflows/release.yml/badge.svg)](https://github.com/wivuu/Wivuu.JsonPolymorphism/actions/workflows/release.yml)

[![wivuu.jsonpolymorphism](https://img.shields.io/nuget/v/wivuu.jsonpolymorphism.svg?label=wivuu.jsonpolymorphism)](https://www.nuget.org/packages/Wivuu.JsonPolymorphism/)

Easily add System.Text.Json serialization support for polymorphic models in C# / .NET. Works by generating a `JsonConverter` tailored to your class/record at compile-time and attaching it via `JsonConverterAttribute`. All you need is a `discriminator` property on your base types marked with the new `JsonDiscriminator` attribute

## Install
Since this project relies on Source Code Generation, this project can only be used with C# (not F#) projects, built with the .NET 10 SDK or later.

```sh
dotnet add package Wivuu.JsonPolymorphism
```

## Usage

The `JsonDiscriminator` attribute can be attached to either a record parameter or class/record property. Once attached the type of that discriminator will be used to find all classes which derive from your base type. Any enum types without a corresponding inherited type will result in a compile-time error.

```C#
enum AnimalType
{
    Insect,
    Mammal,
    Reptile,
    Bird // <- This causes an easy to understand build error if it's missing a corresponding inherited type!
}

// My base type is 'Animal'
abstract partial record Animal( [JsonDiscriminator] AnimalType type, string Name );

// Animals with type = 'Insect' will automatically deserialize as `Insect`
record Insect(int NumLegs = 6, int NumEyes=4) : Animal(AnimalType.Insect, "Insectoid");

record Mammal(int NumNipples = 2) : Animal(AnimalType.Mammal, "Mammalian");

record Reptile(bool ColdBlooded = true) : Animal(AnimalType.Reptile, "Reptilian");
```

### Closed hierarchies (C# 15 / .NET 11)

Base types can be declared `closed`. Since a closed type can only be derived from in the same assembly, the generator checks every subtype at compile time: a concrete subtype with no corresponding enum member is a build error (`WIVUUJSONPOLY006`) instead of a "not a supported value" exception when serializing.

```C#
public closed partial record Animal([JsonDiscriminator] AnimalKind Kind);
public record Dog() : Animal(AnimalKind.Dog);
public record Cat() : Animal(AnimalKind.Cat);
```

## Serialize and Deserialize with ease!

Make sure to use consistent `JsonSerializerOptions` for serializing and deserializing. The discriminator property is found using the same name `System.Text.Json` writes for it, so `PropertyNamingPolicy` and `[JsonPropertyName]` (`[property: JsonPropertyName("...")]` on a record parameter) are respected; matching is case-insensitive when `PropertyNameCaseInsensitive` is set.

`System.Text.Json` writes base type properties last, so the discriminator usually comes at the end of each object. Adding `[property: JsonPropertyOrder(-1)]` to the discriminator writes it first, which lets deserialization find it without scanning the whole object. It's a build error when the discriminator isn't first (`WIVUUJSONPOLY007`); to make it a warning instead, or turn it off, set its severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.WIVUUJSONPOLY007.severity = warning # or none
```

```C#
JsonSerializerOptions options = new();

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
```
