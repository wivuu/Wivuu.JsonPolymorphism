# JsonPolymorphism
[![Nuget](https://github.com/wivuu/Wivuu.JsonPolymorphism/workflows/Nuget/badge.svg)](https://github.com/wivuu/Wivuu.JsonPolymorphism/actions?query=workflow%3ANuget)

[![wivuu.jsonpolymorphism](https://img.shields.io/nuget/v/wivuu.jsonpolymorphism.svg?label=wivuu.jsonpolymorphism)](https://www.nuget.org/packages/Wivuu.JsonPolymorphism/)

Easily add System.Text.Json serialization support for polymorphic models in C# / .NET 5. Works by generating a `JsonConverter` tailored to your class/record at compile-time and attaching it via `JsonConverterAttribute`. All you need is a `discriminator` property on your base types marked with the new `JsonDiscriminator` attribute

## Install
Since this project relies on Source Code Generation, this project can only be used with C# (not F#) projects targeting net5.0.

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

## Serialize and Deserialize with ease!

Make sure to use consistent `JsonSerializerOptions`; Currently the discriminator value when deserializing is case-sensitive.

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
